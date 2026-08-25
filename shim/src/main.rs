use std::env;
use std::ffi::{OsStr, OsString};
use std::os::unix::ffi::OsStrExt;
use std::os::unix::process::{CommandExt, ExitStatusExt};
use std::path::{Path, PathBuf};
use std::process::Command;

mod x11;

const DEFAULT_APP: &str = "io.github.mark12870.cabinet";
const DESKTOP_TITLE: &str = "Wine Desktop";

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
    args: &[OsString],
    getenv: E,
    canon: C,
    read: R,
) -> Vec<OsString>
where
    E: Fn(&str) -> Option<OsString>,
    C: Fn(&Path) -> Option<PathBuf>,
    R: Fn(&Path) -> Option<String>,
{
    let mut argv: Vec<OsString> = Vec::with_capacity(args.len() + FORWARD.len() + 5);

    if in_sandbox {
        argv.push("flatpak-spawn".into());
        argv.push("--host".into());
    }

    argv.push("flatpak".into());
    argv.push("run".into());

    let prefix = getenv("WINEPREFIX").map(|value| canonicalize(&value, &canon));
    let mut command = OsString::from("--command=");
    command.push(wine_command(prefix.as_deref(), &read));
    argv.push(command);

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

    for arg in args {
        argv.push(canonicalize(arg, &canon));
    }

    argv
}

fn main() {
    let app = env::var("CABINET_APP").unwrap_or_else(|_| DEFAULT_APP.to_string());
    let args: Vec<OsString> = env::args_os().skip(1).collect();

    if args.first().is_some_and(|arg| arg == "--cabinet-self-test") {
        println!("cabinet-wine {} ok", env!("CARGO_PKG_VERSION"));
        return;
    }

    let argv = build_argv(
        &app,
        Path::new("/.flatpak-info").exists(),
        &args,
        |key| env::var_os(key),
        |path| std::fs::canonicalize(path).ok(),
        |path| std::fs::read_to_string(path).ok(),
    );

    if let Some(path) = env::var_os("CABINET_SHIM_LOG") {
        use std::io::Write;
        if let Ok(mut log) = std::fs::OpenOptions::new()
            .create(true)
            .append(true)
            .open(path)
        {
            let _ = writeln!(log, "{argv:?}");
        }
    }

    let mut command = Command::new(&argv[0]);
    command.args(&argv[1..]);

    unsafe {
        command.pre_exec(|| {
            if prctl(PR_SET_PDEATHSIG, SIGTERM) == -1 {
                return Err(std::io::Error::last_os_error());
            }

            Ok(())
        });
    }

    let mut child = match command.spawn() {
        Ok(child) => child,
        Err(error) => {
            eprintln!("cabinet-wine: cannot start {:?}: {error}", argv[0]);
            std::process::exit(127);
        }
    };
    let watcher = x11::Watcher::start();
    let status = child.wait().unwrap_or_else(|error| {
        eprintln!("cabinet-wine: cannot wait for {:?}: {error}", argv[0]);
        std::process::exit(127);
    });
    drop(watcher);

    std::process::exit(
        status
            .code()
            .unwrap_or_else(|| 128 + status.signal().unwrap_or(1)),
    );
}

const PR_SET_PDEATHSIG: i32 = 1;
const SIGTERM: i32 = 15;

