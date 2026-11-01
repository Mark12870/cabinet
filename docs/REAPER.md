# REAPER's plugin scan cache

When REAPER "cannot find" a plugin Cabinet has installed and bridged, read its scan cache before
looking at scan paths or links:
`~/.var/app/fm.reaper.Reaper/config/REAPER/reaper-vstplugins64.ini`.

Each scanned plugin is a line `<file>=<FILETIME>,<id>{<uid>,<name>`. A line carrying **only** the
timestamp, with no id and no name, is a plugin that REAPER found but **failed to load**. Look at
load failures, such as `ldd` against REAPER's Flatpak runtime rather than the host, not at why it
is missing.

The timestamp is the modification time of the **vendor binary inside the bundle**, not of
Cabinet's link, and REAPER scans a plugin again only when that time changes. A plugin shipped with
an old timestamp (Vital 1.0.7 is stamped 2021) keeps its failure record through every ordinary
re-scan; only "Clear cache and re-scan" retries it. Rewriting the binary, for example by relinking
it, changes its time and invalidates the entry.

A project whose `<VST …>` line names the plugin with a **different** UID is a separate problem: a
different build of the plugin, not a load failure.
