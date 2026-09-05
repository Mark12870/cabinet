use std::env;
use std::ffi::{OsStr, OsString};
use std::io;
use std::os::unix::ffi::OsStrExt;
use std::os::unix::process::CommandExt;
use std::path::{Path, PathBuf};
use std::process::Command;

mod session;
mod x11;

const DEFAULT_APP: &str = "io.github.mark12870.cabinet";
const DESKTOP_TITLE: &str = "Wine Desktop";
const INNER_COMMAND: &str = "/app/lib/yabridge/cabinet-wine";
const INNER_MODE: &str = "--cabinet-inner";
const JOIN_MODE: &str = "--cabinet-join";
const SESSION_MODE: &str = "--cabinet-session";
const NO_SESSION: i32 = 1;
const SESSION_LIVE: &str = "live";
const SOCKET_DIR: &str = "yabridge";

const FORWARD: &[&str] = &[
    "WINEPREFIX",
    "WINEDLLPATH",
    "WINEDLLOVERRIDES",
    "WINEDEBUG",
    "WINEFSYNC",
    "WINEESYNC",
    "YABRIDGE_TEMP_DIR",
    "YABRIDGE_NO_WATCHDOG",
    "YABRIDGE_DEBUG_FILE",
    "YABRIDGE_DEBUG_LEVEL",
    "DISPLAY",
    "XAUTHORITY",
    "LANG",
    "LC_ALL",
];

const CANON_VARS: &[&str] = &[
    "WINEPREFIX",
    "YABRIDGE_TEMP_DIR",
    "YABRIDGE_DEBUG_FILE",
    "XAUTHORITY",
];

const CANON_LIST_VARS: &[&str] = &["WINEDLLPATH"];

const BLANKED: &[&str] = &["WAYLAND_DISPLAY"];

const RUNNER_MARKER: &str = ".cabinet-runner";
const SYNC_MARKER: &str = ".cabinet-sync";
const ENV_MARKER: &str = ".cabinet-env";
const BUNDLED_RUNNER: &str = "bundled";

const SYNC_MODES: &[(&str, [&str; 3])] = &[
    ("esync", ["1", "0", "0"]),
    ("fsync", ["0", "1", "0"]),
    ("ntsync", ["0", "0", "1"]),
];

const SYNC_VARS: [&str; 3] = ["WINEESYNC", "WINEFSYNC", "WINENTSYNC"];

const CABINET_OWNED: &[&str] = &[
    "WINEPREFIX",
    "WINELOADER",
    "WINEDLLPATH",
    "YABRIDGE_TEMP_DIR",
];

fn wine_command<R>(prefix: Option<&OsStr>, read: &R) -> OsString
where
    R: Fn(&Path) -> Option<String>,
{
    let fallback = || OsString::from("wine");

    let Some(prefix) = prefix else {
        return fallback();
    };
    let prefix = Path::new(prefix);

    let Some(name) = read(&prefix.join(RUNNER_MARKER)) else {
        return fallback();
    };
    let name = name.trim();

    if name.is_empty() || name == BUNDLED_RUNNER || name.contains('/') {
        return fallback();
    }

    match prefix.parent().and_then(Path::parent) {
        Some(data) => data
            .join("runners")
            .join(name)
            .join("bin")
            .join("wine")
            .into_os_string(),
        None => fallback(),
    }
}

fn sync_flags<R>(prefix: Option<&OsStr>, read: &R) -> Vec<OsString>
where
    R: Fn(&Path) -> Option<String>,
{
    let Some(recorded) = prefix.and_then(|p| read(&Path::new(p).join(SYNC_MARKER))) else {
        return Vec::new();
    };
    let recorded = recorded.trim();

    let Some((_, values)) = SYNC_MODES.iter().find(|(word, _)| *word == recorded) else {
        return Vec::new();
    };

    SYNC_VARS
        .iter()
        .zip(values)
        .map(|(var, value)| OsString::from(format!("--env={var}={value}")))
        .collect()
}

