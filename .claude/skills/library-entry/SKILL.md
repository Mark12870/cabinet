---
name: library-entry
description: Add a plugin to Cabinet's Library — the bundled catalogue the Library page and `cabinet library` read. Use whenever a new VST should become installable in one click, or an existing entry needs its version bumped. Covers which fields are judgements rather than lookups, how to pin a download so it cannot drift, and the one check that actually proves an entry works.
---

# Adding a plugin to the Library

An entry is one flat `.yml` in its vendor's directory — `data/library/<vendor>/<id>.yml`,
installed to `/app/share/cabinet/library/<vendor>/` — beside that vendor's install script and
its plugins' artwork:

```
data/library/u-he/podolski.yml      the entry
data/library/u-he/podolski.jpg      screenshot, ≤1000px
data/library/u-he/podolski.png      icon, 192×192
data/library/u-he/u-he.sh           the install script its entries name
data/library/u-he/logo.png          the vendor's mark, 192×192, shown on every page of theirs
```

Ids stay unique across the whole catalogue — they are what `cabinet library install <id>` takes
— and two vendors shipping the same one is refused when the library is read.
`Library.Install` reads it and does the whole install — download, checksum, unpack, link,
record, bridge. Only the unpack-or-install step in the middle is replaceable, by naming a
shipped `sh` script in `Script:`; see *When an entry needs a script* below. Everything else is
fields, and a plugin needing more of them is a gap in `Library.Install` to raise.

`data/library/surge-xt.yml` is the worked example. Copy it.

## What fills the plugin's page

The Library page lists rows; a row opens a page of its own, and these fields are what it has
to show. `cabinet library show <id>` renders the same facts.

```yaml
Developer: u-he              # who made it, shown under the name
Version: 1.2.3               # the pinned release, not "latest"
Licence: Freeware            # Freeware, GPL-3.0, Commercial
Formats: VST3, CLAP          # what a DAW will actually find after installing
Description:                 # indented block, blank line between paragraphs
  Made in 2005 and still shipping. One oscillator, one filter,
  one envelope and an arpeggiator.

  The Linux build is u-he's own, and they call it beta.
```

`Formats` is what the plugin *installs as here*, not what the vendor's box says: u-he's
freeware ships one binary that is a VST3 for every product and also a CLAP for two of them, so
`grep -qa clap_entry` on the binary decides it, and AU and AAX never appear. Two short
paragraphs is the right length for `Description` — what it is, then what it costs to run.

**Artwork is bundled**: `<vendor>/<id>.png` is a 192×192 icon for the row, `<id>.jpg` a
screenshot no wider than 1000px, and `<vendor>/logo.png` the vendor's own mark, which is what a
plugin's page shows in its top-left corner — falling back to a symbolic icon when a vendor has
none, not to the plugin's own artwork. A logo needs a background chosen for it: a white-on-transparent mark disappears in the
light theme and a black one in the dark theme, so pad each onto near-black or white as it
needs, rather than shipping it transparent — unless the mark is a self-contained badge, as
u-he's and FabFilter's are. A vendor with no logo worth shipping ships none, as Digital
Suburban does. Record where a new one came from in `SOURCES.md` beside this skill, because
shipping it makes Cabinet redistribute that vendor's artwork. Take it from the vendor's own product
page — or, when a project publishes no screenshot at all, **run the thing and photograph it**:
Dexed's came from its own standalone build, started under `GDK_BACKEND=x11` and captured with
`import -window`, which beats borrowing a stranger's screenshot. An entry with no files there
falls back to the category icon and shows no screenshot, which is a supported state, not a
broken one. The
icon is a square centre crop of the screenshot — the plugin's own face reads better at 32px
than a wordmark, and a wordmark in the vendor's brand colour disappears in one of the two GNOME
themes.

```sh
magick shot.jpg -strip -resize '1000x600>' -quality 80 data/library/<vendor>/<id>.jpg
magick shot.jpg -strip -gravity center -crop 1:1 +repage -resize 192x192 data/library/<vendor>/<id>.png
```

## Linux first, and it is not a tie-break

