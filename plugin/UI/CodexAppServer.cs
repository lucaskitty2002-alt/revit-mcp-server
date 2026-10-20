using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace revit_mcp_plugin.UI
{
    // Newline-delimited app-server JSON-RPC. No credentials are read or copied by the plugin.
    public sealed class CodexAppServer : IDisposable
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource<JToken>> _pending = new ConcurrentDictionary<string, TaskCompletionSource<JToken>>();
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        private Process _process;
        private int _nextId;
        private bool _disposed;
        public event Action<string, JObject> Notification;
        public event Action<Exception> Disconnected;
        public Func<string, JObject, Task<JObject>> ServerRequest { get; set; }

        public async Task StartAsync(string executable, string directory, CancellationToken token)
        {
            _process = new Process { StartInfo = new ProcessStartInfo
            {
                FileName = executable, Arguments = "app-server --listen stdio://", WorkingDirectory = directory,
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                StandardInputEncoding = new UTF8Encoding(false)
            }};
            _process.Start();
            // Drain stderr without retaining diagnostics that could contain user data.
            _process.ErrorDataReceived += (s, e) => { };
            _process.BeginErrorReadLine();
            _ = Task.Run(ReadLoopAsync);
            await RequestAsync("initialize", new JObject
            {
                ["clientInfo"] = new JObject { ["name"] = "revit_codex", ["title"] = "Codex for Revit", ["version"] = "2.3.0" },
                ["capabilities"] = new JObject { ["experimentalApi"] = true }
            }, token).ConfigureAwait(false);
            await WriteAsync(new JObject { ["method"] = "initialized", ["params"] = new JObject() }).ConfigureAwait(false);
        }

        public async Task<JToken> RequestAsync(string method, JObject parameters, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string id = Interlocked.Increment(ref _nextId).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var completion = new TaskCompletionSource<JToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = completion;
            try
            {
                using (token.Register(() => completion.TrySetCanceled()))
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                using (timeout.Token.Register(() => completion.TrySetException(new TimeoutException("Codex did not respond to " + method))))
                {
                    await WriteAsync(new JObject { ["id"] = int.Parse(id), ["method"] = method, ["params"] = parameters }).ConfigureAwait(false);
                    return await completion.Task.ConfigureAwait(false);
                }
            }
            finally { _pending.TryRemove(id, out _); }
        }

        private async Task WriteAsync(JObject message)
        {
            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed || _process == null || _process.HasExited) throw new IOException("Codex connection is closed.");
                await _process.StandardInput.WriteLineAsync(message.ToString(Formatting.None)).ConfigureAwait(false);
                await _process.StandardInput.FlushAsync().ConfigureAwait(false);
            }
            finally { _writeLock.Release(); }
        }

        private async Task ReadLoopAsync()
        {
            Exception failure = new IOException("Codex disconnected. Send the message again to reconnect.");
            try
            {
                string line;
                while ((line = await _process.StandardOutput.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var message = JObject.Parse(line);
                    string method = (string)message["method"];
                    if (method != null)
                    {
                        if (message["id"] != null) _ = HandleServerRequestAsync(message);
                        else Notification?.Invoke(method, message["params"] as JObject ?? new JObject());
                    }
                    else if (_pending.TryGetValue(message["id"]?.ToString() ?? "", out var pending))
                    {
                        if (message["error"] != null) pending.TrySetException(new InvalidOperationException((string)message["error"]["message"] ?? "Codex request failed."));
                        else pending.TrySetResult(message["result"]);
                    }
                }
            }
            catch (Exception ex) { failure = ex; }
            foreach (var pending in _pending.Values) pending.TrySetException(failure);
            if (!_disposed) Disconnected?.Invoke(failure);
        }

        private async Task HandleServerRequestAsync(JObject message)
        {
            JObject response;
            try
            {
                if (ServerRequest == null) throw new NotSupportedException("This Codex request is not supported by the Revit panel.");
                var result = await ServerRequest((string)message["method"], message["params"] as JObject ?? new JObject()).ConfigureAwait(false);
                response = new JObject { ["id"] = message["id"], ["result"] = result };
            }
            catch (Exception ex)
            {
                response = new JObject { ["id"] = message["id"], ["error"] = new JObject { ["code"] = -32601, ["message"] = ex.Message } };
            }
            try { await WriteAsync(response).ConfigureAwait(false); }
            catch (Exception ex) { if (!_disposed) Disconnected?.Invoke(ex); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var pending in _pending.Values) pending.TrySetException(new IOException("Codex connection closed."));
            try { _process?.StandardInput.Close(); } catch { }
            try { if (_process != null && !_process.HasExited && !_process.WaitForExit(500)) _process.Kill(); } catch { }
            _process?.Dispose();
        }
    }
}
