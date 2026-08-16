//! `cabinet-wine` — the WINELOADER shim.
//!
//! yabridge's winegcc wrapper ends in
//!
//! ```sh
//! exec "$WINELOADER" "$appdir/yabridge-host.exe.so" "$@"
//! ```
//!
//! so pointing `WINELOADER` at this binary is the entire boundary between a DAW
//! running outside the sandbox and the Wine that runs inside it. Everything here
//! exists because `flatpak run` starts the sandbox from a clean environment and a
//! different mount namespace: what is not forwarded is lost, and what is not
//! resolved to a real path does not exist on the other side.

use std::env;
use std::ffi::{OsStr, OsString};
use std::os::unix::ffi::OsStrExt;
use std::os::unix::process::CommandExt;
use std::path::{Path, PathBuf};
use std::process::Command;

const DEFAULT_APP: &str = "io.github.mark12870.cabinet";

/// Forwarded into the sandbox when set and non-empty.
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

/// Variables holding a single path.
const CANON_VARS: &[&str] = &[
    "WINEPREFIX",
    "YABRIDGE_TEMP_DIR",
    "YABRIDGE_DEBUG_FILE",
    "XAUTHORITY",
];

/// Variables holding a colon-separated list of paths.
const CANON_LIST_VARS: &[&str] = &["WINEDLLPATH"];

/// Resolve a path for the far side of the boundary.
///
/// Flatpak masks `~/.var/app/<other-app>` even under `--filesystem=home`, and the
/// chainloader finds yabridge through the DAW's `$XDG_DATA_HOME` — which for a
/// Flatpak DAW is exactly that masked directory. Handing those paths across
/// unresolved makes Wine fail with `failed to open ...yabridge-host.exe.so`.
///
/// Only absolute paths are resolved. yabridge passes every path absolutely, while
/// its other arguments are bare words like `vst3` or `group`; resolving those
/// would rewrite them the moment the working directory happened to contain a file
/// of the same name.
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

/// Build the command that runs Wine inside the sandbox.
///
/// Split out from `main` so the translation can be tested without exec'ing
/// anything or touching the filesystem.
fn build_argv<E, C>(
    app: &str,
    in_sandbox: bool,
    args: &[OsString],
    getenv: E,
    canon: C,
) -> Vec<OsString>
where
    E: Fn(&str) -> Option<OsString>,
    C: Fn(&Path) -> Option<PathBuf>,
{
    let mut argv: Vec<OsString> = Vec::with_capacity(args.len() + FORWARD.len() + 5);

    // The DAW is itself sandboxed, so hop out to the host before starting Wine.
    if in_sandbox {
        argv.push("flatpak-spawn".into());
        argv.push("--host".into());
    }

    argv.push("flatpak".into());
    argv.push("run".into());
    argv.push("--command=wine".into());

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

    // This binary is exec'd inside whatever sandbox the DAW lives in, which may run an
    // older runtime than the one it was built against. Running it here proves it loads
    // there, turning "the plugin silently will not scan" into one legible check.
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

    /// Stands in for the masking that motivates canonicalization at all: the DAW
    /// reaches yabridge through a symlink in its own data directory, which the
    /// Wine sandbox cannot see.
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
        let env: Vec<(String, String)> = env
            .iter()
            .map(|(k, v)| (k.to_string(), v.to_string()))
            .collect();
        let args: Vec<OsString> = args.iter().map(OsString::from).collect();

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

    /// yabridge's first argument is a bare word like `vst3` or `group`. Resolving
    /// those would rewrite them whenever the working directory held a file of the
    /// same name, so only absolute paths are touched.
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
        let argv = build_argv("some.App", false, &[], no_env, fake_canon);
        let argv: Vec<String> = argv
            .iter()
            .map(|a| a.to_string_lossy().into_owned())
            .collect();
        assert_eq!(argv, vec!["flatpak", "run", "--command=wine", "some.App"]);
    }
}
