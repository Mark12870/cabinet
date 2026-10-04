use std::fs;
use std::io::{BufRead, BufReader};
use std::os::unix::fs::PermissionsExt;
use std::path::{Path, PathBuf};
use std::process::{Child, Command, Stdio};
use std::thread;
use std::time::{Duration, Instant};

const SHIM: &str = env!("CARGO_BIN_EXE_cabinet-wine");
const INSTALLED_SHIM: &str = "/app/lib/yabridge/cabinet-wine";
const SERVICE: &str = "io.github.mark12870.cabinet.Bridge.service";
const PATH: &str = "/usr/bin:/bin";

const WINE: &str = "#!/bin/sh\n\
                    printf 'argv=%s\\n' \"$*\"\n\
                    printf 'prefix=%s\\n' \"$WINEPREFIX\"\n\
                    printf 'watchdog=%s\\n' \"$YABRIDGE_NO_WATCHDOG\"\n\
                    printf 'unforwarded=%s\\n' \"$CABINET_UNFORWARDED\"\n\
                    exit 7\n";

const FALLBACK: &str = "#!/bin/sh\nprintf 'fallback\\n'\nexit 99\n";

struct Bus {
    root: PathBuf,
    daemon: Child,
}

impl Bus {
    fn start(root: &Path, stubs: &Path, runtime: &Path) -> Bus {
        let services = root.join("services");
        fs::create_dir_all(&services).unwrap();
        let shipped =
            fs::read_to_string(Path::new(env!("CARGO_MANIFEST_DIR")).join(SERVICE)).unwrap();
        fs::write(
            services.join(SERVICE),
            shipped.replace(INSTALLED_SHIM, SHIM),
        )
        .unwrap();

        let config = root.join("bus.conf");
        fs::write(
            &config,
            format!(
                "<busconfig>\
                 <type>session</type>\
                 <listen>unix:path={}</listen>\
                 <auth>EXTERNAL</auth>\
                 <servicedir>{}</servicedir>\
                 <policy context=\"default\">\
                 <allow send_destination=\"*\"/><allow receive_sender=\"*\"/><allow own=\"*\"/>\
                 </policy>\
                 </busconfig>",
                root.join("bus").display(),
                services.display()
            ),
        )
        .unwrap();

        let mut daemon = Command::new("dbus-daemon")
            .arg(format!("--config-file={}", config.display()))
            .args(["--nofork", "--print-address"])
            .env_clear()
            .env("PATH", format!("{}:{PATH}", stubs.display()))
            .env("XDG_RUNTIME_DIR", runtime)
            .env("DBUS_SESSION_BUS_ADDRESS", address(root))
            .stdout(Stdio::piped())
            .spawn()
            .unwrap();
        let mut ready = String::new();
        BufReader::new(daemon.stdout.take().unwrap())
            .read_line(&mut ready)
            .unwrap();

        Bus {
            root: root.to_path_buf(),
            daemon,
        }
    }
}

impl Drop for Bus {
    fn drop(&mut self) {
        let _ = self.daemon.kill();
        let _ = self.daemon.wait();
        let _ = fs::remove_dir_all(&self.root);
    }
}

fn address(root: &Path) -> String {
    format!("unix:path={}", root.join("bus").display())
}

fn stub(directory: &Path, name: &str, script: &str) {
    let path = directory.join(name);
    fs::write(&path, script).unwrap();
    fs::set_permissions(&path, fs::Permissions::from_mode(0o755)).unwrap();
}

fn gone_within(path: &Path, patience: Duration) -> bool {
    let deadline = Instant::now() + patience;
    while path.exists() && Instant::now() < deadline {
        thread::sleep(Duration::from_millis(100));
    }

    !path.exists()
}

#[test]
fn a_sandboxed_daw_starts_wine_through_the_activated_bridge_and_gets_its_status() {
    let root = std::env::temp_dir().join(format!("cabinet-bridge-{}", std::process::id()));
    let stubs = root.join("stubs");
    let runtime = root.join("runtime");
    let sockets = runtime.join("yabridge");
    let prefix = root.join("prefix");
    fs::create_dir_all(&stubs).unwrap();
    fs::create_dir_all(&sockets).unwrap();
    fs::create_dir_all(&prefix).unwrap();
    stub(&stubs, "wine", WINE);
    stub(&stubs, "flatpak-spawn", FALLBACK);
    let bus = Bus::start(&root, &stubs, &runtime);

    let shim = Command::new(SHIM)
        .args(["host.exe", "--plugin"])
        .env_clear()
        .env("PATH", format!("{}:{PATH}", stubs.display()))
        .env("XDG_RUNTIME_DIR", &runtime)
        .env("YABRIDGE_TEMP_DIR", &sockets)
        .env("WINEPREFIX", &prefix)
        .env("DBUS_SESSION_BUS_ADDRESS", address(&bus.root))
        .env("CABINET_UNFORWARDED", "leaked")
        .output()
        .unwrap();
    let said = String::from_utf8_lossy(&shim.stdout);

    assert_eq!(
        shim.status.code(),
        Some(7),
        "{said}{}",
        String::from_utf8_lossy(&shim.stderr)
    );
    assert!(said.contains("argv=host.exe --plugin\n"), "{said}");
    assert!(
        said.contains(&format!(
            "prefix={}\n",
            prefix.canonicalize().unwrap().display()
        )),
        "{said}"
    );
    assert!(said.contains("watchdog=1\n"), "{said}");
    assert!(said.contains("unforwarded=\n"), "{said}");
    assert!(!said.contains("fallback"), "{said}");
    assert!(
        gone_within(&sockets.join("bridge.sock"), Duration::from_secs(30)),
        "the bridge was still serving 30 s after its only session went idle"
    );
}
