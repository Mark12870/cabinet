---
name: library-entry
description: Add a plugin to Cabinet's Library — the bundled catalogue the Library page and `cabinet library` read. Use whenever a new VST should become installable in one click, or an existing entry needs its version bumped. Covers which fields are judgements rather than lookups, how to pin a download so it cannot drift, and the one check that actually proves an entry works.
---

# Adding a plugin to the Library

An entry is one flat `.yml` in `data/library/`, installed to `/app/share/cabinet/library/`.
`Library.Install` is the only procedure that reads it — **there is no script**, no steps, no
hooks. If a plugin needs something the fields cannot say, that is a gap in `Library.Install`
to raise, not an escape hatch to add.

`data/library/surge-xt.yml` is the worked example. Copy it.

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

Removal must take its own links and leave every other one alone.

## Before it reaches anyone

A new entry ships with a release — the catalogue is bundled, not fetched. See the `releasing`
skill; the version lives only in the newest metainfo `<release>` and a published one cannot be
corrected afterwards.
