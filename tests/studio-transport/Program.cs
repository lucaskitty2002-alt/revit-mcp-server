using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using revit_mcp_plugin.Core;

int checks = 0;
int failures = 0;
void Check(bool condition, string detail)
{
    if (!condition) throw new Exception(detail);
    checks++;
}
async Task Until(Func<bool> predicate)
{
    var clock = Stopwatch.StartNew();
    while (!predicate() && clock.Elapsed < TimeSpan.FromSeconds(4)) await Task.Delay(10);
    Check(predicate(), "Timed out waiting for loopback state.");
}
(CommandSocketSession session, int port, TcpListener listener) Start(Func<string, Func<bool>, string> processor, int port = 0)
{
    var listener = new TcpListener(IPAddress.Loopback, port);
    listener.Start();
    var session = new CommandSocketSession(listener, processor, ex => Interlocked.Increment(ref failures));
    session.Start();
    return (session, ((IPEndPoint)listener.LocalEndpoint).Port, listener);
}
async Task<bool> Disconnected(TcpClient client)
{
    try
    {
        using var reader = new StreamReader(client.GetStream(), Encoding.UTF8, false, 4096, true);
        return await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(4)) == null;
    }
    catch (IOException) { return true; }
    catch (SocketException) { return true; }
}

// Stop must close an idle client blocked in ReadLine, not just the listening socket.
int idleCalls = 0;
var idle = Start((line, allowed) => { Interlocked.Increment(ref idleCalls); return line; });
using (idle.session)
using (var client = new TcpClient())
{
    await client.ConnectAsync(IPAddress.Loopback, idle.port);
    await Until(() => idle.session.ActiveClientCount == 1);
    idle.session.Dispose();
    Check(!idle.session.IsRunning && idle.session.ActiveClientCount == 0, "Stop must invalidate and close all tracked clients.");
    Check(await Disconnected(client), "Idle client must be disconnected by Stop.");
    Check(idleCalls == 0, "Stopping an idle listener must not dispatch a command.");
}

// Two lines arrive in one write. Stop during first command must discard the buffered second line,
// and restarting on the same port must not revive the old client or its buffered commands.
using var firstEntered = new ManualResetEventSlim();
using var releaseFirst = new ManualResetEventSlim();
int oldCalls = 0;
var old = Start((line, allowed) =>
{
    if (!allowed()) return "stopped";
    Interlocked.Increment(ref oldCalls);
    firstEntered.Set();
    if (!releaseFirst.Wait(TimeSpan.FromSeconds(8))) throw new TimeoutException();
    return "first completed";
});
using (old.session)
using (var client = new TcpClient())
{
    try
    {
        await client.ConnectAsync(IPAddress.Loopback, old.port);
        byte[] input = Encoding.UTF8.GetBytes("first\nsecond\n");
        await client.GetStream().WriteAsync(input);
        await Until(() => firstEntered.IsSet);
        old.session.Dispose();
        Check(!old.session.IsRunning, "Old listener must stay stopped.");
        var next = Start((line, allowed) => allowed() ? "new:" + line : "stopped", old.port);
        using (next.session)
        using (var replacement = new TcpClient())
        {
            releaseFirst.Set();
            Check(await Disconnected(client), "Stop must close the old client while the accepted command finishes.");
            await replacement.ConnectAsync(IPAddress.Loopback, next.port);
            await replacement.GetStream().WriteAsync(Encoding.UTF8.GetBytes("fresh\n"));
            using var reader = new StreamReader(replacement.GetStream());
            Check(await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(4)) == "new:fresh", "New generation must accept a fresh connection.");
            Check(oldCalls == 1, "An old buffered request must never execute after restart.");
        }
    }
    finally { releaseFirst.Set(); }
}

// Stop can also occur during request parsing, before command admission.
using var parsing = new ManualResetEventSlim();
using var resumeParsing = new ManualResetEventSlim();
using var parseFinished = new ManualResetEventSlim();
int executed = 0;
var parsed = Start((line, allowed) =>
{
    parsing.Set();
    if (!resumeParsing.Wait(TimeSpan.FromSeconds(8))) throw new TimeoutException();
    if (allowed()) Interlocked.Increment(ref executed);
    parseFinished.Set();
    return "done";
});
using (parsed.session)
using (var client = new TcpClient())
{
    try
    {
        await client.ConnectAsync(IPAddress.Loopback, parsed.port);
        await client.GetStream().WriteAsync(Encoding.UTF8.GetBytes("parse first\n"));
        await Until(() => parsing.IsSet);
        parsed.session.Dispose();
        resumeParsing.Set();
        await Until(() => parseFinished.IsSet);
        Check(executed == 0, "A command not admitted before Stop must be rejected.");
    }
    finally { resumeParsing.Set(); }
}

// UTF-8 characters can span network reads; line framing must preserve them.
const string unicode = "måling · højder · α";
var fragmented = Start((line, allowed) => line);
using (fragmented.session)
using (var client = new TcpClient())
{
    await client.ConnectAsync(IPAddress.Loopback, fragmented.port);
    byte[] bytes = Encoding.UTF8.GetBytes(unicode + "\n");
    foreach (byte value in bytes) await client.GetStream().WriteAsync(new[] { value });
    using var reader = new StreamReader(client.GetStream());
    Check(await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(4)) == unicode, "Fragmented UTF-8 must survive framing.");
}
Check(failures == 0, "Normal stop/disconnect must not be reported as listener failure.");

// Unexpected accept failure must invalidate the generation and close its clients as well.
var broken = Start((line, allowed) => line);
using (broken.session)
using (var client = new TcpClient())
{
    await client.ConnectAsync(IPAddress.Loopback, broken.port);
    await Until(() => broken.session.ActiveClientCount == 1);
    broken.listener.Stop();
    await Until(() => !broken.session.IsRunning && Volatile.Read(ref failures) == 1);
    Check(broken.session.ActiveClientCount == 0, "Listener failure must close tracked clients.");
    Check(await Disconnected(client), "Listener failure must release clients blocked in ReadLine.");
}
Console.WriteLine($"VUE Studio loopback transport: {checks} checks passed.");
