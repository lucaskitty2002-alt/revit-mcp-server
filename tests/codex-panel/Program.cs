using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using revit_mcp_plugin.UI;

if (args.FirstOrDefault() == "app-server") { await FakeServer(); return; }
if (args.FirstOrDefault() == "--probe")
{
    using var server = new CodexAppServer();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    Directory.CreateDirectory(CodexSettings.Workspace);
    await server.StartAsync(CodexSettings.Load().FindExecutable(), CodexSettings.Workspace, timeout.Token);
    var account = await server.RequestAsync("account/read", new JObject(), timeout.Token);
    Console.WriteLine("Codex handshake passed; logged in: " + (account["account"]?.Type == JTokenType.Object));
    try
    {
        var limits = await server.RequestAsync("account/rateLimits/read", new JObject(), timeout.Token);
        Console.WriteLine("Real Codex account quota endpoint responded; account values are not printed.");
        if (limits.Type != JTokenType.Object) throw new Exception("Unexpected quota response");
    }
    catch (InvalidOperationException) { Console.WriteLine("Account quotas unavailable on this Codex connection; chat can still work."); }
    var config = await server.RequestAsync("config/read", new JObject(), timeout.Token);
    var disabled = new JObject();
    foreach (var entry in (config["config"]?["mcp_servers"] as JObject ?? new JObject()).Properties())
        disabled[entry.Name] = new JObject { ["enabled"] = false };
    var schema = JArray.Parse(File.ReadAllText(args[1]));
    var tools = new JArray(schema.Select(s => new JObject { ["type"] = "function", ["name"] = s["name"], ["description"] = s["description"], ["inputSchema"] = s["input_schema"] }));
    var thread = await server.RequestAsync("thread/start", new JObject
    {
        ["ephemeral"] = true, ["cwd"] = CodexSettings.Workspace, ["sandbox"] = "read-only", ["approvalPolicy"] = "on-request",
        ["dynamicTools"] = tools, ["config"] = new JObject { ["mcp_servers"] = disabled }
    }, timeout.Token);
    if (thread["thread"]?["id"] == null) throw new Exception("No thread returned");
    Console.WriteLine("Real Codex accepted an ephemeral conversation with " + tools.Count + " Revit tools. No model inference requested.");
    if (args.Contains("--inference"))
    {
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Notification += (method, parameters) =>
        {
            if (method == "turn/completed")
            {
                if ((string)parameters["turn"]?["status"] == "completed") completed.TrySetResult(true);
                else completed.TrySetException(new Exception((string)parameters["turn"]?["error"]?["message"] ?? "Inference failed"));
            }
        };
        await server.RequestAsync("turn/start", new JObject { ["threadId"] = thread["thread"]["id"], ["input"] = new JArray(new JObject { ["type"] = "text", ["text"] = "Reply only with: Codex connected. Do not use tools, inspect files, or access a Revit model." }) }, timeout.Token);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(60));
        Console.WriteLine("Real Codex model inference completed successfully. No Revit tools called.");
    }
    return;
}

