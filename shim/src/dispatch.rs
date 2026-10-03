use crate::bus::Bus;
use crate::session::{self, Lock, Starting};
use std::ffi::{OsStr, OsString};
use std::fs::File;
use std::io::{self, Write};
use std::os::unix::ffi::OsStrExt;
use std::os::unix::io::AsRawFd;
use std::os::unix::net::{UnixListener, UnixStream};
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicUsize, Ordering};
use std::sync::{Arc, Mutex, PoisonError};
use std::thread;
use std::time::{Duration, Instant};

const SOCKET: &str = "bridge.sock";
const LOCK: &str = "bridge.lock";
const REACH_TIMEOUT: Duration = Duration::from_secs(5);
const OWN_DISPLAY: &[&str] = &["DISPLAY", "XAUTHORITY"];
const REFUSALS: &[&str] = &[".ServiceUnknown", ".AccessDenied", ".NameHasNoOwner"];

pub struct Dispatched(UnixStream);

impl Starting for Dispatched {
    fn ended(&mut self) -> Option<String> {
        if !session::readable(self.0.as_raw_fd()) {
            return None;
        }

        Some(match session::read_frame(&mut self.0) {
            Ok(payload) if payload.len() == 4 => format!(
                "the wine session gave up with {}",
                i32::from_le_bytes(payload[..4].try_into().unwrap_or_default())
            ),
            _ => "Cabinet's bridge went away while starting the wine session".to_string(),
        })
    }
}

pub fn request(socket: &OsStr, forwarded: &[(&str, OsString)]) -> Vec<OsString> {
    let mut request = vec![socket.to_os_string()];

    request.extend(forwarded.iter().map(|(var, value)| {
        let mut pair = OsString::from(var);
        pair.push("=");
        pair.push(value);
        pair
    }));

    request
}

pub fn submit<E>(getenv: E, directory: &Path, request: &[OsString]) -> io::Result<Dispatched>
where
    E: Fn(&str) -> Option<OsString>,
{
    let mut bus = Bus::session(getenv)?;
    let lock = File::create(directory.join(LOCK))?;
    let mut deadline = None;

    loop {
        match bus.start_service(crate::BRIDGE) {
            Ok(()) => {}
            Err(error) if refused(&error) => return Err(error),
            Err(error) if deadline.is_some_and(|deadline| Instant::now() >= deadline) => {
                return Err(error)
            }
            Err(_) => {}
        }
        let deadline = *deadline.get_or_insert_with(|| Instant::now() + REACH_TIMEOUT);

        if let Some(_guard) = Lock::try_hold(lock.as_raw_fd()) {
            match UnixStream::connect(directory.join(SOCKET)) {
                Ok(mut stream) => {
                    stream.set_read_timeout(Some(REACH_TIMEOUT))?;
                    stream.set_write_timeout(Some(REACH_TIMEOUT))?;
                    session::write_frame(&mut stream, &session::encode(request))?;
                    return Ok(Dispatched(stream));
                }
                Err(error) if Instant::now() >= deadline => return Err(error),
                Err(_) => {}
            }
        } else if Instant::now() >= deadline {
            return Err(io::Error::other("Cabinet's bridge stayed busy"));
        }

        thread::sleep(session::TICK);
    }
}

fn refused(error: &io::Error) -> bool {
    let named = error.to_string();
    REFUSALS.iter().any(|refusal| named.ends_with(refusal))
}