**If the plugin has a Linux build that works, add that and do not add the Windows one.** Not a
preference — a Windows entry costs a Wine prefix, a pinned old runner, a DXVK install, a
yabridge bridge on the audio path, and the editor click-offset bug
([yabridge#382](https://github.com/robbert-vdh/yabridge/issues/382)) that the pinned runner
only works around. The native build has none of that. Two entries for one plugin also put the
same name in both sections of the page, which reads as a choice the user has to make and is
not one.

Windows entries are for plugins with **no** Linux build — which is most commercial ones. Check
before writing one: look for a `-linux`, `-lnx` or `.tar.gz`/`AppImage` asset on the same
release page, and confirm it carries a `.vst3`, `.clap` or `.lv2`.

*Working* is the qualifier, and it is decided the same way as everything else here — install it
and open its editor in a DAW. A Linux build that crashes, has no GUI, or ships only a
standalone binary is not a working one, and the Windows entry is then the right answer. Say so
in the commit message when that happens, so nobody re-litigates it.

## The fields that are judgements

Everything else is a lookup; these three are decisions, and getting them wrong shows up much
later as a plugin that installs and then cannot be used.

- **`Runner: 9.21`** for anything with a plugin editor. From Wine 9.22 on, clicks land offset
  by the window's distance from the screen origin —
  [yabridge#382](https://github.com/robbert-vdh/yabridge/issues/382), acknowledged upstream and
  unfixed. Leave `Runner` out only for a plugin with no editor at all.
- **`Dxvk: true`** for any JUCE editor. The symptom without it is a UI that draws once and then
  only when the window is moved; Surge XT and Dexed both need it. When in doubt, set it — DXVK
  backs up the DLLs it replaces and the switch reverses.
- **`Sync: fsync`** alongside `Runner: 9.21`, which is a TkG build and has fsync. Do **not** set
  it for a runner that lacks fsync — Soda reports `( TkG Plain )` — and leave it out entirely to
  inherit whatever launched the DAW.

## Pinning the download

```sh
url=https://github.com/<owner>/<repo>/releases/download/<tag>/<asset>
curl -sSfL -O "$url" && sha256sum "$(basename "$url")"
```

- Take the asset URL for a **tag**, never `/latest/` — a moving URL turns a pinned checksum into
  a download that fails for everyone the day upstream ships.
- Compute the SHA-256 **from the file you downloaded**, never from a number the release page
  quotes. A checksum copied from the same page that served a bad file proves nothing.
- Keep the version in the filename. That is what makes a stale entry visible in a diff.
- Windows entries take an **installer `.exe`**; `Prefixes.Install` runs it under Wine and the
  user clicks through it. A zip of loose plugin files is not something an entry can install.

## When an entry has to be `Source: byo`

Anything behind a login, a licence agreement, an account or a download manager. The entry still
carries the prefix, runner, DXVK and sync knowledge — that is most of its value — and only the
`.exe` comes from the user. **Do not try to script a login.** Omit `Url` and `Sha256`; the
error `Library.Install` raises already tells the user exactly what to pass.

## Native entries

`Kind: native` means no prefix, no runner, no DXVK and no sync — the parser refuses those four
outright, because a Linux plugin is loaded by the DAW directly. Before pinning one, look inside
the archive:

```sh
tar -tzf <archive> | awk -F/ 'NF<=3'     # or: unzip -Z1 <archive> | awk -F/ 'NF<=3'
```

`Library` links a `.vst3`, `.clap`, `.lv2` or `.so` found in the **top two levels**, into
`~/.vst3`, `~/.clap`, `~/.lv2` and `~/.vst` respectively, and never descends into a bundle
directory. An archive whose plugins sit deeper than that cannot be linked, and one with none at
all fails with a message saying so. LV2 is native-only: yabridge bridges VST2, VST3 and CLAP,
so `.lv2` never appears under a `Kind: windows` entry.

## When an entry needs a script

When `tar -xf` alone does not leave a loadable plugin in the top two levels. u-he is the worked
example — one `.so`, no bundle at all, and the VST3 has to be assembled — so the seven u-he
entries share `data/library/u-he/u-he.sh`. Write a new script only when an existing one does
not fit; a shared one is better than a near-copy.

```yaml
Script: u-he.sh          # a filename, not a path; it sits in the entry's own vendor directory
Data: .u-he/Podolski     # native only: a directory of the plugin's own under $HOME
```

The script is `sh -e`, run **in the destination directory**, and given:

| | |
| --- | --- |
| `CABINET_ARCHIVE` | the downloaded file, checksum already verified |
| `CABINET_DEST` | native: the empty `data/native/<id>/` it must fill |
| `CABINET_DATA` | native: `$HOME/<Data>`, created, when the entry names one |
| `CABINET_PREFIX`, `WINE` | windows: the prefix and its own Wine, plus everything `Prefixes.Wine` sets |
| `CABINET_WORK` | scratch, deleted afterwards |
| `CABINET_ID`, `CABINET_NAME` | the entry's id and display name |

A new `Data:` directory needs a **`--filesystem=~/<dir>:create` in the manifest** beside
`~/.u-he`: Cabinet holds `$HOME` read-only, so without the grant the install fails on the
first write.

What a script may **not** do: download anything (the checksummed archive is the only input),
write outside those directories, or link into `~/.vst3` and friends — Cabinet does the linking,
the recording and the removal, which is what keeps uninstall predictable. `Data:` exists for
plugins that hardcode a path under `$HOME`: Cabinet creates it, refuses to install over one it
did not create, and deletes it on removal, so the script only fills it.

## The check that counts

```sh
flatpak run io.github.mark12870.cabinet library install <id>
```

Then **load the plugin in a DAW and open its editor.** An entry that installs cleanly and whose
editor cannot be clicked is a broken entry, and this is the only step that catches it. That is
what the `Runner` and `Dxvk` choices are for, and neither is verifiable any other way.

For a native entry, also check the links:

```sh
ls -l ~/.vst3 ~/.clap ~/.vst
flatpak run io.github.mark12870.cabinet library remove <id>
```

Removal must take its own links and its `Data:` directory, and leave every other one alone.

## Before it reaches anyone

A new entry ships with a release — the catalogue is bundled, not fetched. See the `releasing`
skill; the version lives only in the newest metainfo `<release>` and a published one cannot be
corrected afterwards.