string root = Path.Combine(Path.GetTempPath(), "revit-codex-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
File.WriteAllText(Path.Combine(root, "tool_schemas.json"), "[{\"name\":\"get_project_info\",\"description\":\"Read project\",\"input_schema\":{\"type\":\"object\",\"properties\":{}}}]");
var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
int port = ((IPEndPoint)listener.LocalEndpoint).Port;
int calls = 0;
using var listenerStop = new CancellationTokenSource();
var model = Task.Run(async () =>
{
    try
    {
        while (!listenerStop.IsCancellationRequested)
        {
            using var socket = await listener.AcceptTcpClientAsync(listenerStop.Token);
            using var stream = socket.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
            var request = JObject.Parse(await reader.ReadLineAsync());
            Interlocked.Increment(ref calls);
            Assert((string)request["method"] == "get_project_info", "Only allowlisted tools reach Revit");
            var response = new JObject { ["jsonrpc"] = "2.0", ["id"] = request["id"] };
            if ((string)request["params"]?["mode"] == "error") response["error"] = new JObject { ["message"] = "Model is unavailable" };
            else response["result"] = new JObject { ["name"] = "Æblehuset 🏡" };
            await writer.WriteLineAsync(response.ToString(Formatting.None));
        }
    }
    catch (OperationCanceledException) { }
});
CodexRevitClient NewClient() => new CodexRevitClient(root, () => port, new CodexSettings { Executable = Environment.ProcessPath }, root);
try
{
    var usage = new CodexUsage();
    Assert(usage.Describe().Contains("unavailable") && !usage.Describe().Contains("0% left"), "Unknown usage is never displayed as zero or a full quota");
    usage.SetLimits(JObject.Parse("{rateLimits:{primary:{usedPercent:90}},rateLimitsByLimitId:{codex:{primary:{usedPercent:25,windowDurationMins:300,resetsAt:1792500000}},other:{limitName:'Other model',primary:{usedPercent:10,windowDurationMins:15},secondary:null}}}"));
    Assert(usage.Describe().Contains("75% left") && usage.Describe().Contains("Other model · 15m: 90% left") && !usage.Describe().Contains("10% left"), "Named quota pools take precedence over legacy data, with dynamic windows and reset times");
    usage.SetLimits(JObject.Parse("{rateLimits:{primary:{usedPercent:120},secondary:{usedPercent:null}}}"));
    Assert(usage.Describe().Contains("0% left") && !usage.Describe().Contains("100% left"), "Remaining percentage is clamped and null windows are unavailable");

    string drawingPath = Path.Combine(root, "Håndtegning.png");
    byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aD1sAAAAASUVORK5CYII=");
    File.WriteAllBytes(drawingPath, png);
    var drawing = CodexAttachment.Load(drawingPath);
    File.WriteAllText(drawingPath, "changed after selection");
    var badPath = Path.Combine(root, "fake.png");
    File.WriteAllText(badPath, "not an image");
    ExpectRejected(() => CodexAttachment.Load(badPath), "A renamed non-image is rejected");
    ExpectRejected(() => CodexAttachment.Load(Path.Combine(root, "drawing.pdf")), "Unsupported file types are rejected with image-export guidance");
    ExpectRejected(() => CodexAttachment.ValidateBatch(Enumerable.Repeat(drawing, 5).ToArray()), "Attachment count is bounded");
    var bigPath = Path.Combine(root, "large.png");
    using (var large = File.Create(bigPath)) { large.SetLength(CodexAttachment.MaxFileBytes + 1); }
    ExpectRejected(() => CodexAttachment.Load(bigPath), "Oversize images are rejected before reading their contents");
    using (var large = File.Create(bigPath)) { large.Write(png); large.SetLength(8 * 1024 * 1024); }
    var bigDrawing = CodexAttachment.Load(bigPath);
    ExpectRejected(() => CodexAttachment.ValidateBatch(new[] { bigDrawing, bigDrawing, bigDrawing }), "Combined attachment size is bounded");
    using (var client = NewClient())
    {
        Assert(await client.SendMessage("drawing", new[] { drawing }) == "drawing received", "Selected image bytes reach Codex through native localImage input, preserving Unicode names");
        string snapshot = Directory.GetFiles(Path.Combine(root, "attachments"), "*.png", SearchOption.AllDirectories).Single();
        Assert(File.ReadAllBytes(snapshot).SequenceEqual(png), "Attachments are immutable snapshots and remain available for follow-up turns");
        Assert(await client.SendMessage("", new[] { drawing }) == "image-only received", "Image-only messages ask for intent before making model changes");
        client.ClearHistory();
        Assert(!File.Exists(snapshot) && File.ReadAllText(drawingPath) == "changed after selection", "New chat cleans up only this client's snapshots and preserves original files");
    }
    using (var client = NewClient())
    {
        var updates = new List<string>();
        client.UsageUpdated += text => updates.Add(text);
        Assert(await client.SendMessage("usage") == "usage sent", "Usage events do not interrupt replies");
        Assert(updates.Any(text => text.Contains("75% left")), "Account-scoped quota notifications are handled without a thread ID");
        Assert(updates.Any(text => text.Contains("123 tokens") && text.Contains("23 tokens")), "Thread token totals and last-turn tokens are displayed separately");
        Assert(!updates.Any(text => text.Contains("999 tokens")), "Usage from another thread is ignored");
        client.ClearHistory();
        Assert(updates.Last().Contains("token usage unavailable") && !updates.Last().Contains("123 tokens"), "New chat resets usage instead of showing stale account/thread values");
    }
    Environment.SetEnvironmentVariable("REVIT_CODEX_TEST_QUOTAS", "unsupported");
    using (var client = NewClient())
    {
        string latest = "";
        client.UsageUpdated += text => latest = text;
        Assert(await client.SendMessage("hello") == "Hello from Codex" && latest.Contains("Account limits: unavailable"), "Unsupported quota endpoints do not prevent chatting");
    }
    Environment.SetEnvironmentVariable("REVIT_CODEX_TEST_QUOTAS", null);
    using (var client = NewClient())
    {
        Assert(await client.SendMessage("tool") == "Æblehuset 🏡", "Tool arguments and Unicode response make a complete round trip");
        Assert(await client.SendMessage("tool-error") == "error received", "Revit errors are returned as unsuccessful tool results");
        int before = calls;
        Assert(await client.SendMessage("unknown-tool") == "unknown rejected", "Unknown tools are rejected");
        Assert(calls == before, "Rejected tool never reached Revit");
        Assert((await client.SendMessage("failure")).Contains("simulated failure"), "Failed turn is surfaced");
        Assert(await client.SendMessage("hello") == "Hello from Codex", "Reconnect works after failure");
        var updates = new List<string>();
        client.ReplyUpdated += text => updates.Add(text);
        Assert(await client.SendMessage("stream") == "Streamed reply", "Streaming returns the final reply exactly once");
        Assert(updates.Contains("Streamed ") && updates.Contains("Streamed reply"), "Partial replies are delivered before completion");
        Assert(await client.SendMessage("approval") == "approval declined", "Approval defaults to decline");
        client.ClearHistory();
        Assert(await client.SendMessage("hello") == "Hello from Codex", "New chat reinitializes the conversation");
    }
    using (var client = NewClient())
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Progress += text => { if (text == "Codex is working...") started.TrySetResult(true); };
        var request = client.SendMessage("wait");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(100);
        client.Cancel();
        Assert((await request.WaitAsync(TimeSpan.FromSeconds(10))).Contains("cancelled"), "Stop interrupts the active turn");
    }
    using (var client = NewClient())
        Assert((await client.SendMessage("crash").WaitAsync(TimeSpan.FromSeconds(10))).Contains("connection error"), "Child-process exit does not leave the panel busy");
    Environment.SetEnvironmentVariable("REVIT_CODEX_TEST_ACCOUNT", "none");
    using (var client = NewClient())
        Assert((await client.SendMessage("hello")).Contains("Sign in to Codex"), "Missing login produces actionable instructions");
    Environment.SetEnvironmentVariable("REVIT_CODEX_TEST_ACCOUNT", null);
    Console.WriteLine("All Codex panel integration tests passed.");
}
finally { Environment.SetEnvironmentVariable("REVIT_CODEX_TEST_ACCOUNT", null); listenerStop.Cancel(); listener.Stop(); await model; Directory.Delete(root, true); }