pub fn serve(directory: &Path) -> i32 {
    let path = directory.join(SOCKET);

    if UnixStream::connect(&path).is_ok() {
        note(&format!("cabinet-wine: {path:?} already has a dispatcher"));
        return 1;
    }

    let _ = std::fs::remove_file(&path);
    let listener = match UnixListener::bind(&path) {
        Ok(listener) => listener,
        Err(error) => {
            note(&format!("cabinet-wine: cannot serve {path:?}: {error}"));
            return 1;
        }
    };
    let identity = session::file_identity(&path);

    let owned = Bus::session(|var| std::env::var_os(var))
        .and_then(|mut bus| bus.own(crate::BRIDGE).map(|()| bus));
    let (mut bus, lock) = match (owned, File::create(directory.join(LOCK))) {
        (Ok(bus), Ok(lock)) if listener.set_nonblocking(true).is_ok() => (Some(bus), lock),
        (owned, _) => {
            if let Err(error) = owned {
                note(&format!(
                    "cabinet-wine: cannot own {}: {error}",
                    crate::BRIDGE
                ));
            }
            session::remove_if_unchanged(&path, identity);
            return 1;
        }
    };

    let live = Arc::new(AtomicUsize::new(0));
    let activity = Arc::new(Mutex::new(Instant::now()));

    loop {
        match listener.accept() {
            Ok((stream, _)) => admit(stream, directory, &live, &activity),
            Err(error) if error.kind() == io::ErrorKind::WouldBlock => {
                if let Some(connection) = bus.as_mut() {
                    if session::readable(connection.fd()) && connection.discard().is_err() {
                        bus = None;
                    }
                }

                let idle = activity
                    .lock()
                    .unwrap_or_else(PoisonError::into_inner)
                    .elapsed();

                if live.load(Ordering::SeqCst) == 0 && (idle >= session::WATCH || bus.is_none()) {
                    if let Some(_guard) = Lock::try_hold(lock.as_raw_fd()) {
                        if let Ok((stream, _)) = listener.accept() {
                            admit(stream, directory, &live, &activity);
                            continue;
                        }

                        session::remove_if_unchanged(&path, identity);
                        return 0;
                    }
                }

                let mut watched = vec![listener.as_raw_fd()];
                watched.extend(bus.as_ref().map(Bus::fd));
                session::wait_readable(&watched, Some(session::WATCH));
            }
            Err(error) => {
                note(&format!("cabinet-wine: cannot accept on {path:?}: {error}"));
                session::wait_readable(&[], Some(session::WATCH));
            }
        }
    }
}

fn admit(
    stream: UnixStream,
    directory: &Path,
    live: &Arc<AtomicUsize>,
    activity: &Arc<Mutex<Instant>>,
) {
    live.fetch_add(1, Ordering::SeqCst);
    let directory = directory.to_path_buf();
    let live = Arc::clone(live);
    let activity = Arc::clone(activity);

    thread::spawn(move || {
        start_broker(stream, &directory);
        *activity.lock().unwrap_or_else(PoisonError::into_inner) = Instant::now();
        live.fetch_sub(1, Ordering::SeqCst);
    });
}

fn start_broker(mut stream: UnixStream, directory: &Path) {
    let _ = stream.set_nonblocking(false);
    let _ = stream.set_read_timeout(Some(REACH_TIMEOUT));
    let read = |path: &Path| std::fs::read_to_string(path).ok();

    let started = session::read_frame(&mut stream)
        .ok()
        .and_then(|payload| session::decode(&payload))
        .and_then(|request| broker(&request, directory, &read))
        .ok_or_else(|| io::Error::other("a malformed request"))
        .and_then(|(argv, environment, log)| {
            let mut argv = argv;
            argv.insert(0, std::env::current_exe()?.into_os_string());
            crate::start(&argv, &environment, &log)
        });

    let status = match started {
        Ok(mut child) => child.wait().map(session::exit_code).unwrap_or(127),
        Err(error) => {
            note(&format!(
                "cabinet-wine: cannot start a wine session: {error}"
            ));
            127
        }
    };

    let _ = session::write_frame(&mut stream, &status.to_le_bytes());
}

type Broker = (Vec<OsString>, Vec<(OsString, OsString)>, PathBuf);