fn env_flags<R>(prefix: Option<&OsStr>, read: &R) -> Vec<OsString>
where
    R: Fn(&Path) -> Option<String>,
{
    let Some(recorded) = prefix.and_then(|p| read(&Path::new(p).join(ENV_MARKER))) else {
        return Vec::new();
    };

    recorded
        .lines()
        .filter_map(|line| {
            let line = line.trim();
            if line.starts_with('#') {
                return None;
            }
            let (key, value) = line.split_once('=')?;
            let key = key.trim_end();
            if key.is_empty() || CABINET_OWNED.contains(&key) {
                return None;
            }
            Some(OsString::from(format!("--env={key}={value}")))
        })
        .collect()
}

fn canonicalize<C>(value: &OsStr, canon: &C) -> OsString
where
    C: Fn(&Path) -> Option<PathBuf>,
{
    if value.as_bytes().first() != Some(&b'/') {
        return value.to_os_string();
    }
    match canon(Path::new(value)) {
        Some(resolved) => resolved.into_os_string(),
        None => value.to_os_string(),
    }
}

fn canonicalize_list<C>(value: &OsStr, canon: &C) -> OsString
where
    C: Fn(&Path) -> Option<PathBuf>,
{
    let mut out = OsString::new();
    for segment in value.as_bytes().split(|b| *b == b':') {
        if segment.is_empty() {
            continue;
        }
        if !out.is_empty() {
            out.push(":");
        }
        out.push(canonicalize(OsStr::from_bytes(segment), canon));
    }
    out
}

fn build_argv<E, C, R>(
    app: &str,
    in_sandbox: bool,
    socket: &OsStr,
    getenv: E,
    canon: C,
    read: R,
) -> Vec<OsString>
where
    E: Fn(&str) -> Option<OsString>,
    C: Fn(&Path) -> Option<PathBuf>,
    R: Fn(&Path) -> Option<String>,
{
    let mut argv: Vec<OsString> = Vec::with_capacity(FORWARD.len() + 8);

    if in_sandbox {
        argv.push("flatpak-spawn".into());
        argv.push("--host".into());
    }

    argv.push("flatpak".into());
    argv.push("run".into());
    argv.push(format!("--command={INNER_COMMAND}").into());

    let prefix = getenv("WINEPREFIX").map(|value| canonicalize(&value, &canon));
    let wine = wine_command(prefix.as_deref(), &read);

    for var in FORWARD {
        let Some(value) = getenv(var) else { continue };
        if value.is_empty() {
            continue;
        }

        let value = if CANON_LIST_VARS.contains(var) {
            canonicalize_list(&value, &canon)
        } else if CANON_VARS.contains(var) {
            canonicalize(&value, &canon)
        } else {
            value
        };

        let mut flag = OsString::from("--env=");
        flag.push(var);
        flag.push("=");
        flag.push(&value);
        argv.push(flag);
    }

    for var in BLANKED {
        let mut flag = OsString::from("--env=");
        flag.push(var);
        flag.push("=");
        argv.push(flag);
    }

    argv.extend(sync_flags(prefix.as_deref(), &read));
    argv.extend(env_flags(prefix.as_deref(), &read));

    argv.push(app.into());
    argv.push(INNER_MODE.into());
    argv.push(socket.to_os_string());
    argv.push(wine);

    argv
}

fn job_args<C>(args: &[OsString], canon: &C) -> Vec<OsString>
where
    C: Fn(&Path) -> Option<PathBuf>,
{
    args.iter().map(|arg| canonicalize(arg, canon)).collect()
}

fn inner_argv(socket: &OsStr, wine: OsString) -> Vec<OsString> {
    [
        INNER_COMMAND.into(),
        INNER_MODE.into(),
        socket.to_os_string(),
        wine,
    ]
    .into()
}

