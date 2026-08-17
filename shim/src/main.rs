use std::env;
use std::ffi::{OsStr, OsString};
use std::os::unix::ffi::OsStrExt;
use std::os::unix::process::CommandExt;
use std::path::{Path, PathBuf};
use std::process::Command;

const DEFAULT_APP: &str = "io.github.mark12870.cabinet";

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

const RUNNER_MARKER: &str = ".cabinet-runner";
const BUNDLED_RUNNER: &str = "bundled";

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

    let error = Command::new(&argv[0]).args(&argv[1..]).exec();
    eprintln!("cabinet-wine: cannot exec {:?}: {error}", argv[0]);
    std::process::exit(127);
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
        let env: Vec<(String, String)> = env
            .iter()
            .map(|(k, v)| (k.to_string(), v.to_string()))
            .collect();
        let args: Vec<OsString> = args.iter().map(OsString::from).collect();
        let runner = runner.map(str::to_string);

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
                path.file_name()
                    .is_some_and(|n| n == RUNNER_MARKER)
                    .then(|| runner.clone())
                    .flatten()
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
        assert_eq!(argv, vec!["flatpak", "run", "--command=wine", "some.App"]);
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
}
