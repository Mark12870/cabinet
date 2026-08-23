# Why the Spitfire Audio App is held back

This entry is finished and the tests check it, but the manifest does not install this
directory, so Cabinet does not offer it. **The app installs perfectly and then crashes a few
seconds after launch**, before it ever paints. Nothing Cabinet can set changes that.

Delete this file to ship the entry.

## What works

Everything except the app itself. Measured against a clean prefix, repeatedly:

- The rolling download resolves and fetches (12 MB, no checksum — Spitfire publish none).
- `spitfire-audio.sh` installs it silently. It is **Inno Setup 6.1.0**, so `/VERYSILENT
  /SUPPRESSMSGBOXES /NORESTART` needs no clicks, and it leaves `unins000.exe` where Cabinet's
  registry scan finds it.
- It lands in `C:\Program Files\Spitfire Audio\` — **not** `Program Files (x86)`, which is what
  the widely-linked [helper script](https://github.com/julien-deoux/spitfire-audio-app) says.
  That script predates the 64-bit build. Both `Launch:` and the script's guard use the 64-bit
  path.
- DXVK installs, sync is set, the prefix is bridged, and `library remove` takes the prefix and
  leaves every other one alone.

## The crash

The app writes its own log to
`drive_c/users/<you>/AppData/Roaming/Spitfire Audio/Settings/lm.log`. It ends dead:

```
[I] Core application started
[I] UI Version: 3.4.18   Core Version: 3.4.18   API Version: 11
[I] REQUEST0: 42A208B6-…   SYSTEM0: <SPITFIRE Fs="…" Cn="…" Cc="…"/>
[I] UUIDs:
[I] wlo1: fa-75-34-5a-bc-ae
[I] enp34s0: 00-d8-61-a3-06-b3
```

Nothing follows. Beside it, in `Settings/App/CrashReports/<uuid>.run/__sentry-event`:

```
level: fatal   release: spitfire-app@3.4.18   platform: native
sdk: sentry.native 0.9.0   integrations: crashpad
```

So it dies immediately after fingerprinting the machine for licensing — hashing system
identifiers and reading the host's real network adapters. The window you see afterwards is an
orphaned shell left by a dead process, which is why no display setting ever changed it. Wine
logs no unhandled exception because Spitfire's own crashpad handler catches the fault first,
and `cabinet library launch` never prints `closed.` because the outer `wine` process outlives
the child that died.

**Read `lm.log` first when revisiting.** It is the only thing here that gave a straight answer;
everything below was measured before it was found, and all of it was chasing the wrong problem.

## What was ruled out

Every installed runner, each with and without DXVK. `colors` is the count of distinct colours
in a capture of the window — `1` is a flat fill with no content at all.

| Runner | DXVK on | DXVK off |
| --- | --- | --- |
| wine-9.21-staging-tkg | 1024×732, solid black, colors=1 | 1×1 window |
| wine-10.8-staging-tkg | 1024×732, solid black, colors=1 | no window |
| bundled (wine-11.0) | 1024×732, solid black, colors=1 | no window |
| soda-11.0-5 | 1032×766, white, correct chrome | 1×1 window |

`soda-11.0-5` is the only runner that produces a properly sized, decorated window, which is why
the entry names it. `Sync: system` goes with it because Soda is `( TkG Plain )` and has no
fsync. None of this makes the app work; it only makes the corpse look tidier.

Also tried and made no difference:

- **Windows 7 mode** (`HKCU\Software\Wine` → `Version` = `win7`). The reasoning was sound —
  the binary is **JUCE**, not Chromium (2217 JUCE strings, no `libcef.dll`, no `.pak` files),
  and JUCE 8 only uses its Direct2D-on-DirectComposition renderer on Windows 8.1+, so Win7
  should force the software path. It does not help, because the problem is not the renderer.
- **Chromium flags** — `--disable-gpu`, `--disable-gpu-compositing`, `--disable-direct-composition`,
  `--no-sandbox`, and combinations. Wasted effort: the app is not Chromium. The
  `DxgiFactory::CreateSwapChainForComposition: Not implemented` spam (~22,600 lines) is DXVK
  answering JUCE's Direct2D backend, and it is a symptom, not the cause.

## Older versions

Not obtainable from the vendor, so an older build could not be tested.

- The download URL carries no version: `https://www2.spitfireaudio.com/library-manager/download/win/`
  302s to a timestamped CloudFront path, `…/p/files/lm/1770184800/win/Spitfire Audio.Win-3.4.18-<hash> Installer.exe`.
  Older builds sit under other timestamps with their own hashes and cannot be guessed.
- The binary leaks one versioned path, `…/p/files/lm/mac/spitfireaudio-mac-3.0.7.zip`, which is
  live (200) but is a hardcoded macOS fallback, not a browsable archive. Every Windows and every
  other macOS name tried returns 403.
- Third-party mirrors carry `SpitfireAudio-Win-3.4.4.exe`. Do not point the entry at one: it
  would break the one thing `Source: rolling` still promises, that the bytes came from the
  vendor over HTTPS. If an older build is ever shown to work, ship it as `Source: byo` and let
  the user supply the file, the way Vital and Serum do.

## If you revisit

1. Read `lm.log`. If it now gets past `UUIDs:`, the licensing crash is fixed and this file can go.
2. The crash is in Spitfire's own code under Wine, so the realistic fixes are upstream: a newer
   Wine, or a Spitfire release that stops crashing. A symbolised minidump from
   `Settings/App/CrashReports/` would name the faulting call, if it is worth that much.
3. When it does work, one thing here is still unproven: whether the plugins the app installs —
   LABS, BBCSO — need `Runner: 9.21` for the editor click offset
   ([yabridge#382](https://github.com/robbert-vdh/yabridge/issues/382)), which would conflict
   with the `soda-11.0-5` this entry needs. One prefix cannot satisfy both, and
   [yabridge#366](https://github.com/robbert-vdh/yabridge/issues/366) reports BBCSO and LABS
   disagreeing about the Windows version too.