fn session_dir<E, C>(getenv: &E, canon: &C) -> PathBuf
where
    E: Fn(&str) -> Option<OsString>,
    C: Fn(&Path) -> Option<PathBuf>,
{
    let given = match getenv("YABRIDGE_TEMP_DIR").filter(|value| !value.is_empty()) {
        Some(given) => PathBuf::from(given),
        None => match getenv("XDG_RUNTIME_DIR").filter(|value| !value.is_empty()) {
            Some(runtime) => PathBuf::from(runtime).join(SOCKET_DIR),
            None => PathBuf::from("/tmp").join(SOCKET_DIR),
        },
    };

    let _ = std::fs::create_dir_all(&given);

    PathBuf::from(canonicalize(given.as_os_str(), canon))
}

fn main() {
    let app = env::var("CABINET_APP").unwrap_or_else(|_| DEFAULT_APP.to_string());
    let args: Vec<OsString> = env::args_os().skip(1).collect();

    if args.first().is_some_and(|arg| arg == "--cabinet-self-test") {
        println!("cabinet-wine {} ok", env!("CARGO_PKG_VERSION"));
        return;
    }

    if args.first().is_some_and(|arg| arg == INNER_MODE) {
        std::process::exit(session::run_broker(&args[1..]));
    }

    let getenv = |key: &str| env::var_os(key);
    let canon = |path: &Path| std::fs::canonicalize(path).ok();
    let read = |path: &Path| std::fs::read_to_string(path).ok();

    let mode = args
        .first()
        .filter(|arg| *arg == JOIN_MODE || *arg == SESSION_MODE)
        .cloned();
    let rest = if mode.is_some() {
        &args[1..]
    } else {
        &args[..]
    };

    let job = job_args(rest, &canon);
    let prefix = getenv("WINEPREFIX").map(|value| canonicalize(&value, &canon));
    let seed = prefix
        .clone()
        .or_else(|| job.first().cloned())
        .unwrap_or_default();
    let name = session::key(&seed);
    let directory = session_dir(&getenv, &canon);
    let socket = session::socket_path(&directory, &name);
    let lock = session::lock_path(&directory, &name);
    let busy = session::busy_path(&directory, &name);
    let in_cabinet = env::var_os("FLATPAK_ID").as_deref() == Some(OsStr::new(DEFAULT_APP));

    if mode.as_deref() == Some(OsStr::new(SESSION_MODE)) {
        if !session::live(&socket) {
            std::process::exit(NO_SESSION);
        }

        println!("{SESSION_LIVE}");
        std::process::exit(0);
    }

    if mode.as_deref() == Some(OsStr::new(JOIN_MODE)) {
        match session::join(&socket, &job) {
            Ok(Some(status)) => std::process::exit(status),
            Ok(None) => {}
            Err(error) => {
                eprintln!("cabinet-wine: cannot join the Wine session {socket:?}: {error}");
                std::process::exit(127);
            }
        }
    }

    let Some(_claimed) = session::claim(&busy) else {
        eprintln!(
            "cabinet-wine: Cabinet has an app open in this prefix; \
             close it before loading its plugins"
        );
        std::process::exit(127);
    };

    let argv = if in_cabinet {
        inner_argv(socket.as_os_str(), wine_command(prefix.as_deref(), &read))
    } else {
        build_argv(
            &app,
            Path::new("/.flatpak-info").exists(),
            socket.as_os_str(),
            getenv,
            canon,
            read,
        )
    };

    if let Some(path) = env::var_os("CABINET_SHIM_LOG") {
        use std::io::Write;
        if let Ok(mut log) = std::fs::OpenOptions::new()
            .create(true)
            .append(true)
            .open(path)
        {
            let _ = writeln!(log, "{argv:?}\n{job:?}");
        }
    }

    match session::submit(&socket, &lock, &job, || start(&argv)) {
        Ok(status) => std::process::exit(status),
        Err(error) => {
            eprintln!("cabinet-wine: cannot reach the Wine session {socket:?}: {error}");
            std::process::exit(127);
        }
    }
}

