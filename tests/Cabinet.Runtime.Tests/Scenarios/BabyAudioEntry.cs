namespace Cabinet.Runtime.Tests.Scenarios;

public abstract class BabyAudioEntry(string id) : InstalledEntry(id)
{
    private const string QuickTourSeen = "1";

    protected override void Settle(string home)
    {
        var settings = Path.Combine(Harness.Prefix, "drive_c", "ProgramData", "BABY Audio", "settings", Entry.Name);
        Directory.CreateDirectory(settings);
        File.WriteAllText(Path.Combine(settings, "quick.baby"), QuickTourSeen);
    }
}