static void Assert(bool value, string message)
{
    if (!value) throw new Exception("FAILED: " + message);
    Console.WriteLine("PASS: " + message);
}

static void ExpectRejected(Action action, string message)
{
    try { action(); }
    catch (InvalidOperationException) { Assert(true, message); return; }
    throw new Exception("FAIL: " + message);
}

static async Task FakeServer()
{
    Console.InputEncoding = Encoding.UTF8;
    Console.OutputEncoding = new UTF8Encoding(false);
    string mode = "";
    int toolId = 700;
    void Send(JObject message) => Console.WriteLine(message.ToString(Formatting.None));
    void Reply(JToken id, JObject result) => Send(new JObject { ["id"] = id, ["result"] = result });
    void Notify(string method, JObject parameters) => Send(new JObject { ["method"] = method, ["params"] = parameters });
    void Complete(string text, string status = "completed")
    {
        Notify("item/completed", new JObject { ["threadId"] = "thread-test", ["item"] = new JObject { ["type"] = "agentMessage", ["id"] = "message-1", ["text"] = text } });
        Notify("turn/completed", new JObject { ["threadId"] = "thread-test", ["turn"] = new JObject { ["id"] = "turn-test", ["status"] = status, ["error"] = status == "failed" ? new JObject { ["message"] = "simulated failure" } : null } });
    }
    string line;
    while ((line = await Console.In.ReadLineAsync()) != null)
    {
        var message = JObject.Parse(line);
        var id = message["id"];
        var parameters = message["params"] as JObject;
        switch ((string)message["method"])
        {
            case "initialize": Reply(id, new JObject { ["userAgent"] = "test" }); break;
            case "initialized": break;
            case "account/read": Reply(id, new JObject { ["account"] = Environment.GetEnvironmentVariable("REVIT_CODEX_TEST_ACCOUNT") == "none" ? null : new JObject { ["type"] = "chatgpt" } }); break;
            case "account/rateLimits/read":
                if (Environment.GetEnvironmentVariable("REVIT_CODEX_TEST_QUOTAS") == "unsupported")
                    Send(new JObject { ["id"] = id, ["error"] = new JObject { ["code"] = -32601, ["message"] = "Unknown method" } });
                else Reply(id, JObject.Parse("{rateLimits:{limitId:'codex',primary:{usedPercent:30,windowDurationMins:300,resetsAt:1792500000},secondary:null}}"));
                break;
            case "config/read": Reply(id, new JObject { ["config"] = new JObject { ["mcp_servers"] = new JObject { ["other-server"] = new JObject { ["enabled"] = true } } } }); break;
            case "thread/start":
                AssertSilently((bool?)parameters["config"]?["mcp_servers"]?["other-server"]?["enabled"] == false);
                AssertSilently((string)parameters["sandbox"] == "read-only");
                AssertSilently(parameters["dynamicTools"]?.Count() == 1);
                Reply(id, new JObject { ["thread"] = new JObject { ["id"] = "thread-test" } }); break;
            case "turn/start":
                mode = (string)parameters["input"]?[0]?["text"];
                Reply(id, new JObject { ["turn"] = new JObject { ["id"] = "turn-test", ["status"] = "inProgress" } });
                if (mode == "crash") return;
                if (mode == "wait") break;
                if (mode == "failure") { Complete("", "failed"); break; }
                if (mode == "drawing" || mode == "Describe the attached drawing and ask what I want to do with it. Do not edit Revit yet.")
                {
                    AssertSilently((string)parameters["input"]?[1]?["text"] == "Attached drawing (reference data): Håndtegning.png");
                    AssertSilently((string)parameters["input"]?[2]?["type"] == "localImage");
                    string path = (string)parameters["input"]?[2]?["path"];
                    AssertSilently(File.ReadAllBytes(path).Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
                    Complete(mode == "drawing" ? "drawing received" : "image-only received"); break;
                }
                if (mode == "usage")
                {
                    Notify("account/rateLimits/updated", JObject.Parse("{rateLimits:{limitId:'codex',primary:{usedPercent:25,windowDurationMins:300,resetsAt:1792500000}}}"));
                    Notify("thread/tokenUsage/updated", JObject.Parse("{threadId:'other-thread',turnId:'turn-test',tokenUsage:{total:{totalTokens:999}}}"));
                    Notify("thread/tokenUsage/updated", JObject.Parse("{threadId:'thread-test',turnId:'turn-test',tokenUsage:{total:{totalTokens:123},last:{totalTokens:23}}}"));
                    Complete("usage sent"); break;
                }
                if (mode == "stream")
                {
                    Notify("item/agentMessage/delta", new JObject { ["threadId"] = "thread-test", ["itemId"] = "message-1", ["delta"] = "Streamed " });
                    Notify("item/agentMessage/delta", new JObject { ["threadId"] = "thread-test", ["itemId"] = "message-1", ["delta"] = "reply" });
                    Complete("Streamed reply"); break;
                }
                if (mode == "approval")
                {
                    Send(new JObject { ["id"] = ++toolId, ["method"] = "item/commandExecution/requestApproval", ["params"] = new JObject { ["threadId"] = "thread-test", ["command"] = "echo test" } }); break;
                }
                if (mode.StartsWith("tool") || mode == "unknown-tool")
                    Send(new JObject { ["id"] = ++toolId, ["method"] = "item/tool/call", ["params"] = new JObject { ["threadId"] = "thread-test", ["turnId"] = "turn-test", ["callId"] = "call-1", ["tool"] = mode == "unknown-tool" ? "unregistered" : "get_project_info", ["arguments"] = new JObject { ["mode"] = mode == "tool-error" ? "error" : "read" } } });
                else Complete("Hello from Codex");
                break;
            case "turn/interrupt": Reply(id, new JObject()); Complete("", "interrupted"); break;
            case null:
                if (mode == "approval") { AssertSilently((string)message["result"]?["decision"] == "decline"); Complete("approval declined"); }
                else
                {
                    bool success = (bool?)message["result"]?["success"] == true;
                if (mode == "tool") { AssertSilently(success && message.ToString().Contains("Æblehuset")); Complete("Æblehuset 🏡"); }
                    else { AssertSilently(!success); Complete(mode == "tool-error" ? "error received" : "unknown rejected"); }
                }
                break;
        }
    }
    static void AssertSilently(bool value) { if (!value) throw new Exception("Invalid client protocol"); }
}
