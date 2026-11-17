namespace Cabinet.Runtime.Tests.Scenarios;

public abstract class SonibleEntry(string id, string product, string preferences = "preferences.plist")
    : InstalledEntry(id)
{
    private const string FirstRunAnswered = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <dict>
          <key>GuidedTourTaken</key>
          <string>true</string>
          <key>guidedTourTaken</key>
          <string>true</string>
          <key>GroupMatrixInstructionSlideSeen</key>
          <string>true</string>
          <key>AnalyticsConsentGiven</key>
          <string>false</string>
          <key>AutoLearnInNewInstance</key>
          <string>false</string>
          <key>AutoLearnOnStartUp</key>
          <string>false</string>
          <key>AutoLearnOnAudioPlayback</key>
          <string>false</string>
        </dict>
        """;

    protected override void Settle(string home)
    {
        var user = Directory.GetDirectories(Path.Combine(Harness.Prefix, "drive_c", "users"))
            .Single(path => Directory.Exists(Path.Combine(path, "AppData")) && Path.GetFileName(path) != "Public");
        var settings = Path.Combine(user, "AppData", "Roaming", $"com.sonible.{product}");
        Directory.CreateDirectory(settings);
        File.WriteAllText(Path.Combine(settings, preferences), FirstRunAnswered);
    }
}