extern "C" {
    fn prctl(option: i32, ...) -> i32;
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

    fn build(env: &[(&str, &str)], args: &[&str], in_sandbox: bool) -> Vec<String> {
        build_with_runner(env, args, in_sandbox, None)
    }

    fn build_with_runner(
        env: &[(&str, &str)],
        args: &[&str],
        in_sandbox: bool,
        runner: Option<&str>,
    ) -> Vec<String> {
        build_with_markers(env, args, in_sandbox, &[(RUNNER_MARKER, runner)])
    }

    fn build_with_markers(
        env: &[(&str, &str)],
        args: &[&str],
        in_sandbox: bool,
        markers: &[(&str, Option<&str>)],
    ) -> Vec<String> {
        let env: Vec<(String, String)> = env
            .iter()
            .map(|(k, v)| (k.to_string(), v.to_string()))
            .collect();
        let args: Vec<OsString> = args.iter().map(OsString::from).collect();
        let markers: Vec<(String, Option<String>)> = markers
            .iter()
            .map(|(name, body)| (name.to_string(), body.map(str::to_string)))
            .collect();

        build_argv(
            "io.github.mark12870.cabinet",
            in_sandbox,
            &args,
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

    #[test]
    fn native_daw_does_not_hop_through_the_host() {
        let argv = build(&[], &[], false);
        assert_eq!(argv[0], "flatpak");
        assert_eq!(&argv[..3], &["flatpak", "run", "--command=wine"]);
    }

    #[test]
    fn sandboxed_daw_hops_through_the_host() {
        let argv = build(&[], &[], true);
        assert_eq!(&argv[..2], &["flatpak-spawn", "--host"]);
        assert_eq!(argv[2], "flatpak");
    }

    #[test]
    fn only_set_and_non_empty_variables_are_forwarded() {
        let argv = build(&[("WINEDEBUG", "-all"), ("WINEESYNC", "")], &[], false);
        assert!(argv.iter().any(|a| a == "--env=WINEDEBUG=-all"));
        assert!(!argv.iter().any(|a| a.starts_with("--env=WINEESYNC")));
    }

    #[test]
    fn masked_paths_are_resolved_in_argv() {
        let argv = build(
            &[],
            &["/home/u/.var/app/fm.reaper.Reaper/data/yabridge/yabridge-host.exe.so"],
            false,
        );
        assert_eq!(
            argv.last().unwrap(),
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
            &[],
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
            &[],
            false,
        );
        assert!(argv
            .iter()
            .any(|a| a == "--env=WINEDLLPATH=/home/u/.local/share/yabridge:/opt/wine"));
    }

    #[test]
    fn bare_words_are_never_treated_as_paths() {
        let argv = build(&[], &["vst3", "group", "./relative"], false);
        assert_eq!(&argv[argv.len() - 3..], &["vst3", "group", "./relative"]);
    }

    #[test]
    fn unresolvable_absolute_paths_are_passed_through_unchanged() {
        let argv = build(&[], &["/run/user/1000/yabridge/sock"], false);
        assert_eq!(argv.last().unwrap(), "/run/user/1000/yabridge/sock");
    }

    #[test]
    fn the_app_id_separates_flatpak_options_from_wine_arguments() {
        let argv = build(&[("WINEDEBUG", "-all")], &["a.so", "vst3"], false);
        let app = argv
            .iter()
            .position(|a| a == "io.github.mark12870.cabinet")
            .expect("app id present");
        assert!(argv[..app]
            .iter()
            .all(|a| a.starts_with("--") || a == "flatpak" || a == "run"));
        assert_eq!(&argv[app + 1..], &["a.so", "vst3"]);
    }

    #[test]
    fn an_empty_environment_still_produces_a_runnable_command() {
        let argv = build_argv("some.App", false, &[], no_env, fake_canon, |_| None);
        let argv: Vec<String> = argv
            .iter()
            .map(|a| a.to_string_lossy().into_owned())
            .collect();
        assert_eq!(
            argv,
            vec![
                "flatpak",
                "run",
                "--command=wine",
                "--env=WAYLAND_DISPLAY=",
                "some.App"
            ]
        );
    }

    const PREFIX: &str = "/home/u/.var/app/io.github.mark12870.cabinet/data/prefixes/serum";

    #[test]
    fn a_prefix_with_no_marker_uses_the_bundled_wine() {
        let argv = build_with_runner(&[("WINEPREFIX", PREFIX)], &[], false, None);
        assert!(argv.iter().any(|a| a == "--command=wine"));
    }

    #[test]
    fn a_prefix_naming_a_runner_starts_that_runners_wine() {
        let argv = build_with_runner(&[("WINEPREFIX", PREFIX)], &[], false, Some("wine-9.21\n"));
        assert!(argv.iter().any(|a| a
            == "--command=/home/u/.var/app/io.github.mark12870.cabinet/data/runners/wine-9.21/bin/wine"));
    }

    #[test]
    fn the_runner_is_found_beside_the_prefixes_directory() {
        let argv = build_with_runner(&[("WINEPREFIX", PREFIX)], &[], false, Some("r"));
        let command = argv.iter().find(|a| a.starts_with("--command=")).unwrap();
        assert!(command.contains("/data/runners/r/bin/wine"));
        assert!(!command.contains("/prefixes/"));
    }

    #[test]
    fn the_bundled_name_and_a_path_are_both_refused() {
        for marker in ["bundled", "", "  ", "../../escape"] {
            let argv = build_with_runner(&[("WINEPREFIX", PREFIX)], &[], false, Some(marker));
            assert!(
                argv.iter().any(|a| a == "--command=wine"),
                "marker {marker:?} should fall back"
            );
        }
    }

    #[test]
    fn wine_never_inherits_the_sandboxs_wayland_socket() {
        let argv = build(&[], &[], false);
        assert!(argv.iter().any(|a| a == "--env=WAYLAND_DISPLAY="));
    }

    #[test]
    fn a_prefix_can_hand_wayland_back_to_wine() {
        let argv = build_with_markers(
            &[("WINEPREFIX", PREFIX)],
            &[],
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
            &[],
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
            &[],
            false,
            &[(SYNC_MARKER, Some("esync"))],
        );
        let fsync = argv.iter().position(|a| a == "--env=WINEFSYNC=1").unwrap();
        let off = argv.iter().position(|a| a == "--env=WINEFSYNC=0").unwrap();
        assert!(off > fsync);
    }

    #[test]
    fn no_sync_marker_leaves_the_daws_own_choice_alone() {
        let argv = build_with_markers(
            &[("WINEPREFIX", PREFIX), ("WINEFSYNC", "1")],
            &[],
            false,
            &[],
        );
        assert!(argv.iter().any(|a| a == "--env=WINEFSYNC=1"));
        assert!(!argv.iter().any(|a| a.starts_with("--env=WINENTSYNC")));
    }

    #[test]
    fn an_unknown_sync_word_sets_nothing() {
        let argv = build_with_markers(
            &[("WINEPREFIX", PREFIX)],
            &[],
            false,
            &[(SYNC_MARKER, Some("ntsyncc"))],
        );
        assert!(!argv.iter().any(|a| a.starts_with("--env=WINEESYNC")));
    }

    #[test]
    fn a_prefix_environment_file_reaches_the_plugin_load_path() {
        let argv = build_with_markers(
            &[("WINEPREFIX", PREFIX)],
            &[],
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
            &[],
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
            &[],
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
