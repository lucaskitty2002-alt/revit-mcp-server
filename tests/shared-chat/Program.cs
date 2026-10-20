using System.IO.Pipes;
using System.Text;
using Newtonsoft.Json.Linq;
using revit_mcp_plugin.UI;

static void Check(bool ok, string what) { if (!ok) throw new Exception(what); }
static async Task<JObject> Read(Stream pipe)
{
    var h = new byte[4]; await pipe.ReadExactlyAsync(h); int n = BitConverter.ToInt32(h);
    if (n <= 0 || n > 268435456) throw new Exception("frame length");
    var b = new byte[n]; await pipe.ReadExactlyAsync(b); return JObject.Parse(Encoding.UTF8.GetString(b));
}
static async Task Write(Stream pipe, JObject m)
{
    var b = Encoding.UTF8.GetBytes(m.ToString(Newtonsoft.Json.Formatting.None));
    var h = BitConverter.GetBytes(b.Length);
    // Fragment headers and payloads to exercise stream framing.
    await pipe.WriteAsync(h.AsMemory(0, 1)); await pipe.WriteAsync(h.AsMemory(1));
    for (int p = 0; p < b.Length; p += 17) await pipe.WriteAsync(b.AsMemory(p, Math.Min(17, b.Length-p)));
    await pipe.FlushAsync();
}
string thread = Guid.NewGuid().ToString();
JObject state = new JObject {
    ["id"] = thread, ["threadRuntimeStatus"] = new JObject { ["type"] = "idle" },
    ["turnHistory"] = new JObject { ["kind"] = "canonical", ["history"] = new JObject {
        ["islands"] = new JArray(new JObject { ["entries"] = new JArray(new JObject { ["value"] = "t1" }) }),
        ["entitiesByKey"] = new JObject { ["t1"] = new JObject {
            ["params"] = new JObject { ["input"] = new JArray(new JObject { ["type"] = "text", ["text"] = "Hej æøå" }) },
            ["items"] = new JArray(new JObject { ["type"] = "agentMessage", ["text"] = "Samme svar" }) } }
    } }
};
Check(CodexChatTranscript.Render(state).Contains("Hej æøå") && CodexChatTranscript.Render(state).Contains("Samme svar"), "canonical transcript");
Check(!CodexSharedChat.IsBusy(state), "idle");
if (args.Contains("--list"))
{
    using var serverList = new CodexAppServer();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
    await serverList.StartAsync(CodexSettings.Load().FindExecutable(), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), timeout.Token);
    var data = await serverList.RequestAsync("thread/list", new JObject { ["limit"] = 100, ["archived"] = false, ["sortKey"] = "updated_at" }, timeout.Token);
    string id = Environment.GetEnvironmentVariable("CODEX_THREAD_ID");
    var current = ((JArray)data["data"]).OfType<JObject>().Single(t => (string)t["id"] == id);
    Check(!string.IsNullOrWhiteSpace((string)current["name"] ?? (string)current["preview"]), "chat list title");
    Console.WriteLine("PASS live read-only chat list includes the current desktop thread.");
    return;
}
if (args.Contains("--probe"))
{
    using var chat = new CodexSharedChat();
    var snapshot = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
    chat.Snapshot += s => snapshot.TrySetResult(s);
    chat.Disconnected += e => snapshot.TrySetException(e);
    string id = Environment.GetEnvironmentVariable("CODEX_THREAD_ID") ?? throw new Exception("CODEX_THREAD_ID missing");
    await chat.ConnectAsync(id, CancellationToken.None);
    var actual = await snapshot.Task.WaitAsync(TimeSpan.FromSeconds(15));
    Check((string)actual["id"] == id && CodexChatTranscript.Turns(actual).Any(), "actual desktop same-thread history");
    Console.WriteLine("PASS live desktop owner and same-thread transcript; no message sent, no thread resumed.");
    return;
}
string name = "revit-shared-chat-test-" + Guid.NewGuid();
using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
var accepted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
var serverTask = Task.Run(async () => {
    await server.WaitForConnectionAsync();
    for (int step=0;step<4;step++)
    {
        var m = await Read(server);
        if (step == 2)
        {
            Check((string)m["method"] == "thread-stream-following-changed" && (string)m["params"]?["conversationId"] == thread, "follow correct thread");
            await Write(server, new JObject { ["type"] = "broadcast", ["method"] = "thread-stream-state-changed", ["version"] = 11,
                ["sourceClientId"] = "owner", ["params"] = new JObject { ["hostId"] = "local", ["conversationId"] = thread,
                    ["change"] = new JObject { ["type"] = "snapshot", ["revision"] = 1, ["conversationState"] = state } } });
            continue;
        }
        if (step == 3)
        {
            Check((string)m["method"] == "thread-follower-start-turn" && (int)m["version"] == 2 && (string)m["targetClientId"] == "owner", "owner send");
            var req = (JObject)m["params"]["turnStart"]["request"];
            Check((string)req["threadId"] == thread && (string)req["input"][0]["text"] == "Besked fra Revit æøå", "same thread and unicode");
            Check(!req.Properties().Any(p => new[]{"approvalPolicy","permissions","sandboxPolicy","model","cwd"}.Contains(p.Name)), "preserve desktop permissions/settings");
        }
        await Write(server, new JObject { ["type"] = "response", ["requestId"] = m["requestId"], ["resultType"] = "success",
            ["handledByClientId"] = "owner", ["result"] = step == 0 ? new JObject { ["clientId"] = "test-follower" } : new JObject() });
    }
    accepted.TrySetResult(true);
});
using (var chat = new CodexSharedChat(name))
{
    var snapshot = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
    chat.Snapshot += s => snapshot.TrySetResult(s);
    await chat.ConnectAsync(thread, CancellationToken.None);
    await snapshot.Task.WaitAsync(TimeSpan.FromSeconds(10));
    await chat.SendAsync("Besked fra Revit æøå", CancellationToken.None);
    await accepted.Task.WaitAsync(TimeSpan.FromSeconds(10));
    Check(!chat.CanSend, "do not repeat a send before transcript acknowledgment");
    chat.State["threadRuntimeStatus"]["type"] = "active";
    bool refused = false;
    try { await chat.SendAsync("must not send", CancellationToken.None); } catch (InvalidOperationException) { refused = true; }
    Check(refused, "active turn refused");
}
await serverTask;
Console.WriteLine("PASS fragmented IPC, same-thread history/send, settings preservation, active-turn conflict.");