fn start(argv: &[OsString]) -> io::Result<std::process::Child> {
    let mut command = Command::new(&argv[0]);
    command.args(&argv[1..]);

    unsafe {
        command.pre_exec(|| {
            if setsid() == -1 {
                return Err(io::Error::last_os_error());
            }

            Ok(())
        });
    }

    command.spawn()
}

extern "C" {
    fn setsid() -> i32;
}

#[cfg(test)]
mod tests {
    use super::*;

    fn fake_canon(path: &Path) -> Option<PathBuf> {
        let masked = "/home/u/.var/app/fm.reaper.Reaper/data/yabridge";
        let real = "/home/u/.local/share/yabridge";
        let text = path.to_str()?;
        text.starts_with(masked)
            .then(|| PathBuf::from(text.replacen(masked, real, 1)))
    }

    fn no_env(_: &str) -> Option<OsString> {
        None
    }

    const SOCKET: &str = "/run/user/1000/yabridge/cabinet-0123456789abcdef.sock";

    fn build(env: &[(&str, &str)], in_sandbox: bool) -> Vec<String> {
        build_with_runner(env, in_sandbox, None)
    }

    fn build_with_runner(
        env: &[(&str, &str)],
        in_sandbox: bool,
        runner: Option<&str>,
    ) -> Vec<String> {
        build_with_markers(env, in_sandbox, &[(RUNNER_MARKER, runner)])
    }

    fn build_with_markers(
        env: &[(&str, &str)],
        in_sandbox: bool,
        markers: &[(&str, Option<&str>)],
    ) -> Vec<String> {
        let env: Vec<(String, String)> = env
            .iter()
            .map(|(k, v)| (k.to_string(), v.to_string()))
            .collect();
        let markers: Vec<(String, Option<String>)> = markers
            .iter()
            .map(|(name, body)| (name.to_string(), body.map(str::to_string)))
            .collect();

        build_argv(
            "io.github.mark12870.cabinet",
            in_sandbox,
            OsStr::new(SOCKET),
            |key| {
                env.iter()
                    .find(|(k, _)| k == key)
                    .map(|(_, v)| OsString::from(v))
            },
            fake_canon,
            |path| {
                let name = path.file_name()?.to_str()?;
                markers
                    .iter()
                    .find(|(marker, _)| marker == name)
                    .and_then(|(_, body)| body.clone())
            },
        )
        .iter()
        .map(|a| a.to_string_lossy().into_owned())
        .collect()
    }

    fn canonical(args: &[&str]) -> Vec<String> {
        let args: Vec<OsString> = args.iter().map(OsString::from).collect();
        job_args(&args, &fake_canon)
            .iter()
            .map(|a| a.to_string_lossy().into_owned())
            .collect()
    }

    #[test]
    fn native_daw_does_not_hop_through_the_host() {
        let argv = build(&[], false);
        assert_eq!(argv[0], "flatpak");
        assert_eq!(
            &argv[..3],
            ["flatpak", "run", "--command=/app/lib/yabridge/cabinet-wine"]
        );
    }

    #[test]
    fn sandboxed_daw_hops_through_the_host() {
        let argv = build(&[], true);
        assert_eq!(&argv[..3], &["flatpak-spawn", "--host", "flatpak"]);
    }

    #[test]
    fn the_inner_command_keeps_the_runner_out_of_flatpak_options() {
        let argv = build(&[], false);
        let app = argv.iter().position(|arg| arg == DEFAULT_APP).unwrap();

        assert_eq!(&argv[app + 1..], &[INNER_MODE, SOCKET, "wine"]);
    }

    #[test]
    fn cabinet_starts_its_broker_without_an_extra_sandbox() {
        let argv: Vec<String> = inner_argv(OsStr::new(SOCKET), OsString::from("wine"))
            .iter()
            .map(|arg| arg.to_string_lossy().into_owned())
            .collect();

        assert_eq!(argv, [INNER_COMMAND, INNER_MODE, SOCKET, "wine"]);
    }

