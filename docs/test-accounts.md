# Test accounts

Scenarios for entries behind a vendor account sign in with a shared test account, or restore a
session it saved.

Helix Native is tested through a Line 6 test account. `secrets.env` at the repository root,
gitignored, holds its `EMAIL` and `PASSWORD`; the Plugins workflow writes the same file inside the
container from the repository secrets of those names, owned by the test user and readable by no
one else, and a push never sees them. The scenario signs in to line6.com, takes the Windows
release whose version the entry pins, accepts the licence and downloads it, checking the MD5 the
page gives. A download that stops for a minute is asked for again, up to three times. In the
editor it types the account through `type`, which reaches the probe in its environment and is
typed from stdin, so it is on no command line and in no log, then presses Sign In and Start Free
Trial. Line 6 knows a computer by the name Wine takes from the hostname, and allows the account
five active computers and eleven over its lifetime, seven of them spent once CI has run. The
Plugins workflow therefore starts its container as `ci`, so every CI run is one computer beside the
toolbox's `toolbx`, and the account's devices say which ran; a run under any other name spends one
for good.
Signed out it passes the tone through untouched; signed in, the default amp preset leaves a tail.

Serum 2's demo sits behind an Xfer sign-in that Cloudflare Turnstile guards, which a test does not
pass. Its installer is kept instead in a shared Dropbox folder whose read-only link is `DROPBOX`
in the same file and secrets; the scenario lists the folder through the endpoint Dropbox's own
page uses, downloads the installer by its path with `raw=1`, and checks a SHA-256 it pins. That
endpoint is not a documented API, so a Dropbox change shows up as a failed listing. A new Serum 2
release means replacing the file in the folder and the scenario's path and checksum.

Vital signs in to its account site through Firebase's password sign-in with the same `EMAIL` and
`PASSWORD`, asks the account's products for the Linux zip of the version the scenario pins, and
downloads it. 1.0.7 is pinned because 1.6.4 ships no LV2. The editor opens behind a sign-in panel
that Work offline dismisses; a press is a move, a few turns of the host, and then the click, since
Vital takes no click that arrives with the pointer. Its knobs are host parameters sorted by name,
so the aim is named, Macro 1. Only VST2 is checked in the editor: under Carla, Vital's VST3 editor
draws once and takes no input, and its LV2 editor never opens, on a GPU display as well, while the
VST3 works in REAPER. Every format still plays a note.

IK Product Manager signs in with `USERNAME` rather than the email, kept in the same file and
secrets, and nothing is captured from the
first key until its log-in page has gone. The scenario presses MODO BASS 2 CS's download, which
starts an Inno Setup wizard of its own, walks it with the licence accepted, and waits for the
manager to authorise the product. The account holds five devices, and IK counts the same computer
across fresh prefixes, so repeated runs keep it at one. MODO BASS 2's controls are its own: the VST2
exposes no parameter and the VST3 only Bypass and MIDI CC proxies, so its editor has no aim and
the scenario counts no parameters. Nor does it assert a tail: a render is identical to the sample
until 15 ms after the note-off, but under load the release decays faster, as if timed by the clock
rather than the samples, and the tail fell from 3e-6 to 1e-11.

Native Access signs in through a browser, and Native Instruments' login page refuses a browser
driven by WebDriver, so its scenario does not sign in; it restores a session instead. Native
Access keeps that session in Wine's Credential Manager, as `NTKDaemon/session_data` under
`HKCU\Software\Wine\Credential Manager` beside the `EncryptionKey` that decrypts it, and the entry
holds in a fresh prefix and does not change with use. `NATIVE_ACCESS` in `secrets.env` and the
secrets holds those two `user.reg` sections base64-encoded. To renew it, sign
in by hand in a scenario install and encode the same two sections again. Raum's editor is not
checked: it draws through DXVK's Direct3D 9, whose swapchain throws on the test display, and Wine's
unwinder spins on that exception instead of returning it.

The same scenario installs Kontakt 8 Player and the Acoustic Drums library in the same Native Access
session. Kontakt's installer goes through Cabinet's `msi.dll` stand-in (see
`docs/vendors/native-instruments.md`), so the scenario waits for the bridged `Kontakt 8.vst3`, for
Native Access's own activation of it, and for its card to read Installed before it asks for a
library that needs it. The prefix runs on lavapipe, as a runner does: on this machine's GPU the test
display offers no DRI3 and Kontakt's editor loses its X connection at its first frame. Until it is
activated, Kontakt's editor opens behind a Demo dialog. A loop is double-clicked in its browser and
the note played live through Carla's engine rather than rendered offline; the plugin's saved state
is then given to a fresh instance, which has to play the loop again. Without Kontakt's factory
content its browser finds nothing and that restore hangs, which is why the scenario also checks
for the application, the factory presets and their registry values. Native Access's two popups are
settled beforehand: the usage consent is answered on the account, and the survey's dismissal is
written into its store before it starts.

Roland Cloud Manager signs in through a browser too, and Roland's login page accepts one driven by
WebDriver: the scenario points Wine's `WineBrowser` at a script that keeps the sign-in URL, signs in
with the test account in headless Chromium, and hands the `rolandcloudmanager://` link it returns
to `library open`. Roland counts each Windows `MachineGuid` as a device and refuses a new one past
the account's limit, so the scenario writes the same synthetic `MachineGuid` into every prefix. Its
manager window, like Kontakt's editor, loses its X connection at its first frame on the test
display, so the prefix runs on lavapipe.

Waves Central signs in through a browser too, and Waves' login page answers a WebDriver-driven
sign-in with "Incorrect captcha code", so its scenario restores a session instead. Central keeps it
in `AppData\Roaming\Waves Audio\Preferences\Waves Central.json`, as `SessionData` beside the
`UserSettings.systemId` it belongs to. `WAVES` in `secrets.env` and the secrets holds those two
fields base64-encoded as JSON, and the scenario writes them into a fresh prefix with the welcome tour
already answered. Central rewrites `SessionData` on every launch, but an earlier copy still signs
in. To renew it, sign in by hand in a scenario install and encode the same two fields again. The
scenario installs the whole Free Plugin Pack; a fresh prefix already lists its licence as activated
on device `C`, and the side panel's button reads Install or Install & Activate accordingly, so the
scenario presses it without reading its label. Carla takes the first plugin of the pack's one
WaveShell VST3, Magma Lil Tube: its pinned build loads only a bundle's first class.
