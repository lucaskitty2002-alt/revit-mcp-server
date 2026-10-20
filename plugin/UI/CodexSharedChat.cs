using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace revit_mcp_plugin.UI
{
    // Experimental desktop/IDE coordination protocol, verified with desktop 26.1013.
    // The desktop remains the thread owner and executes tools/approvals. Never resume
    // the thread in a second app-server, copy credentials, or write session files.
    public sealed class CodexSharedChat : IDisposable
    {
        private readonly NamedPipeClientStream _pipe;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<JObject>> _pending = new ConcurrentDictionary<string, TaskCompletionSource<JObject>>();
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        private string _client = "initializing-client", _owner, _thread;
        private bool _disposed;
        private string _pendingMessageId;
        private readonly TaskCompletionSource<bool> _firstSnapshot = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        public JObject State { get; private set; }
        public DateTime LastSnapshotUtc { get; private set; }
        public bool CanSend => State != null && !IsBusy(State) && _pendingMessageId == null;
        public event Action<JObject> Snapshot;
        public event Action<Exception> Disconnected;

        public CodexSharedChat(string pipeName = "codex-ipc")
        {
            _pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous,
                System.Security.Principal.TokenImpersonationLevel.Identification);
        }

        public async Task ConnectAsync(string thread, CancellationToken token)
        {
            if (!Guid.TryParse(thread, out _)) throw new ArgumentException("Choose a Codex chat first.");
            _thread = thread;
            await _pipe.ConnectAsync(5000, token).ConfigureAwait(false);
            _ = Task.Run(ReadLoopAsync);
            var initialized = await RequestAsync("initialize", new JObject { ["clientType"] = "revit" }, 0, null, token).ConfigureAwait(false);
            _client = (string)initialized["result"]?["clientId"] ?? throw new IOException("Codex IPC initialization failed.");
            var owner = await RequestAsync("thread-owner-discovery", new JObject { ["hostId"] = "local", ["conversationId"] = thread }, 1, null, token).ConfigureAwait(false);
            _owner = (string)owner["handledByClientId"] ?? throw new IOException("Open this chat in the Codex desktop app first.");
            await RefreshAsync().ConfigureAwait(false);
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12)))
            using (token.Register(() => _firstSnapshot.TrySetCanceled()))
            using (timeout.Token.Register(() => _firstSnapshot.TrySetException(new TimeoutException("Codex did not provide shared history. Reconnect to the open desktop chat."))))
                await _firstSnapshot.Task.ConfigureAwait(false);
        }

        // Re-request a complete snapshot instead of guessing at version-specific
        // patches or accepted-text deltas. UI throttles this to once per two seconds.
        public Task RefreshAsync() => WriteAsync(new JObject
        {
            ["type"] = "broadcast", ["method"] = "thread-stream-following-changed", ["version"] = 1,
            ["sourceClientId"] = _client, ["targetClientIds"] = new JArray(_owner),
            ["params"] = new JObject { ["hostId"] = "local", ["conversationId"] = _thread, ["following"] = true }
        });

        public async Task SendAsync(string text, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (State == null) throw new InvalidOperationException("Wait for the shared chat to load.");
            if (!CanSend) throw new InvalidOperationException("Wait for the shared chat to confirm the previous message, or reconnect after checking the desktop app.");
            string messageId = Guid.NewGuid().ToString();
            _pendingMessageId = messageId;
            // Exactly one delivery attempt. A timeout has an unknown outcome: do not
            // auto-retry and risk executing a model operation twice.
            await RequestAsync("thread-follower-start-turn", new JObject
            {
                ["conversationId"] = _thread,
                ["turnStart"] = new JObject
                {
                    ["request"] = new JObject
                    {
                        ["threadId"] = _thread, ["turnTrigger"] = "composer",
                        ["clientUserMessageId"] = messageId,
                        ["input"] = new JArray(new JObject { ["type"] = "text", ["text"] = text, ["text_elements"] = new JArray() })
                    },
                    ["context"] = new JObject { ["inheritThreadSettings"] = true }
                }
            }, 2, _owner, token).ConfigureAwait(false);
        }

        public static bool IsBusy(JObject state) => (string)state?["threadRuntimeStatus"]?["type"] != "idle";

        private async Task<JObject> RequestAsync(string method, JObject parameters, int version, string target, CancellationToken token)
        {
            string id = Guid.NewGuid().ToString();
            var done = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = done;
            try
            {
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                using (token.Register(() => done.TrySetCanceled()))
                using (timeout.Token.Register(() => done.TrySetException(new TimeoutException("Codex did not confirm delivery. Check the desktop chat before retrying."))))
                {
                    var message = new JObject { ["type"] = "request", ["requestId"] = id, ["sourceClientId"] = _client,
                        ["version"] = version, ["method"] = method, ["params"] = parameters, ["timeoutMs"] = 25000 };
                    if (target != null) message["targetClientId"] = target;
                    await WriteAsync(message).ConfigureAwait(false);
                    var response = await done.Task.ConfigureAwait(false);
                    if ((string)response["resultType"] != "success") throw new IOException("Codex: " + (string)response["error"]);
                    return response;
                }
            }
            finally { _pending.TryRemove(id, out _); }
        }

        private async Task WriteAsync(JObject message)
        {
            byte[] body = Encoding.UTF8.GetBytes(message.ToString(Formatting.None));
            byte[] header = BitConverter.GetBytes(body.Length);
            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed) throw new ObjectDisposedException(nameof(CodexSharedChat));
                await _pipe.WriteAsync(header, 0, 4).ConfigureAwait(false);
                await _pipe.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
                await _pipe.FlushAsync().ConfigureAwait(false);
            }
            finally { _writeLock.Release(); }
        }

        private async Task ReadExactAsync(byte[] bytes)
        {
            int offset = 0;
            while (offset < bytes.Length)
            {
                int count = await _pipe.ReadAsync(bytes, offset, bytes.Length - offset).ConfigureAwait(false);
                if (count == 0) throw new EndOfStreamException("Codex disconnected. Reconnect to the chat.");
                offset += count;
            }
        }

        private async Task ReadLoopAsync()
        {
            try
            {
                while (!_disposed)
                {
                    var header = new byte[4];
                    await ReadExactAsync(header).ConfigureAwait(false);
                    int length = BitConverter.ToInt32(header, 0);
                    if (length <= 0 || length > 268435456) throw new IOException("Unsupported Codex frame size.");
                    var bytes = new byte[length];
                    await ReadExactAsync(bytes).ConfigureAwait(false);
                    var message = JObject.Parse(Encoding.UTF8.GetString(bytes));
                    string type = (string)message["type"];
                    if (type == "response" && _pending.TryGetValue((string)message["requestId"] ?? "", out var done)) done.TrySetResult(message);
                    else if (type == "client-discovery-request") await WriteAsync(new JObject
                    { ["type"] = "client-discovery-response", ["requestId"] = message["requestId"], ["response"] = new JObject { ["canHandle"] = false } }).ConfigureAwait(false);
                    else if (type == "broadcast" && (string)message["method"] == "client-status-changed"
                        && (string)message["params"]?["clientId"] == _owner && (string)message["params"]?["status"] == "disconnected")
                        throw new IOException("The Codex chat owner disconnected. Open the chat in the app and reconnect.");
                    else if (type == "broadcast" && (string)message["method"] == "thread-stream-state-changed"
                        && (string)message["sourceClientId"] == _owner
                        && (string)message["params"]?["hostId"] == "local"
                        && (string)message["params"]?["conversationId"] == _thread)
                    {
                        if ((int?)message["version"] != 11) throw new IOException("Codex chat protocol changed. Update the Revit integration.");
                        var change = message["params"]?["change"];
                        if ((string)change?["type"] != "snapshot") continue;
                        var state = change["conversationState"] as JObject;
                        if ((string)state?["id"] != _thread) throw new IOException("Codex returned a different chat.");
                        State = state;
                        LastSnapshotUtc = DateTime.UtcNow;
                        if (_pendingMessageId != null)
                            foreach (var turn in CodexChatTranscript.Turns(state))
                                if ((string)turn["params"]?["clientUserMessageId"] == _pendingMessageId) { _pendingMessageId = null; break; }
                        Snapshot?.Invoke(state);
                        _firstSnapshot.TrySetResult(true);
                    }
                }
            }
            catch (Exception ex)
            {
                foreach (var done in _pending.Values) done.TrySetException(ex);
                _firstSnapshot.TrySetException(ex);
                if (!_disposed) Disconnected?.Invoke(ex);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _pipe.Dispose(); // Router removes this follower; never stops the owner/app.
            _firstSnapshot.TrySetCanceled();
            foreach (var done in _pending.Values) done.TrySetException(new IOException("Chat disconnected."));
        }
    }
}