    #[test]
    fn a_wine_session_outlives_the_shim_that_started_it() {
        for in_sandbox in [false, true] {
            let argv = build(&[], in_sandbox);
            assert!(!argv.iter().any(|a| a == "--die-with-parent"), "{argv:?}");
            assert!(!argv.iter().any(|a| a == "--watch-bus"), "{argv:?}");
        }
    }

    #[test]
    fn one_prefix_is_one_session() {
        let one = session::key(OsStr::new(PREFIX));
        let again = session::key(OsStr::new(PREFIX));
        let other = session::key(OsStr::new("/home/u/prefixes/klevgrand"));

        assert_eq!(one, again);
        assert_ne!(one, other);
        assert!(one.starts_with("cabinet-"));
    }

    #[test]
    fn a_session_socket_and_lock_share_one_name() {
        let directory = Path::new("/run/user/1000/yabridge");
        let name = session::key(OsStr::new(PREFIX));

        assert_eq!(
            session::socket_path(directory, &name).with_extension("lock"),
            session::lock_path(directory, &name)
        );
    }

    #[test]
    fn a_busy_marker_shares_the_sessions_name() {
        let directory = Path::new("/run/user/1000/yabridge");
        let name = session::key(OsStr::new(PREFIX));

        assert_eq!(
            session::socket_path(directory, &name).with_extension("busy"),
            session::busy_path(directory, &name)
        );
    }

    #[test]
    fn joining_a_prefix_with_no_session_reports_no_session() {
        let socket = Path::new("/run/user/1000/yabridge/cabinet-nothing-here.sock");

        assert!(matches!(session::join(socket, &[]), Ok(None)));
    }

    #[test]
    fn a_busy_marker_takes_a_lock_plugin_runs_can_share() {
        let marker = std::env::temp_dir().join(format!("cabinet-busy-{}.busy", std::process::id()));
        let _ = std::fs::remove_file(&marker);

        let claimed = session::claim(&marker).expect("the marker can be claimed");
        let shared = session::claim(&marker).expect("a second local run shares it");

        drop(shared);
        drop(claimed);

        let _ = std::fs::remove_file(&marker);
    }

    #[test]
    fn a_job_survives_the_round_trip_to_the_session() {
        let argv: Vec<OsString> = ["host.exe.so", "vst3", "/run/user/1000/yabridge/sock"]
            .iter()
            .map(OsString::from)
            .collect();

        assert_eq!(session::decode(&session::encode(&argv)), Some(argv));
    }

    #[test]
    fn a_truncated_job_is_refused() {
        let argv: Vec<OsString> = vec![OsString::from("host.exe.so")];
        let payload = session::encode(&argv);

        assert_eq!(session::decode(&payload[..payload.len() - 1]), None);
        assert_eq!(session::decode(&[]), None);
    }

    #[test]
    fn only_set_and_non_empty_variables_are_forwarded() {
        let argv = build(&[("WINEDEBUG", "-all"), ("WINEESYNC", "")], false);
        assert!(argv.iter().any(|a| a == "--env=WINEDEBUG=-all"));
        assert!(!argv.iter().any(|a| a.starts_with("--env=WINEESYNC")));
    }

    #[test]
    fn masked_paths_are_resolved_in_a_job() {
        let job =
            canonical(&["/home/u/.var/app/fm.reaper.Reaper/data/yabridge/yabridge-host.exe.so"]);
        assert_eq!(
            job.last().unwrap(),
            "/home/u/.local/share/yabridge/yabridge-host.exe.so"
        );
    }

    #[test]
    fn masked_paths_are_resolved_in_env() {
        let argv = build(
            &[(
                "WINEPREFIX",
                "/home/u/.var/app/fm.reaper.Reaper/data/yabridge/pfx",
            )],
            false,
        );
        assert!(argv
            .iter()
            .any(|a| a == "--env=WINEPREFIX=/home/u/.local/share/yabridge/pfx"));
    }

