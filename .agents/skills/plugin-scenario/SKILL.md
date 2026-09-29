---
name: plugin-scenario
description: Write, run or debug an end-to-end runtime scenario for one Cabinet catalogue entry. Use when adding a test under `tests/Cabinet.Runtime.Tests/Scenarios/`, when an entry's version, URL or formats change, or when a scenario fails.
---

# Plugin scenarios

1. Prepare the isolated runtime as `runtime-test` step 2 describes. Never install, open or remove
   the entry through the host Cabinet data, and never drive a DAW.

2. Create `tests/Cabinet.Runtime.Tests/Scenarios/<Entry>Scenario.cs`. The entry id appears only
   as `Id`. The download, the pinned version and the formats come from the `.yml` through
   `InstalledEntry`:

   ```csharp
   namespace Cabinet.Runtime.Tests.Scenarios;

   public sealed class ValhallaSupermassiveScenario(ValhallaSupermassiveScenario.Installed installed)
       : IClassFixture<ValhallaSupermassiveScenario.Installed>
   {
       private const string Id = "valhalla-supermassive";

       private const string Mix = "Mix";

       private static readonly Dictionary<string, string> Bridges = new()
       {
           ["VST3"] = "ValhallaSupermassive.vst3",
           ["VST2"] = "ValhallaSupermassive_x64.so",
       };

       public static TheoryData<string> Formats => InstalledEntry.Formats(Id);

       [Theory]
       [MemberData(nameof(Formats))]
       public async Task OpensItsEditorAndReverberates(string format)
       {
           Assert.True(
               Bridges.ContainsKey(format),
               $"{Id} declares {format}, which this scenario does not say how to find");
           var bridge = installed.Harness.Plugin(format, Bridges[format]);

           var editor = installed.Harness.VerifyEditor(bridge);
           Assert.True(
               editor.Wine != "none" && editor.Wine == editor.Told,
               $"Wine places the editor at {editor.Wine} but was told {editor.Told}, so clicks land that far away");
           var audio = await installed.Harness.Render(bridge, Mix, installed.Display);

           Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
           Assert.True(audio.MixChanged, $"{installed.Entry.Name}'s {Mix} did not hold fully wet");
           Assert.InRange(audio.Before, 0, 0.00001);
           Assert.InRange(audio.Tail, 0.0025, 1);
           Assert.InRange(audio.Peak, 0.014, 1);
       }

       public sealed class Installed() : InstalledEntry(Id);
   }
   ```

   Only the file name and class name, `Id`, `Mix`, `Bridges`, the test name and the audio floors
   change between scenarios. `Mix` names the parameter the render drives to its maximum. Keep the
   test free of conditions and loops. `Harness.Plugin` knows `VST3`, `VST2` and `LV2`, where an
   LV2 bridge is the plugin's URI; a format it does not know has to be added to
   `ScenarioHarness.Formats` first. The origin assertion is for bridged plugins only. A plugin
   that has no editor of its own skips `VerifyEditor` altogether and says so in its test name.

3. Fill `Bridges` with one bridged file name for each format in the entry's `Formats:`. To find
   the names, run the scenario once with `Bridges` empty and list what the install left:

   ```sh
   ROOT="${CABINET_RUNTIME_ROOT:-${XDG_CACHE_HOME:-$HOME/.cache}/cabinet-rt}"
   ls "$ROOT/tmp/scenarios/<id>/home/.vst3/cabinet/windows" \
      "$ROOT/tmp/scenarios/<id>/home/.vst/cabinet/windows"
   ```

4. Run the scenario on its own:

   ```sh
   toolbox run --container cabinet-runtime nice \
     dotnet build tests/Cabinet.Runtime.Tests --nologo -m:1 -p:BuildInParallel=false
   toolbox run --container cabinet-runtime nice \
     dotnet test --project tests/Cabinet.Runtime.Tests --no-build \
     --filter-class '*<Entry>Scenario'
   ```

   Run it in the background and wait for the completion event instead of polling. Each run
   downloads and installs the entry into a fresh home, once for the whole class.

5. Set the audio floors from a measured run, not a guess. Start with loose floors and read each
   format's `render.log`, `parameters.txt` and `output.wav`:

   ```sh
   A="$ROOT/tmp/scenarios/<id>/artefacts"
   grep -h AUDIO_RENDER "$A"/audio/*/render.log
   grep -h EDITOR= "$A"/editor/*/probe.log
   ```

   - Confirm the `Mix` row in `parameters.txt` reads its maximum.
   - Put the `Tail` and `Peak` floors about 20 dB below the measured values, a tenth of each.
   - `signal_rms` can legitimately be near zero: a fully wet delay speaks only after the 100 ms
     input has ended.
   - `Render` feeds an effect a 100 ms sine and sends no MIDI. An instrument calls
     `Harness.Play(bridge, Note, display)` instead: silence in, one note held for the same 100 ms.
     It has no `Mix` and asserts no `MixChanged`. `Note` is the scenario's to choose; a drum
     plugin sounds only on the keys it maps, 36 for a General MIDI kick. An effect that needs a
     note as well, such as a vocoder whose carrier is a synth, calls
     `Harness.Render(bridge, Mix, Note, display)`: the sine and the note over the same 100 ms.
   - An instrument that ships nothing to play gets content from `Content`: `Hit` writes a tone,
     and `Lv2State` or `ChunkState` writes the Carla state that points the plugin at it, passed
     as `Play(..., state: path)`. To learn what a plugin's state holds, load it in Carla with
     `PLUGIN_OPTION_USE_CHUNKS` set and `save_plugin_state` it; `DrumGizmoScenario` and
     `DecentSamplerScenario` are the two shapes, LV2 state and a JUCE chunk.
   - A plugin that stays silent until a button in its editor is pressed gets `click:` with the
     button's editor-relative positions, on `VerifyEditor` and on `Play` alike; `click.log` beside
     `render.log` shows where each press landed.

6. When a case fails, read that format's artefacts in `$A/editor/<format>/` and `$A/audio/<format>/`
   before changing anything. `yabridge.log`, `probe.log`, the captures and `render.log` hold the
   evidence. If the render exits with 139 (a segfault), rerun just that probe under
   `gdb -batch -ex run -ex bt` in the Toolbox. Use the scenario home and the environment that
   `ScenarioHarness.Render` sets, then map the faulting library offset with `objdump` inside the
   Toolbox.

   An editor that reacts to nothing, or whose presses never move a control, is often covered by
   something a first run shows: a language picker, a welcome screen, an update window. An update
   window is a window of its own, so `resting.png` may look clean while it takes every press. Look
   in the scenario home for what the plugin saved while it ran, and have the scenario's `Installed`
   override `Settle(home)` to write that answer before the editor opens. A dialog that asks for a
   licence, a sign-in or a key cannot be answered this way; that entry is set aside in `docs/TESTS.md`.

   `AIM=none` on an editor that draws and reacts can mean its knobs are the plugin's own settings
   rather than host parameters. Check `parameters.txt`: if nothing there is a control the editor
   shows, pass `controlsAreParameters: false` to `VerifyEditor` and say why in the scenario name.

7. Check that every scenario names a shipped entry, then run the full checks:

   ```sh
   dotnet test --project tests/Cabinet.Core.Tests --filter-class '*CatalogueTests'
   scripts/checks.sh
   ```

8. Confirm the entry has left the list of entries without a scenario:

   ```sh
   scripts/scenario-coverage.sh
   ```

9. Confirm no scenario sandbox is still running
   (`toolbox run --container cabinet-runtime flatpak ps`), then commit on main.
