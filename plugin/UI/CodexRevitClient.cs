using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace revit_mcp_plugin.UI
{
    public sealed class CodexRevitClient : IDisposable
    {
        private readonly string _pluginDirectory;
        private readonly Func<int> _port;
        private readonly CodexSettings _settings;
        private readonly string _workspace;
        private readonly SemaphoreSlim _toolLock = new SemaphoreSlim(1, 1);
        private readonly HashSet<string> _toolNames = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> _messageOrder = new List<string>();
        private readonly Dictionary<string, string> _messages = new Dictionary<string, string>();
        private CodexAppServer _server;
        private string _threadId;
        private string _turnId;
        private TaskCompletionSource<string> _completion;
        private CancellationTokenSource _turnCancellation;
        private bool _busy;
        public event Action<string> ToolExecuting;
        public event Action<string, bool, string> ToolCompleted;
        public event Action<string> Progress;
        public event Action<string> ReplyUpdated;
        public Func<string, Task<bool>> ConfirmAction { get; set; }

        private const string Instructions = @"You are Codex, integrated into Autodesk Revit through this chat panel.
Use the supplied dynamic Revit tools to inspect and edit the active model. Discover model content before modifying it.
Use get_selected_elements for references to the selection. Use BuiltInCategory names where possible; tool coordinates are millimeters.
Execute the user's requested Revit operations, then briefly report actual results. Never claim success after a tool error.
Use tools only for the user's request. Ask for clarification in your chat reply when needed. Do not use shell commands, files, other MCP servers, or external services to operate Revit.
Reply in the user's language. Do not expose hidden reasoning. Report concise progress and results.";

        public CodexRevitClient(string pluginDirectory, Func<int> port, CodexSettings settings = null, string workspace = null)
        {
            _pluginDirectory = pluginDirectory;
            _port = port;
            _settings = settings;
            _workspace = workspace ?? CodexSettings.Workspace;
        }

        private JArray LoadTools()
        {
            var schemas = JArray.Parse(File.ReadAllText(Path.Combine(_pluginDirectory, "tool_schemas.json")));
            if (schemas.Count == 0) throw new InvalidOperationException("Revit tool schemas are missing. Reinstall the plugin.");
            _toolNames.Clear();
            return new JArray(schemas.Select(schema =>
            {
                string name = (string)schema["name"];
                if (string.IsNullOrWhiteSpace(name) || schema["input_schema"] == null) throw new InvalidOperationException("Invalid Revit tool schema.");
                _toolNames.Add(name);
                return new JObject { ["type"] = "function", ["name"] = name, ["description"] = schema["description"] ?? name, ["inputSchema"] = schema["input_schema"].DeepClone() };
            }));
        }

        private async Task EnsureConnectedAsync(CancellationToken token)
        {
            if (_server != null && _threadId != null) return;
            _server?.Dispose();
            _server = new CodexAppServer();
            _server.Notification += OnNotification;
            _server.Disconnected += ex => _completion?.TrySetException(ex);
            _server.ServerRequest = HandleRequestAsync;
            var settings = _settings ?? CodexSettings.Load();
            Directory.CreateDirectory(_workspace);
            await _server.StartAsync(settings.FindExecutable(), _workspace, token).ConfigureAwait(false);
            var account = await _server.RequestAsync("account/read", new JObject(), token).ConfigureAwait(false);
            if (account["account"] == null || account["account"].Type == JTokenType.Null)
                throw new InvalidOperationException("Sign in to Codex first. Open Settings > Codex > Sign in, then try again.");

            // The panel provides its own Revit tool bridge. Avoid starting unrelated user MCP servers.
            var configuration = await _server.RequestAsync("config/read", new JObject { ["includeLayers"] = false }, token).ConfigureAwait(false);
            var disabledServers = new JObject();
            if (configuration["config"]?["mcp_servers"] is JObject servers)
                foreach (var server in servers.Properties()) disabledServers[server.Name] = new JObject { ["enabled"] = false };
            var parameters = new JObject
            {
                ["cwd"] = _workspace, ["sandbox"] = "read-only", ["approvalPolicy"] = "on-request",
                ["approvalsReviewer"] = "user", ["ephemeral"] = true,
                ["baseInstructions"] = Instructions, ["dynamicTools"] = LoadTools(),
                ["config"] = new JObject { ["mcp_servers"] = disabledServers }
            };
            if (!string.IsNullOrWhiteSpace(settings.Model)) parameters["model"] = settings.Model;
            var response = await _server.RequestAsync("thread/start", parameters, token).ConfigureAwait(false);
            _threadId = (string)response["thread"]?["id"] ?? throw new InvalidOperationException("Codex did not create a conversation.");
        }

        public async Task<string> SendMessage(string text)
        {
            if (_busy) throw new InvalidOperationException("Wait for the current reply or press Stop.");
            _busy = true;
            _turnCancellation = new CancellationTokenSource();
            lock (_messages) { _messages.Clear(); _messageOrder.Clear(); }
            _completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                Progress?.Invoke("Connecting to Codex...");
                await EnsureConnectedAsync(_turnCancellation.Token).ConfigureAwait(false);
                Progress?.Invoke("Codex is working...");
                var result = await _server.RequestAsync("turn/start", new JObject
                {
                    ["threadId"] = _threadId,
                    ["input"] = new JArray(new JObject { ["type"] = "text", ["text"] = text })
                }, _turnCancellation.Token).ConfigureAwait(false);
                _turnId = (string)result["turn"]?["id"];
                // Stop can be pressed while turn/start is still in flight.
                if (_turnCancellation.IsCancellationRequested) await InterruptAsync().ConfigureAwait(false);
                return await _completion.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { ResetConnection(); return "Operation cancelled. Changes already completed in Revit remain in the model."; }
            catch (Exception ex) { ResetConnection(); return "Codex connection error: " + ex.Message; }
            finally { _busy = false; _turnId = null; _turnCancellation.Dispose(); _turnCancellation = null; }
        }

        private void OnNotification(string method, JObject parameters)
        {
            if ((string)parameters["threadId"] != _threadId) return;
            if (method == "turn/started") _turnId = (string)parameters["turn"]?["id"];
            if (method == "item/agentMessage/delta" || (method == "item/completed" && (string)parameters["item"]?["type"] == "agentMessage"))
            {
                string id = (string)parameters["itemId"] ?? (string)parameters["item"]?["id"];
                if (id != null)
                {
                    string reply;
                    lock (_messages)
                    {
                        if (!_messages.ContainsKey(id)) { _messageOrder.Add(id); _messages[id] = ""; }
                        if (method == "item/agentMessage/delta") _messages[id] += (string)parameters["delta"];
                        else _messages[id] = (string)parameters["item"]?["text"] ?? _messages[id];
                        reply = string.Join("\n\n", _messageOrder.Select(key => _messages[key]));
                    }
                    ReplyUpdated?.Invoke(reply);
                }
            }
            if (method == "turn/completed")
            {
                string status = (string)parameters["turn"]?["status"];
                string text;
                lock (_messages) text = string.Join("\n\n", _messageOrder.Select(key => _messages[key]));
                if (status == "completed") _completion?.TrySetResult(string.IsNullOrWhiteSpace(text) ? "Codex finished without a text reply." : text);
                else if (status == "interrupted") _completion?.TrySetResult(text + "\nOperation cancelled. Changes already completed in Revit remain in the model.");
                else _completion?.TrySetException(new InvalidOperationException((string)parameters["turn"]?["error"]?["message"] ?? "Codex turn failed."));
            }
        }

        private async Task<JObject> HandleRequestAsync(string method, JObject parameters)
        {
            if ((string)parameters["threadId"] != _threadId) throw new InvalidOperationException("Request belongs to another conversation.");
            if (method == "item/tool/call")
            {
                string name = (string)parameters["tool"];
                bool success = false;
                string text;
                try
                {
                    if (!_busy || _turnCancellation == null) throw new InvalidOperationException("No active Revit request.");
                    if (_turnId != null && (string)parameters["turnId"] != _turnId) throw new InvalidOperationException("Tool request belongs to an inactive turn.");
                    _turnCancellation.Token.ThrowIfCancellationRequested();
                    if (!_toolNames.Contains(name ?? "")) throw new InvalidOperationException("Unknown Revit tool: " + name);
                    if (!(parameters["arguments"] is JObject arguments)) throw new InvalidOperationException("Revit tool arguments must be an object.");
                    ToolExecuting?.Invoke(name);
                    var token = _turnCancellation.Token;
                    await _toolLock.WaitAsync(token).ConfigureAwait(false);
                    JObject result;
                    try { result = await ExecuteRevitAsync(name, arguments, token).ConfigureAwait(false); }
                    finally { _toolLock.Release(); }
                    success = result["error"] == null && (bool?)(result["result"] as JObject)?["success"] != false;
                    text = (result["error"] ?? result["result"] ?? result).ToString(Formatting.Indented);
                }
                catch (Exception ex) { text = "Revit tool failed: " + ex.Message; }
                ToolCompleted?.Invoke(name, !success, text);
                return new JObject { ["success"] = success, ["contentItems"] = new JArray(new JObject { ["type"] = "inputText", ["text"] = text }) };
            }
            if (method == "item/commandExecution/requestApproval" || method == "item/fileChange/requestApproval")
            {
                string description = parameters["command"]?.ToString() ?? parameters["reason"]?.ToString() ?? "Codex requests permission to change local files.";
                bool approved = ConfirmAction != null && await ConfirmAction(description).ConfigureAwait(false);
                return new JObject { ["decision"] = approved ? "accept" : "decline" };
            }
            if (method == "item/tool/requestUserInput")
            {
                var answers = new JObject();
                foreach (var question in parameters["questions"] as JArray ?? new JArray())
                    answers[(string)question["id"]] = new JObject { ["answers"] = new JArray("Ask this question in your chat reply so the user can answer in Revit.") };
                return new JObject { ["answers"] = answers };
            }
            throw new NotSupportedException("Unsupported Codex request: " + method + ". Ask the user in the chat instead.");
        }

        private async Task<JObject> ExecuteRevitAsync(string name, JObject arguments, CancellationToken token)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var socket = new TcpClient())
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(120));
                using (timeout.Token.Register(() => socket.Close()))
                {
                    int port = _port();
                    if (port <= 0) throw new InvalidOperationException("Enable Revit MCP Switch first.");
                    await socket.ConnectAsync("127.0.0.1", port).ConfigureAwait(false);
                    using (var stream = socket.GetStream())
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
                    using (var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true))
                    {
                        string id = Guid.NewGuid().ToString();
                        await writer.WriteLineAsync(new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = name, ["params"] = arguments }.ToString(Formatting.None)).ConfigureAwait(false);
                        await writer.FlushAsync().ConfigureAwait(false);
                        string line = await reader.ReadLineAsync().ConfigureAwait(false);
                        if (line == null) throw new IOException("Revit closed the connection without a result.");
                        var result = JObject.Parse(line);
                        if ((string)result["id"] != id) throw new IOException("Revit response identifier does not match the request.");
                        return result;
                    }
                }
            }
        }

        public async void Cancel()
        {
            var server = _server;
            _turnCancellation?.Cancel();
            try { await InterruptAsync().ConfigureAwait(false); }
            catch { if (ReferenceEquals(server, _server)) ResetConnection(); }
        }

        private async Task InterruptAsync()
        {
            var server = _server;
            var threadId = _threadId;
            var turnId = _turnId;
            var completion = _completion;
            if (server != null && threadId != null && turnId != null)
            {
                await server.RequestAsync("turn/interrupt", new JObject { ["threadId"] = threadId, ["turnId"] = turnId }, CancellationToken.None).ConfigureAwait(false);
                // Do not leave the UI waiting indefinitely for a lost completion event.
                if (completion != null && await Task.WhenAny(completion.Task, Task.Delay(5000)).ConfigureAwait(false) != completion.Task)
                    completion.TrySetCanceled();
            }
        }

        public void ClearHistory()
        {
            if (_busy) throw new InvalidOperationException("Stop the current reply before starting a new chat.");
            ResetConnection();
        }
        private void ResetConnection() { _server?.Dispose(); _server = null; _threadId = null; }
        public void Dispose() { _turnCancellation?.Cancel(); _completion?.TrySetCanceled(); ResetConnection(); }
    }
}