    #[test]
    fn path_lists_are_resolved_segment_by_segment_and_emptied_of_gaps() {
        let argv = build(
            &[(
                "WINEDLLPATH",
                "/home/u/.var/app/fm.reaper.Reaper/data/yabridge:/opt/wine:",
            )],
            false,
        );
        assert!(argv
            .iter()
            .any(|a| a == "--env=WINEDLLPATH=/home/u/.local/share/yabridge:/opt/wine"));
    }

    #[test]
    fn bare_words_are_never_treated_as_paths() {
        assert_eq!(
            canonical(&["vst3", "group", "./relative"]),
            ["vst3", "group", "./relative"]
        );
    }

    #[test]
    fn unresolvable_absolute_paths_are_passed_through_unchanged() {
        assert_eq!(
            canonical(&["/run/user/1000/yabridge/sock"]),
            ["/run/user/1000/yabridge/sock"]
        );
    }

    #[test]
    fn the_app_id_separates_flatpak_options_from_the_session() {
        let argv = build(&[("WINEDEBUG", "-all")], false);
        let app = argv
            .iter()
            .position(|a| a == "io.github.mark12870.cabinet")
            .expect("app id present");
        assert!(argv[..app]
            .iter()
            .all(|a| a.starts_with("--") || a == "flatpak" || a == "run"));
        assert_eq!(&argv[app + 1..], &[INNER_MODE, SOCKET, "wine"]);
    }

    #[test]
    fn an_empty_environment_still_produces_a_runnable_command() {
        let argv = build_argv(
            "some.App",
            false,
            OsStr::new(SOCKET),
            no_env,
            fake_canon,
            |_| None,
        );
        let argv: Vec<String> = argv
            .iter()
            .map(|a| a.to_string_lossy().into_owned())
            .collect();
        assert_eq!(
            argv,
            vec![
                "flatpak",
                "run",
                "--command=/app/lib/yabridge/cabinet-wine",
                "--env=WAYLAND_DISPLAY=",
                "some.App",
                INNER_MODE,
                SOCKET,
                "wine"
            ]
        );
    }

    const PREFIX: &str = "/home/u/.var/app/io.github.mark12870.cabinet/data/prefixes/serum";

    #[test]
    fn a_prefix_with_no_marker_uses_the_bundled_wine() {
        let argv = build_with_runner(&[("WINEPREFIX", PREFIX)], false, None);
        assert!(argv.iter().any(|a| a == "wine"));
    }

    #[test]
    fn a_prefix_naming_a_runner_starts_that_runners_wine() {
        let argv = build_with_runner(&[("WINEPREFIX", PREFIX)], false, Some("wine-9.21\n"));
        assert!(argv
            .iter()
            .any(|a| a
                == "/home/u/.var/app/io.github.mark12870.cabinet/data/runners/wine-9.21/bin/wine"));
    }

    #[test]
    fn the_runner_is_found_beside_the_prefixes_directory() {
        let argv = build_with_runner(&[("WINEPREFIX", PREFIX)], false, Some("r"));
        let command = argv.iter().find(|a| a.contains("/data/runners/")).unwrap();
        assert!(command.contains("/data/runners/r/bin/wine"));
        assert!(!command.contains("/prefixes/"));
    }

    #[test]
    fn the_bundled_name_and_a_path_are_both_refused() {
        for marker in ["bundled", "", "  ", "../../escape"] {
            let argv = build_with_runner(&[("WINEPREFIX", PREFIX)], false, Some(marker));
            assert!(
                argv.iter().any(|a| a == "wine"),
                "marker {marker:?} should fall back"
            );
        }
    }

    #[test]
    fn wine_never_inherits_the_sandboxs_wayland_socket() {
        let argv = build(&[], false);
        assert!(argv.iter().any(|a| a == "--env=WAYLAND_DISPLAY="));
    }

