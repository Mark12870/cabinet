using System.Diagnostics;
using System.Text;
using Cabinet.Core;
using Cabinet.Core.Tests;

namespace Cabinet.Contract.Tests;

internal sealed class Shim : IDisposable
{
    public const string Prefix = "contract";

    private const UnixFileMode Executable =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private static readonly IReadOnlyDictionary<string, string?> Inherited =
        new Dictionary<string, string?>
        {
            ["DISPLAY"] = null,
            ["WAYLAND_DISPLAY"] = "contract-wayland",
            ["CABINET_BLANK"] = "inherited",
            ["CABINET_LATE"] = "inherited",
        };

    private readonly string root = Directory.CreateTempSubdirectory("cabinet-contract-").FullName;
    private readonly Dictionary<string, string?> restored = [];

    public Shim()
    {
        var built = Repo.Path("shim/target/debug/cabinet-wine");

        if (!File.Exists(built))
        {
            throw new FileNotFoundException("run `cargo build` in shim/ first", built);
        }

        Layout = new Layout(
            Path.Combine(root, "home"), root, hostAppFiles: Path.Combine(root, "app"));
        Prefixes = new Prefixes(Layout, new ProcessRunner());

        Directory.CreateDirectory(Layout.HostYabridgeDir);
        File.CreateSymbolicLink(Layout.ShimPath, built);
        Directory.CreateDirectory(Layout.PrefixPath(Prefix));

        var bin = Directory.CreateDirectory(Path.Combine(root, "bin")).FullName;
        Script(bin, "flatpak", Flatpak(built));
        Script(bin, "flatpak-spawn", FlatpakSpawn);
        Script(bin, "wine", Wine);

        Set("PATH", bin + ":" + Environment.GetEnvironmentVariable("PATH"));
        foreach (var (key, value) in Inherited)
        {
            Set(key, value);
        }
    }

    public Layout Layout { get; }

    public Prefixes Prefixes { get; }

    public IReadOnlyList<string> Sockets =>
        Directory.Exists(Layout.SocketDir)
            ? Directory.EnumerateFiles(Layout.SocketDir, "*.sock").ToList()
            : [];

    public string Scratch(string name) => Path.Combine(root, name);

    public void Settle(string name) => Prefixes.RunJoined(Prefix, ["exit", "0"], logTo: Scratch(name));

    public ProcessResult Plugin(IReadOnlyList<string> job) =>
        new ProcessRunner().Run(Layout.ShimPath, job, Prefixes.Variables(Prefix));

    public void GiveUnusableRunner(string name)
    {
        var wine = Path.Combine(Layout.RunnerPath(name), "bin", "wine");
        Directory.CreateDirectory(Path.GetDirectoryName(wine)!);
        File.WriteAllText(wine, "#!/bin/sh\nexit 0\n");
        File.SetUnixFileMode(wine, UnixFileMode.UserRead);
        File.WriteAllText(Layout.PrefixRunnerFile(Prefix), name + "\n");
    }

    public void WriteEnvironment(params string[] lines) =>
        File.WriteAllLines(Layout.PrefixEnvFile(Prefix), lines);

    public static bool Appears(string path) =>
        SpinWait.SpinUntil(() => File.Exists(path), TimeSpan.FromSeconds(30));

    public bool NoBroker() =>
        SpinWait.SpinUntil(() => !Brokers().Any(), TimeSpan.FromSeconds(30));

    public void Dispose()
    {
        foreach (var broker in Brokers())
        {
            broker.Kill(entireProcessTree: true);
            broker.WaitForExit();
        }

        foreach (var (key, value) in restored)
        {
            Environment.SetEnvironmentVariable(key, value);
        }

        Directory.Delete(root, recursive: true);
    }

    private IEnumerable<Process> Brokers() =>
        Directory.EnumerateDirectories("/proc")
            .Select(Path.GetFileName)
            .Where(name => int.TryParse(name, out _))
            .Where(name => CommandLine(name!).Contains(root, StringComparison.Ordinal))
            .Select(name => Process.GetProcessById(int.Parse(name!)))
            .ToList();

    private static string CommandLine(string pid)
    {
        try
        {
            return Encoding.UTF8.GetString(File.ReadAllBytes($"/proc/{pid}/cmdline"));
        }
        catch (IOException)
        {
            return "";
        }
        catch (UnauthorizedAccessException)
        {
            return "";
        }
    }

    private void Set(string key, string? value)
    {
        restored.TryAdd(key, Environment.GetEnvironmentVariable(key));
        Environment.SetEnvironmentVariable(key, value);
    }

    private static void Script(string bin, string name, string body)
    {
        var path = Path.Combine(bin, name);
        File.WriteAllText(path, body);
        File.SetUnixFileMode(path, Executable);
    }

    private static string Flatpak(string shim) => $$"""
        #!/bin/sh
        shift
        for arg do
          shift
          case $arg in
            --env=*) export "${arg#--env=}" ;;
            --*) ;;
            *) exec '{{shim}}' "$@" ;;
          esac
        done
        """;

    private const string FlatpakSpawn = """
        #!/bin/sh
        for arg do
          case $arg in
            --host) shift ;;
            --env=*) export "${arg#--env=}"; shift ;;
            *) exec "$@" ;;
          esac
        done
        """;

    private const string Wine = """
        #!/bin/sh
        case $1 in
          exit)
            echo "out $2"
            echo "err $2" >&2
            exit "$2" ;;
          env)
            shift
            for name do
              if printenv "$name" >/dev/null; then
                echo "$name=[$(printenv "$name")]"
              else
                echo "$name unset"
              fi
            done ;;
          stdin)
            if read value; then echo read; else echo eof; fi ;;
          hold)
            : > "$2"
            while [ ! -e "$3" ]; do sleep 0.05; done ;;
        esac
        """;
}
