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
await CheckRefreshPolicyAsync();

static async Task CheckRefreshPolicyAsync()
{
    string name = "vue-refresh-test-" + Guid.NewGuid();
    string thread = Guid.NewGuid().ToString();
    using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
    using var writerLock = new SemaphoreSlim(1, 1);
    long now = DateTime.UtcNow.Ticks;
    int follows = 0, sends = 0;
    bool suppressSnapshot = false;
    string messageId = null;
    var state = new JObject { ["id"] = thread, ["threadRuntimeStatus"] = new JObject { ["type"] = "idle" }, ["turns"] = new JArray() };
    async Task Publish(JObject message)
    {
        await writerLock.WaitAsync();
        try { await Write(server, message); }
        finally { writerLock.Release(); }
    }
    Task PublishChange(JObject change) => Publish(new JObject {
        ["type"] = "broadcast", ["method"] = "thread-stream-state-changed", ["version"] = 11,
        ["sourceClientId"] = "owner", ["params"] = new JObject { ["hostId"] = "local", ["conversationId"] = thread, ["change"] = change }
    });
    Task PublishSnapshot() => PublishChange(new JObject { ["type"] = "snapshot", ["conversationState"] = state.DeepClone() });
    var serve = Task.Run(async () => {
        await server.WaitForConnectionAsync();
        try
        {
            while (true)
            {
                var m = await Read(server);
                switch ((string)m["method"])
                {
                    case "initialize":
                    case "thread-owner-discovery":
                        await Publish(new JObject { ["type"] = "response", ["requestId"] = m["requestId"], ["resultType"] = "success", ["handledByClientId"] = "owner", ["result"] = new JObject { ["clientId"] = "follower" } });
                        break;
                    case "thread-stream-following-changed":
                        Interlocked.Increment(ref follows);
                        if (!suppressSnapshot) await PublishSnapshot();
                        break;
                    case "thread-follower-start-turn":
                        messageId = (string)m["params"]?["turnStart"]?["request"]?["clientUserMessageId"];
                        Interlocked.Increment(ref sends);
                        await Publish(new JObject { ["type"] = "response", ["requestId"] = m["requestId"], ["resultType"] = "success", ["result"] = new JObject() });
                        break;
                    default: throw new Exception("Unexpected request; refreshing must never start/resume a model thread.");
                }
            }
        }
        catch (EndOfStreamException) { }
        catch (IOException) { }
    });
    using var chat = new CodexSharedChat(name, () => new DateTime(Interlocked.Read(ref now), DateTimeKind.Utc));
    int snapshots = 0;
    chat.Snapshot += _ => Interlocked.Increment(ref snapshots);
    await chat.ConnectAsync(thread, CancellationToken.None);
    Check(follows == 1, "initial history requested once");
    await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => chat.RefreshAsync()));
    await Task.Delay(60);
    Check(follows == 1, "idle snapshot is not repeatedly transferred");

    Interlocked.Add(ref now, TimeSpan.FromSeconds(31).Ticks);
    await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => chat.RefreshAsync()));
    await Until(() => snapshots == 2);
    Check(follows == 2 && sends == 0, "concurrent refresh coalesces into one idle heartbeat with no inference");
    Interlocked.Add(ref now, TimeSpan.FromSeconds(20).Ticks);
    Check(!chat.IsSnapshotOverdue, "old idle snapshot is not a false disconnection");

    suppressSnapshot = true;
    Interlocked.Add(ref now, TimeSpan.FromSeconds(11).Ticks);
    await chat.RefreshAsync();
    await Until(() => follows == 3);
    Interlocked.Add(ref now, TimeSpan.FromSeconds(16).Ticks);
    await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => chat.RefreshAsync()));
    Check(chat.IsSnapshotOverdue && follows == 3, "unanswered refresh is detectable without flooding the desktop");
    suppressSnapshot = false;
    await PublishSnapshot();
    await Until(() => snapshots == 3);
    Check(!chat.IsSnapshotOverdue, "snapshot clears outstanding heartbeat");

    await PublishChange(new JObject { ["type"] = "patch" });
    await Task.Delay(60);
    Interlocked.Add(ref now, TimeSpan.FromSeconds(2).Ticks);
    await chat.RefreshAsync();
    await Until(() => snapshots == 4);
    Check(follows == 4, "desktop delta switches idle client to active refresh cadence");

    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    bool cancelledSend = false;
    try { await chat.SendAsync("cancelled", cancelled.Token); } catch (OperationCanceledException) { cancelledSend = true; }
    Check(cancelledSend && chat.CanSend && sends == 0, "pre-cancelled send never consumes a turn or blocks the composer");
    var first = chat.SendAsync("one", CancellationToken.None);
    bool duplicateRefused = false;
    try { await chat.SendAsync("two", CancellationToken.None); } catch (InvalidOperationException) { duplicateRefused = true; }
    await first;
    Check(duplicateRefused && sends == 1 && !chat.CanSend, "concurrent sends reserve one delivery until transcript acknowledgment");
    state["turns"] = new JArray(new JObject { ["params"] = new JObject { ["clientUserMessageId"] = messageId } });
    await PublishSnapshot();
    await Until(() => chat.CanSend);
    Check(sends == 1, "history acknowledgment never replays a model operation");
    chat.Dispose();
    await serve.WaitAsync(TimeSpan.FromSeconds(5));
    Console.WriteLine("PASS adaptive/coalesced local history reads, liveness, cancellation, exactly-once delivery.");
}

static async Task Until(Func<bool> test)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    while (!test()) await Task.Delay(10, timeout.Token);
}
