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
           var bridge = installed.Harness.Bridged(format, Bridges[format]);

           installed.Harness.VerifyEditor(bridge);
           var audio = await installed.Harness.Render(bridge, installed.Display);

           Assert.True(audio.Parameters > 0, $"{installed.Entry.Name} exposed no parameters");
           Assert.True(audio.MixChanged, $"{installed.Entry.Name}'s Mix did not hold fully wet");
           Assert.InRange(audio.Before, 0, 0.00001);
           Assert.InRange(audio.Tail, 0.0025, 1);
           Assert.InRange(audio.Peak, 0.014, 1);
       }

       public sealed class Installed() : InstalledEntry(Id);
   }
   ```

   Only the file name and class name, `Id`, `Bridges`, the test name and the audio floors change
   between scenarios. Keep the test free of conditions and loops. `Harness.Bridged` knows `VST3`
   and `VST2`; a format it does not know has to be added to `ScenarioHarness.Formats` first.

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
     dotnet test tests/Cabinet.Runtime.Tests --nologo -m:1 -p:BuildInParallel=false \
     --filter 'FullyQualifiedName~<Entry>Scenario'
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
   - The probe feeds audio and sends no MIDI, so it measures effects only.

6. When a case fails, read that format's artefacts in `$A/editor/<format>/` and `$A/audio/<format>/`
   before changing anything. `yabridge.log`, `probe.log`, the captures and `render.log` hold the
   evidence. If the render exits with 139 (a segfault), rerun just that probe under
   `gdb -batch -ex run -ex bt` in the Toolbox. Use the scenario home and the environment that
   `ScenarioHarness.Render` sets, then map the faulting library offset with `objdump` inside the
   Toolbox.

7. Check that every scenario names a shipped entry, then run the full checks:

   ```sh
   dotnet test tests/Cabinet.Core.Tests --filter 'FullyQualifiedName~CatalogueTests'
   scripts/checks.sh
   ```

8. Confirm the entry has left the list of entries without a scenario:

   ```sh
   scripts/scenario-coverage.sh
   ```

9. Confirm no scenario sandbox is still running
   (`toolbox run --container cabinet-runtime flatpak ps`), then commit on main.