    #[test]
    fn a_prefix_can_hand_wayland_back_to_wine() {
        let argv = build_with_markers(
            &[("WINEPREFIX", PREFIX)],
            false,
            &[(ENV_MARKER, Some("WAYLAND_DISPLAY=wayland-0\n"))],
        );
        let blanked = argv
            .iter()
            .position(|a| a == "--env=WAYLAND_DISPLAY=")
            .unwrap();
        let given = argv
            .iter()
            .position(|a| a == "--env=WAYLAND_DISPLAY=wayland-0")
            .unwrap();
        assert!(given > blanked);
    }

    #[test]
    fn a_prefix_sync_mode_sets_all_three_primitives() {
        let argv = build_with_markers(
            &[("WINEPREFIX", PREFIX)],
            false,
            &[(SYNC_MARKER, Some("fsync\n"))],
        );
        assert!(argv.iter().any(|a| a == "--env=WINEFSYNC=1"));
        assert!(argv.iter().any(|a| a == "--env=WINEESYNC=0"));
        assert!(argv.iter().any(|a| a == "--env=WINENTSYNC=0"));
    }

    #[test]
    fn the_prefix_sync_mode_wins_over_what_the_daw_was_launched_with() {
        let argv = build_with_markers(
            &[("WINEPREFIX", PREFIX), ("WINEFSYNC", "1")],
            false,
            &[(SYNC_MARKER, Some("esync"))],
        );
        let fsync = argv.iter().position(|a| a == "--env=WINEFSYNC=1").unwrap();
        let off = argv.iter().position(|a| a == "--env=WINEFSYNC=0").unwrap();
        assert!(off > fsync);
    }

    #[test]
    fn no_sync_marker_leaves_the_daws_own_choice_alone() {
        let argv = build_with_markers(&[("WINEPREFIX", PREFIX), ("WINEFSYNC", "1")], false, &[]);
        assert!(argv.iter().any(|a| a == "--env=WINEFSYNC=1"));
        assert!(!argv.iter().any(|a| a.starts_with("--env=WINENTSYNC")));
    }

    #[test]
    fn an_unknown_sync_word_sets_nothing() {
        let argv = build_with_markers(
            &[("WINEPREFIX", PREFIX)],
            false,
            &[(SYNC_MARKER, Some("ntsyncc"))],
        );
        assert!(!argv.iter().any(|a| a.starts_with("--env=WINEESYNC")));
    }

    #[test]
    fn a_prefix_environment_file_reaches_the_plugin_load_path() {
        let argv = build_with_markers(
            &[("WINEPREFIX", PREFIX)],
            false,
            &[(ENV_MARKER, Some("WINEDEBUG=warn+all\nSTEAM_COMPAT=1\n"))],
        );
        assert!(argv.iter().any(|a| a == "--env=WINEDEBUG=warn+all"));
        assert!(argv.iter().any(|a| a == "--env=STEAM_COMPAT=1"));
    }

    #[test]
    fn blank_comment_and_keyless_lines_are_skipped() {
        let argv = build_with_markers(
            &[("WINEPREFIX", PREFIX)],
            false,
            &[(ENV_MARKER, Some("\n# a note\nnonsense\n=orphan\nKEEP=1\n"))],
        );
        assert_eq!(
            argv.iter()
                .filter(|a| a.starts_with("--env=") && a != &"--env=WAYLAND_DISPLAY=")
                .count(),
            2,
            "{argv:?}"
        );
        assert!(argv.iter().any(|a| a == "--env=KEEP=1"));
    }

    #[test]
    fn a_prefix_cannot_take_over_the_variables_cabinet_owns() {
        let argv = build_with_markers(
            &[("WINEPREFIX", PREFIX)],
            false,
            &[(
                ENV_MARKER,
                Some("WINEPREFIX=/elsewhere\nWINELOADER=/bin/false\nYABRIDGE_TEMP_DIR=/tmp\n"),
            )],
        );
        assert!(!argv.iter().any(|a| a == "--env=WINEPREFIX=/elsewhere"));
        assert!(!argv.iter().any(|a| a.starts_with("--env=WINELOADER")));
        assert!(!argv.iter().any(|a| a == "--env=YABRIDGE_TEMP_DIR=/tmp"));
    }
}
