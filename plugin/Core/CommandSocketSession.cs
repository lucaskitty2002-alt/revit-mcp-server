using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace revit_mcp_plugin.Core
{
    /// <summary>
    /// One listener generation. Closing it invalidates every accepted client permanently, including
    /// buffered requests. No lifecycle lock is held while a Revit command is executing.
    /// </summary>
    internal sealed class CommandSocketSession : IDisposable
    {
        private readonly object _gate = new object();
        private readonly TcpListener _listener;
        private readonly HashSet<TcpClient> _clients = new HashSet<TcpClient>();
        private readonly Func<string, Func<bool>, string> _processRequest;
        private readonly Action<Exception> _onFailure;
        private readonly Action<Exception> _onClientError;
        private volatile bool _running;
        private bool _started;
        private bool _disposed;

        public bool IsRunning => _running;
        internal int ActiveClientCount { get { lock (_gate) return _clients.Count; } }

        public CommandSocketSession(TcpListener listener,
            Func<string, Func<bool>, string> processRequest, Action<Exception> onFailure,
            Action<Exception> onClientError = null)
        {
            _listener = listener ?? throw new ArgumentNullException(nameof(listener));
            _processRequest = processRequest ?? throw new ArgumentNullException(nameof(processRequest));
            _onFailure = onFailure;
            _onClientError = onClientError;
        }

        public void Start()
        {
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(CommandSocketSession));
                if (_started) throw new InvalidOperationException("A listener session can only start once.");
                _started = true;
                _running = true;
            }
            try { new Thread(Listen) { IsBackground = true, Name = "VUE Studio MCP listener" }.Start(); }
            catch { Dispose(); throw; }
        }

        private bool CanDispatch()
        {
            lock (_gate) return _running;
        }

        private void Listen()
        {
            try
            {
                while (_running)
                {
                    TcpClient client = _listener.AcceptTcpClient();
                    lock (_gate)
                    {
                        if (!_running) { client.Close(); return; }
                        _clients.Add(client);
                    }
                    new Thread(() => HandleClient(client)) { IsBackground = true,
                        Name = "VUE Studio MCP client" }.Start();
                }
            }
            catch (Exception ex)
            {
                bool unexpected = _running;
                Dispose();
                if (unexpected) Notify(_onFailure, ex);
            }
        }

        private void HandleClient(TcpClient client)
        {
            try
            {
                using (NetworkStream stream = client.GetStream())
                using (var reader = new StreamReader(stream, new UTF8Encoding(false), false, 4096, true))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true })
                {
                    while (_running)
                    {
                        string line = reader.ReadLine();
                        // Stop closes blocked reads and also rejects lines already buffered by StreamReader.
                        if (line == null || !CanDispatch()) return;
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        string response = _processRequest(line.Trim(), CanDispatch);
                        // A request admitted before Stop may finish; a stopped session sends no new response.
                        if (!CanDispatch()) return;
                        writer.WriteLine(response);
                    }
                }
            }
            catch (IOException) { /* Disconnect/Stop closes blocked reads and writes. */ }
            catch (ObjectDisposedException) { /* Expected when Stop closes a tracked client. */ }
            catch (SocketException) { /* Peer disconnect. */ }
            catch (Exception ex) { Notify(_onClientError, ex); }
            finally
            {
                lock (_gate) _clients.Remove(client);
                client.Close();
            }
        }

        private static void Notify(Action<Exception> callback, Exception error)
        {
            try { callback?.Invoke(error); } catch { /* Logging must not terminate a background thread. */ }
        }

        public void Dispose()
        {
            TcpClient[] clients;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _running = false; // Invalidate this generation before releasing any socket.
                clients = new TcpClient[_clients.Count];
                _clients.CopyTo(clients);
                _clients.Clear();
            }
            try { _listener.Stop(); } catch (SocketException) { }
            foreach (TcpClient client in clients)
                try { client.Close(); } catch (SocketException) { }
        }
    }
}
