using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cabinet.Runtime.Tests.Scenarios;

internal sealed class Browser : IDisposable
{
    private static readonly TimeSpan Beat = TimeSpan.FromSeconds(1);

    private readonly string evidence;
    private readonly Process driver;
    private readonly HttpClient client;
    private readonly string session;
    private readonly string profile = Directory.CreateTempSubdirectory("cabinet-browser-").FullName;

    public Browser(string evidence)
    {
        this.evidence = evidence;
        var port = FreePort();
        driver = Process.Start(new ProcessStartInfo("chromedriver", $"--port={port}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("could not start chromedriver");
        client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromMinutes(1) };
        try
        {
            session = Until(() => Started(), TimeSpan.FromSeconds(30), "chromedriver to answer")!;
        }
        catch
        {
            Stop();
            throw;
        }
    }

    public void Go(string url) => Call(HttpMethod.Post, "url", new JsonObject { ["url"] = url });

    public JsonNode? Script(string script) =>
        Call(HttpMethod.Post, "execute/sync", new JsonObject { ["script"] = script, ["args"] = new JsonArray() });

    public void Type(string selector, string text)
    {
        var found = Call(HttpMethod.Post, "element", new JsonObject { ["using"] = "css selector", ["value"] = selector })!;
        var element = found.AsObject().Single().Value!.GetValue<string>();
        Call(HttpMethod.Post, $"element/{element}/value", new JsonObject { ["text"] = text });
    }

    public void Press(string text) =>
        Script($"[...document.querySelectorAll('a,button')].find(e => e.innerText.trim() === {JsonSerializer.Serialize(text)}).click()");

    public bool Shows(string selector) =>
        Script($"const e = document.querySelector({JsonSerializer.Serialize(selector)}); return e !== null && e.offsetParent !== null")!
            .GetValue<bool>();

    public string Path() => Script("return location.host + location.pathname")!.GetValue<string>();

    public IEnumerable<string> Requests() =>
        Call(HttpMethod.Post, "se/log", new JsonObject { ["type"] = "performance" })!.AsArray()
            .Select(entry => JsonNode.Parse(entry!["message"]!.GetValue<string>())!["message"]!)
            .Where(message => message["method"]!.GetValue<string>() == "Network.requestWillBeSent")
            .Select(message => message["params"]!["request"]!["url"]!.GetValue<string>());

    public static T Until<T>(Func<T?> found, TimeSpan patience, string what)
    {
        var deadline = DateTime.UtcNow + patience;
        while (DateTime.UtcNow < deadline)
        {
            if (found() is { } value && !Equals(value, false))
            {
                return value;
            }

            Thread.Sleep(Beat);
        }

        throw new TimeoutException($"{what} did not happen within {patience}");
    }

    public T Await<T>(Func<T?> found, TimeSpan patience, string what)
    {
        try
        {
            return Until(found, patience, what);
        }
        catch (TimeoutException)
        {
            Keep(what);
            throw;
        }
    }

    public void Dispose()
    {
        try
        {
            client.DeleteAsync($"session/{session}").Wait();
        }
        finally
        {
            Stop();
        }
    }

    private void Stop()
    {
        client.Dispose();
        driver.Kill(entireProcessTree: true);
        driver.WaitForExit();
        driver.Dispose();
        Directory.Delete(profile, recursive: true);
    }

    private void Keep(string what)
    {
        Directory.CreateDirectory(evidence);
        var shown = Script(
            "return [document.title, ...[...document.querySelectorAll('h1,h2,[role=alert]')].map(e => e.innerText.trim())]"
            + ".filter(text => text).join('\\n')")!.GetValue<string>();
        File.WriteAllText(
            System.IO.Path.Combine(evidence, "browser.txt"),
            $"waiting for: {what}\npage: {Path()}\n\n{shown}\n");
    }

    private string? Started()
    {
        try
        {
            var response = client.PostAsync("session", Body(new JsonObject
            {
                ["capabilities"] = new JsonObject
                {
                    ["alwaysMatch"] = new JsonObject
                    {
                        ["goog:loggingPrefs"] = new JsonObject { ["performance"] = "ALL" },
                        ["goog:chromeOptions"] = new JsonObject
                        {
                            ["args"] = new JsonArray("--headless=new", "--no-sandbox", $"--user-data-dir={profile}", "--window-size=1200,900"),
                        },
                    },
                },
            })).Result;
            return JsonNode.Parse(response.Content.ReadAsStringAsync().Result)!["value"]!["sessionId"]!.GetValue<string>();
        }
        catch (AggregateException refused) when (refused.InnerException is HttpRequestException)
        {
            return null;
        }
    }

    private JsonNode? Call(HttpMethod method, string path, JsonObject body)
    {
        using var request = new HttpRequestMessage(method, $"session/{session}/{path}") { Content = Body(body) };
        using var response = client.Send(request);
        var answer = JsonNode.Parse(response.Content.ReadAsStream())!["value"];
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"WebDriver {path} failed: {answer?["message"]}");
        }

        return answer;
    }

    private static StringContent Body(JsonObject body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
