# Scenario harness

`docs/TESTS.md` says what a plugin scenario is and how to run one; this is what `ScenarioHarness` does
for it.

`ScenarioHarness` owns isolation, process supervision, Carla and artefact collection; the scenario
owns its expectations. Isolation includes the user database: u-he plugins find their data through
the passwd home rather than `HOME`, so a probe runs under `nss_wrapper` with a passwd entry whose
home is its own. Without it Protoverb passed locally on the developer's own `~/.u-he` and showed
its 600x400 data-missing screen on CI. A home that fresh is also every plugin's first run, so a
plugin that covers its editor on first run gets the answer it would have saved written into the
scenario's home by `Installed.Settle`, after the install and before the editor opens, the way the
setup does for the fixtures: Sitala 1 would otherwise open its Update window, a separate window
the editor capture never shows, and every press would land on it.

An instrument that ships nothing to play is given something: `Content` writes a half-second
decaying tone, a DrumGizmo kit or a Decent Sampler group around it, and a Carla state file that
points the plugin at it, which the render loads with `carla_load_plugin_state`. Carla drops the
first character and the last five of every chunk-typed `<Value>`, since its own writer wraps one
in a newline and a newline with four spaces, so `Content` writes that wrapping too; written plainly
the value arrives garbled and the plugin keeps its empty default. A plugin that mutes itself until
a button is pressed gets the press: `click` names the positions, and both the editor probe and the
render open the editor and press them before measuring, since a press in one process unmutes only
that instance. The render then keeps processing silence in real time for five seconds, because a
plugin that loads samples on a thread of its own starts when it is processed, and offline the
whole render would pass before the first sample arrived. Where a plugin's knobs are its own
settings rather than host parameters, as DrumGizmo's and Decent Sampler's are, the scenario says
so with `controlsAreParameters: false`, and every editor assertion but the aimed drag still holds.
Where the aim cannot find a knob, the scenario names one with `control`, as its parameter and
editor-relative position, and the probe drags it before any it locates itself. Podolski redraws
only its text fields when the host moves a parameter, so locating finds no knob, and the sweep's
four points fall between them; its Attack knob, named, moves parameter 27 and redraws.
An editor whose controls are neither host parameters nor under the sweep is given `press`: points
the probe presses as part of the measurement, each compared with the frame just before it.
Melodyne is that editor. Its entry is bring-your-own, but `InstalledEntry` installs its pinned
demo, and the scenario answers the welcome window with Melodyne player, the mode that needs no
account, closes the tips window after it, and presses the inspector toggle. Nothing is
transferred into it, so it passes the tone through at the level it came in.

A press is followed by a wait for the editor to answer it: at least 2% of it must change from the
frame before the press, and then two frames in a row must match, up to a minute. Stillness alone
is not enough, because Helix Native stops drawing while it talks to Line 6's server, and a frozen
frame looks settled.

The render probe, `Cabinet.Probes audio-render` (`tools/`), opens Carla with `dlopen` the way the
editor probes load Carla: linked into an executable, Carla's bundled asio takes the place of
yabridge's own and the VST3 bridge crashes while it loads. Carla hosts no ARA, so for an ARA plugin
`RenderAsTake` has the runtime's REAPER add it through `RenderTake.lua` as take FX to a noisy
synthetic voice, wait for its analysis (`-renderproject` alone renders before it ends) and apply it;
`Take` reports the noise removed, the change in the voice and the difference. REAPER's private bus
starts only Flatpak's session helper: a document portal there would unmount the Toolbox's
`/run/user/<uid>/doc`.