fn broker<R>(request: &[OsString], directory: &Path, read: &R) -> Option<Broker>
where
    R: Fn(&Path) -> Option<String>,
{
    let (socket, pairs) = request.split_first()?;
    let socket = Path::new(socket);
    let name = socket.file_name()?.as_bytes();

    if socket.parent() != Some(directory)
        || !name.starts_with(b"cabinet-")
        || !name.ends_with(b".sock")
    {
        return None;
    }

    let given: Vec<(&'static str, OsString)> = pairs
        .iter()
        .filter_map(|pair| {
            let (var, value) = pair
                .as_bytes()
                .split_at(pair.as_bytes().iter().position(|b| *b == b'=')?);
            let var = crate::FORWARD.iter().find(|forwarded| {
                forwarded.as_bytes() == var && !OWN_DISPLAY.contains(forwarded)
            })?;
            Some((*var, OsStr::from_bytes(&value[1..]).to_os_string()))
        })
        .collect();

    let prefix = given
        .iter()
        .find(|(var, _)| *var == "WINEPREFIX")
        .map(|(_, value)| value.clone());
    let wine = crate::wine_command(prefix.as_deref(), read);

    let mut environment: Vec<(OsString, OsString)> = given
        .into_iter()
        .map(|(var, value)| (OsString::from(var), value))
        .collect();
    environment.extend(
        crate::BLANKED
            .iter()
            .map(|var| (var.into(), OsString::new())),
    );
    environment.extend(
        crate::FORCED
            .iter()
            .map(|(var, value)| (var.into(), value.into())),
    );
    environment.extend(
        crate::sync_environment(prefix.as_deref(), read)
            .into_iter()
            .map(|(var, value)| (var.into(), value.into())),
    );

    Some((
        vec![crate::INNER_MODE.into(), socket.into(), wine],
        environment,
        socket.with_extension("log"),
    ))
}

fn note(told: &str) {
    let _ = writeln!(io::stderr(), "{told}");
}

#[cfg(test)]
mod tests {
    use super::*;

    const DIRECTORY: &str = "/run/user/1/yabridge";
    const PREFIX: &str = "/home/u/.var/app/io.github.mark12870.cabinet/data/prefixes/vendor";

    fn markers(path: &Path) -> Option<String> {
        match path.file_name()?.to_str()? {
            ".cabinet-runner" => Some("staging\n".into()),
            ".cabinet-sync" => Some("ntsync\n".into()),
            _ => None,
        }
    }

    fn requested(pairs: &[&str]) -> Vec<OsString> {
        let mut request = vec![OsString::from(format!("{DIRECTORY}/cabinet-0.sock"))];
        request.extend(pairs.iter().map(OsString::from));
        request
    }

    fn value<'a>(environment: &'a [(OsString, OsString)], var: &str) -> Option<&'a OsString> {
        environment
            .iter()
            .find(|(name, _)| name == var)
            .map(|(_, value)| value)
    }

    #[test]
    fn a_request_carries_the_socket_then_each_forwarded_variable() {
        let request = request(
            OsStr::new("/run/s.sock"),
            &[
                ("WINEPREFIX", OsString::from("/p")),
                ("LANG", OsString::from("C")),
            ],
        );

        assert_eq!(request, ["/run/s.sock", "WINEPREFIX=/p", "LANG=C"]);
    }

    #[test]
    fn the_dispatcher_takes_the_runner_and_sync_mode_from_the_prefix() {
        let (argv, environment, log) = broker(
            &requested(&[&format!("WINEPREFIX={PREFIX}"), "WINEFSYNC=1"]),
            Path::new(DIRECTORY),
            &markers,
        )
        .unwrap();

        assert_eq!(
            argv[2],
            "/home/u/.var/app/io.github.mark12870.cabinet/data/runners/staging/bin/wine"
        );
        assert_eq!(value(&environment, "WINENTSYNC").unwrap(), "1");
        assert_eq!(
            environment
                .iter()
                .rev()
                .find(|(name, _)| name == "WINEFSYNC")
                .unwrap()
                .1,
            "0"
        );
        assert_eq!(log, Path::new(DIRECTORY).join("cabinet-0.log"));
    }

    #[test]
    fn the_dispatcher_drops_what_it_does_not_forward_and_keeps_its_own_display() {
        let (_, environment, _) = broker(
            &requested(&[
                "LD_PRELOAD=/evil.so",
                "DISPLAY=:7",
                "XAUTHORITY=/x",
                "LANG=C",
            ]),
            Path::new(DIRECTORY),
            &markers,
        )
        .unwrap();

        assert_eq!(value(&environment, "LANG").unwrap(), "C");
        assert_eq!(value(&environment, "LD_PRELOAD"), None);
        assert_eq!(value(&environment, "DISPLAY"), None);
        assert_eq!(value(&environment, "XAUTHORITY"), None);
        assert_eq!(value(&environment, "WAYLAND_DISPLAY").unwrap(), "");
        assert_eq!(value(&environment, "YABRIDGE_NO_WATCHDOG").unwrap(), "1");
    }

    #[test]
    fn the_dispatcher_starts_sessions_only_in_its_own_socket_directory() {
        let elsewhere = [OsString::from("/tmp/cabinet-0.sock")];
        let foreign = [OsString::from(format!("{DIRECTORY}/other.sock"))];

        assert!(broker(&elsewhere, Path::new(DIRECTORY), &markers).is_none());
        assert!(broker(&foreign, Path::new(DIRECTORY), &markers).is_none());
    }
}
