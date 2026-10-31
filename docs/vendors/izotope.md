# iZotope under Cabinet

What was measured while building the Product Portal scenario (October 2026, Product Portal 1.4.9,
Vinyl 1.13.0). Each point says how it was established, so it can be checked again when a release
changes things.

- **The prefix needs `wine-d2d1-dcomp-11.0-887-p3` or a build with the same two patches.** Its
  crypt32 patch lets Vinyl import iZotope's certificate-only licence file; without it ACTIVATE ends
  in "Response Validation Failed". Its winex11 patch keeps iZotope's layered dialogs inside the
  editor; without it they draw outside it, out of sight. 887-p2 blacked out Trash's editor around its
  preset list.
- **The Portal's log-in page needs a font called Arial.** The page is CEF. On a prefix where
  DirectWrite finds no Arial, it paints once in fallback fonts and never again: no caret, typed text
  never shows, and the sign-in never completes. The Kron4ek runners carry staging's Arial;
  `wine-d2d1-dcomp` does not. With `corefonts` installed, or any FontSubstitutes entry for Arial, the
  page draws its own Korolev and Roboto and signs in.
- **Vinyl's teardown waits for its usage analytics.** Once its knobs have moved, removing the plugin
  sends the queued events to `api.mixpanel.com` and `door.event-bus.io` and waits for every one.
  Wine's winhttp hands a freed request handle out again while a completion for the old request is
  still on its way, so one request never finishes and the host never exits. The winhttp trace
  shows the reuse; `EnableAnalytics=0` under `HKCU\Software\iZotope` stops the requests, and four
  knob-dragging probes in a row then closed cleanly where every earlier one hung. The Portal asks
  on its first sign-in all the same, and Share writes `EnableAnalytics=1` back; Don't Share keeps
  it off.
- **Trash's editor is a WebView2 page.** Installed from the Portal's TRIALS tab, it drew black on a
  prefix without the WebView2 Runtime; with the Runtime and the browser flags the Native Instruments
  prefix uses, three probes in a row drew it and closed cleanly.
- **ACTIVATE answers only after the computer has been chosen.** On "Choose to authorize to:", a press
  on ACTIVATE alone is not logged by the Portal; the computer icon first, then ACTIVATE, reaches
  "Success!" and `Single-Authorize (comp) | Succeeded` in `ProductPortal.log`.
