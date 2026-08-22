---
name: library-entry
description: Add a plugin to Cabinet's Library — the bundled catalogue the Library page and `cabinet library` read. Use whenever a new VST should become installable in one click, or an existing entry needs its version bumped. Covers which fields are judgements rather than lookups, how to pin a download so it cannot drift, and the one check that actually proves an entry works. The gotchas behind these rules are in GOTCHAS.md.
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
— and two vendors shipping the same one is refused when the library is read. `Library.Install`
does the whole install from the entry: download, checksum, unpack, link, record, bridge. Only
the unpack-or-install step in the middle is replaceable, by naming a shipped `sh` script;
everything else is fields, and a plugin needing more of them is a gap in `Library.Install` to
raise. `data/library/surge-xt.yml` is the worked example — copy it.

## Add one, step by step

**1. Look for a Linux build, and prefer it.** If the plugin has one that works, add that and do
**not** add the Windows one. Not a tie-break: a Windows entry costs a Wine prefix, a pinned old
runner, a DXVK install, a yabridge bridge on the audio path, and the editor click-offset bug
([yabridge#382](https://github.com/robbert-vdh/yabridge/issues/382)) that the pinned runner only
works around. Two entries for one plugin also put the same name in both sections of the page,
which reads as a choice the user has to make and is not one. Check for a `-linux`, `-lnx`,
`.tar.gz` or AppImage asset on the same release page and confirm it carries a `.vst3`, `.clap`
or `.lv2`. *Working* is the qualifier, decided the same way as everything else here — a build
that crashes, has no GUI, or ships only a standalone binary is not one, and the Windows entry is
then right. Say so in the commit message when that happens, so nobody re-litigates it.

**2. Pin the download.**

```sh
url=https://github.com/<owner>/<repo>/releases/download/<tag>/<asset>
curl -sSfL -O "$url" && sha256sum "$(basename "$url")"
```

- Take the asset URL for a **tag**, never `/latest/` — a moving URL turns a pinned checksum into
  a download that fails for everyone the day upstream ships.
- Compute the SHA-256 **from the file you downloaded**, never from a number the release page
  quotes. A checksum copied from the same page that served a bad file proves nothing.
- Keep the version in the filename. That is what makes a stale entry visible in a diff.
- Windows entries take an **installer `.exe`**; `Prefixes.Install` runs it under Wine. A zip of
  loose plugin files is not something an entry can install. An `.msi` is, but only through a
  `Script:` — `"$WINE" msiexec /i "$CABINET_ARCHIVE" /qn`, since `Prefixes.Install` would hand
  the database to `wine` as an executable.

Two vendors do not allow a pinned file at all — see *an account-gated download* and *one
always-current URL* below.

**3. Prefer a silent install, and measure which switch works.** Run `file` on the `.exe`: NSIS
takes `/S`, Inno `/VERYSILENT`, MSI-based ones `/qn`. Where one works, give the entry a
`Script:` that passes it — `xfer-records/serum.sh` installs all 1.9 GB of Serum 2 with nothing
to click. Measure rather than trusting the family: FabFilter's is its own installer and every
switch opened the wizard and installed nothing. A wizard is the fallback, not the default. A
silent install takes the vendor's default paths, so **check the prefix afterwards** — the script
is the place to fail loudly when the plugin or its content did not land.

**4. Fill in the fields.** The Library page lists rows; a row opens a page of its own, and these
are what it shows. `cabinet library show <id>` renders the same facts.

```yaml
Developer: u-he              # who made it, shown under the name
Version: 1.2.3               # the pinned release, not "latest"
Licence: Freeware            # Freeware, GPL-3.0, Commercial
Licensing:                   # optional; one sentence, where the word alone would mislead
  Every download is a free, fully functional 30-day trial version.
Formats: VST3, CLAP          # what a DAW will actually find after installing
Description:                 # indented block, blank line between paragraphs
  Made in 2005 and still shipping. One oscillator, one filter,
  one envelope and an arpeggiator.

  The Linux build is u-he's own, and they call it beta.
```

`Formats` is what the plugin *installs as here*, not what the vendor's box says: u-he's freeware
ships one binary that is a VST3 for every product and also a CLAP for two of them, so
`grep -qa clap_entry` on the binary decides it, and AU and AAX never appear. Two short
paragraphs is the right length for `Description` — what it is, then what it costs to run.

**5. Decide the three fields that are judgements.** Everything above is a lookup; these are
decisions, and getting one wrong shows up much later as a plugin that installs and then cannot
be used.

- **`Runner: 9.21`** for anything with a plugin editor. From Wine 9.22 on, clicks land offset by
  the window's distance from the screen origin — yabridge#382, acknowledged upstream and
  unfixed. Leave `Runner` out only for a plugin with no editor at all.
- **`Dxvk: true`** for any JUCE editor. The symptom without it is a UI that draws once and then
  only when the window is moved; Surge XT and Dexed both need it. When in doubt set it — DXVK
  backs up the DLLs it replaces and the switch reverses.
- **`Sync: fsync`** alongside `Runner: 9.21`, which is a TkG build and has fsync. Do **not** set
  it for a runner that lacks fsync — Soda reports `( TkG Plain )` — and leave it out entirely to
  inherit whatever launched the DAW.

`Kind: native` refuses all four of those outright: a Linux plugin is loaded by the DAW directly,
so there is no prefix, runner, DXVK or sync to set.

**6. Add the artwork.** `<vendor>/<id>.png` is a 192×192 icon for the row, `<id>.jpg` a
screenshot no wider than 1000px, and `<vendor>/logo.png` the vendor's own mark, shown in a
plugin page's top-left corner — falling back to a symbolic icon when a vendor has none, not to
the plugin's own artwork.

```sh
magick shot.jpg -strip -resize '1000x600>' -quality 80 data/library/<vendor>/<id>.jpg
magick shot.jpg -strip -gravity center -crop 1:1 +repage -resize 192x192 data/library/<vendor>/<id>.png
```

The icon is a square centre crop of the screenshot: the plugin's own face reads better at 32px
than a wordmark, and a wordmark in the vendor's brand colour disappears in one of the two GNOME
themes. A logo needs a background chosen for it — white-on-transparent vanishes in the light
theme and black in the dark one, so pad each onto near-black or white as it needs, unless the
mark is a self-contained badge as u-he's and FabFilter's are. A vendor with no logo worth
shipping ships none, as Digital Suburban does. Take artwork from the vendor's own product page,
or, where a project publishes no screenshot at all, **run the thing and photograph it** — Dexed's
came from its own standalone build, started under `GDK_BACKEND=x11` and captured with
`import -window`, which beats borrowing a stranger's screenshot. Record where a new logo came
from in `SOURCES.md` beside this skill, because shipping it makes Cabinet redistribute that
vendor's artwork. An entry with no files there falls back to the category icon and shows no
screenshot, which is a supported state, not a broken one.

**7. Install it, and read what landed.**

```sh
flatpak run io.github.mark12870.cabinet library install <id>
```

An installer that never ran still exits 0, so read the prefix or the links rather than the
status. For a native entry check both directions:

```sh
ls -l ~/.vst3 ~/.clap ~/.vst
flatpak run io.github.mark12870.cabinet library remove <id>
```

Removal must take its own links and its `Data:` directory and leave every other one alone.

**8. Hand it over for the editor check.** **Load the plugin in a DAW and open its editor** — an
entry that installs cleanly and whose editor cannot be clicked is a broken entry, and this is
the only step that catches it. It is what the `Runner` and `Dxvk` choices in step 5 are for, and
neither is verifiable any other way. This step belongs to whoever is adding the entry;
verification from here stops at what the disk shows.

**9. Ship it.** A new entry reaches people only with a release — the catalogue is bundled, not
fetched. See the `releasing` skill: the version lives only in the newest metainfo `<release>`,
and a published one cannot be corrected afterwards.

## For these cases

### An account-gated download

Anything behind a login, a licence agreement, an account or a download manager is `Source: byo`.
The entry still carries the prefix, runner, DXVK and sync knowledge — that is most of its value —
and only the file comes from the user. **Do not try to script a login.** Omit `Url` and `Sha256`;
the error `Library.Install` raises already tells the user exactly what to pass.

```yaml
Source: byo
Account: https://account.vital.audio   # byo only; refused where Cabinet downloads it itself
```

`Account:` is the page to log in on, and both front ends offer it before asking for the file —
the CLI in the message it raises, the GUI as a row in the install dialog that opens a browser.
Leave it out when there is nothing to log into and the user simply owns an installer. A native
plugin may be `byo` too — Vital's download exists only behind an account — and then the file the
user picks is the archive Cabinet unpacks and links, with no checksum, because they fetched it
from the vendor themselves.

### One always-current URL

`Source: rolling`, and **no `Sha256` — the parser refuses one.** FabFilter serves the whole Total
Bundle from `cdn-b.fabfilter.com/downloads/fftotalbundlex64.exe`, a path with no version in it
whose bytes move with every plugin update, and publishes no checksum of its own. A number
computed here would be neither a pin (it fails every install between an upstream update and the
next release) nor a verification (it is only what one person happened to download). So a rolling
entry says so instead: both front ends warn, before the download starts, that nothing can verify
what arrives — only that it came from the vendor over HTTPS. Leave `Version` out too; there is no
pinned release to name. Use it only where the vendor really does serve one moving URL; anything
with a version in its filename stays `download`, where the checksum is real and a mismatch stops
the install.

### A native archive

Look inside before pinning it:

```sh
tar -tzf <archive> | awk -F/ 'NF<=3'     # or: unzip -Z1 <archive> | awk -F/ 'NF<=3'
```

`Library` links a `.vst3`, `.clap`, `.lv2` or `.so` found in the **top two levels**, into
`~/.vst3`, `~/.clap`, `~/.lv2` and `~/.vst` respectively, and never descends into a bundle
directory. An archive whose plugins sit deeper than that cannot be linked, and one with none at
all fails with a message saying so. LV2 is native-only: yabridge bridges VST2, VST3 and CLAP, so
`.lv2` never appears under a `Kind: windows` entry.

### An archive `tar -xf` cannot finish

When unpacking alone does not leave a loadable plugin in the top two levels, name a script.
u-he is the worked example — one `.so`, no bundle at all, and the VST3 has to be assembled — so
the seven u-he entries share `data/library/u-he/u-he.sh`. Write a new script only when an
existing one does not fit; a shared one is better than a near-copy.

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

A script may **not** download anything (the checksummed archive is the only input), write
outside those directories, or link into `~/.vst3` and friends — Cabinet does the linking, the
recording and the removal, which is what keeps uninstall predictable. `Data:` exists for plugins
that hardcode a path under `$HOME`: Cabinet creates it, refuses to install over one it did not
create, and deletes it on removal, so the script only fills it. A new `Data:` directory needs a
**`--filesystem=~/<dir>:create` in the manifest** beside `~/.u-he` — Cabinet holds `$HOME`
read-only, so without the grant the install fails on the first write.
