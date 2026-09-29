# Native Instruments under Cabinet

What was measured while building the Native Access scenario (September 2026, Native Access 2,
Kontakt 8.13.1). Each point says how it was established, so it can be checked again when a
release changes things.

## Signing in

- **Native Access signs in through the system browser**: Auth0 PKCE at
  `auth.native-instruments.com`, which hands back a `native-access://authorize?code&state` link.
- **The login page is behind Akamai Bot Manager.** A scripted HTTP request gets a 403. A Chromium
  driven by ChromeDriver loads the page and posts Akamai's sensor data, but its
  `/usernamepassword/login` request never gets an answer, and the page shows "We're sorry,
  something went wrong". A wrong password would say so instead. A human in a plain Chromium
  signs in fine. No attempt is made to get past this.
- **The whole signed-in session is two `user.reg` sections**:
  `Software\Wine\Credential Manager` (its `EncryptionKey`) and
  `Software\Wine\Credential Manager\Generic: NTKDaemon/session_data` (a 98-byte blob). Nothing
  else Native Access writes (cookies, IndexedDB, its settings store, `ntk.json`) holds a token.
- **The session travels.** Written into a fresh prefix's `user.reg` before launch, Native Access
  starts signed in. The blob stayed byte-identical across several launches in different prefixes,
  so it does not rotate on use. The scenario keeps it base64-encoded as `NATIVE_ACCESS`.

## Native Access itself

- The first screen of a fresh prefix is the EULA. "Agree and proceed" only enables once the text
  has been clicked into and scrolled to its end.
- **Usage Data Consent** is a modal that appears around the first install. It is the account's
  marketing consent, stored server-side (`/v1/users/me/consents/marketing_consent`) and cached
  locally as `marketingConsentStatus`. Answered once, it does not come back for that account.
- **"How is your experience so far?"** is a bar across the bottom of the window after installs
  complete. It covers the install result bar. Native Access decides from `userFeedbackRecords` in
  its store file `2342dc42fd705c1d1004971a4b34109453cc7bd2.json`; a record with
  `"action": "dismissed"` keeps it away.
- Native Access downloads into its `Downloads` folder through aria2, whose `.aria2` control file
  sits beside the download until it completes. Cabinet hard-links each download into the prefix's
  `.cabinet-kept` as it appears. "In `.cabinet-kept`, with no `.aria2` left in `Downloads`" is
  therefore a reliable "downloaded", whatever Native Access reports next.

## Kontakt 8 Player

- **Why Native Access cannot install Kontakt under Wine.** Its installer is an InstallAware setup
  (`Kontakt 8 8.13.1 Setup PC.exe`, run silently by NI's daemon) that rewrites its MSI's component
  table at runtime and then calls `MsiInstallProductA`. On Wine 11.0 Windows Installer terminates
  right after initialising, without running an action; on Wine 11.18 it gets as far as
  `CostInitialize` and then waits forever on its `msiexec -Embedding` custom-action server. Either
  way the setup exits 100, which NI reads as success, and no file is written. Raum's setup, the same
  kind but smaller, installs normally. The same diagnosis is in
  [ni-wine](https://github.com/selimbucher/native-instruments), which replays the MSI's file tables.
- **Cabinet's fix is a `msi.dll` stand-in** (`msi-shim/`), a 32-bit DLL that `native-access.sh`
  puts in `syswow64` beside a copy of the runner's own `msi.dll` named `msi_wine.dll`, and that an
  `AppDefaults` override loads only for `Kontakt 8 Setup PC.exe`. 294 of its exports jump straight
  into `msi_wine.dll`. `MsiInstallProductA/W` do the same for any other package; for Kontakt's they
  run `kontakt-8.sh --installing` through `start.exe /unix`, wait for its result file, write
  Kontakt's registry values and return success, or 1603. InstallAware then writes Kontakt's uninstall
  entry itself, NI's daemon finds and activates Kontakt within seconds, and Native Access lists it
  as installed in the same session, as on Windows. lld prefixes an underscore to export forwarders
  on 32-bit x86 (`_msi_wine.MsiCloseHandle`), which is why the exports are stubs, not forwarders.
- **Prefixes installed before the stand-in** still rely on `kontakt-8.sh`'s recovery when Native
  Access closes, and on a second opening of Native Access to activate Kontakt.
- **Licensing goes through `NTKDaemonService`.** Kontakt Player activates only when it is loaded
  while the daemon runs, about 15 seconds in, writing an activation file to
  `Public/Documents/Native Instruments/Native Access/ras3/`. On that first load its editor still
  shows a **"Kontakt 8 Demo"** dialog (Run Demo / Buy / Activate), which blocks the editor.
  From the next load on it opens as Kontakt Player and no longer needs the daemon.
- **NI's daemon activates what its scan finds installed.** Its "installed product scan" runs
  after every install and on start; a Kontakt it finds there is activated within a second.
- **Setting the service to start automatically does not work.** Every direct Wine step in the
  prefix then starts the daemon, including Cabinet's own `wineboot -u` for DXVK during an install.
  The daemon and its wineserver inherit that step's output pipe, and Cabinet waited on it
  indefinitely; in that install the daemon's own `ping native-instruments.com` had hung as well.
  `sc stop NTKDaemonService` alone did not end a daemon left running. Cabinet ends the prefix's
  Wine after a direct run (`cabinet run`, winetricks) when it can claim the prefix, as a safety net.
- **The editor draws through DXVK's Direct3D 11.** On a headless weston display on this
  machine's GPU (RADV, no DRI3 in Xwayland) the plugin's Wine loses its X connection right after
  the first swapchain is created. On lavapipe, which a GPU-less runner has anyway, the editor
  opens, draws and reacts. Turning vsync off (`dxgi.syncInterval = 0`) was not needed.
- **The first click on a freshly opened Kontakt editor only focuses it.**
- A "What's new in 8.13.1" overlay covers the browser on the first licensed load; its ✕ is at
  (727, 231) in the 1010×647 editor.

## Kontakt libraries

- **Acoustic Drums** (187 MB, part of Komplete Start) installs through Native Access without help,
  after an informational "loads in Kontakt 8 Player" dialog. It lands in
  `Public/Documents/Acoustic Drums Library` with `.nkl` kits, `.wav` loops and an `.nicnt`.
- Kontakt lists it under **Loops**, not Instruments: 160 loop presets that a double-click loads
  into Leap.
- **Carla's saved state does not bring a loaded loop back.** With chunks enabled, Carla saves a
  5.7 KB chunk that names the loop's samples. Restoring it logs
  `PresetSlotManager::selectSlot: slot not found` and leaves the rack empty.
- **Loaded through the browser, the loop plays on MIDI**: silent before, a 0.124 peak while note
  60 is held, silent after release, measured live through Carla's running engine. An offline
  render straight after loading gave silence.

## Raum

- Installs through Native Access and bridges at once. It renders audio with its `Mix` parameter
  held fully wet.
- **Its editor hangs on the test display**, on the GPU and on lavapipe alike. It draws through
  DXVK's Direct3D 9, its swapchain throws, and Wine's unwinder loops on
  `unknown CFA opcode` / `unhandled opcode` instead of returning the exception.

## Test environment notes

- A 15-minute "hang" in an earlier session was `winetricks` stalling on the `vc_redist.x86.exe`
  download during the install, not the sign-in.
- Every `cabinet run` session started while the daemon was set to start automatically outlived
  its command. Such a leftover session is joined by later runs in the prefix and silently changes
  what they measure, so check for stray `services.exe` / `NTKDaemon.exe` between experiments.
