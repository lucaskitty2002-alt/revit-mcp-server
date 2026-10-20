using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;
using revit_mcp_plugin.Core;

namespace revit_mcp_plugin.UI
{
    public partial class MCPDockablePanel : Page
    {
        public static MCPDockablePanel Instance { get; private set; }
        private readonly DispatcherTimer _timer;
        private CodexSharedChat _chat;
        private bool _refreshing, _sending, _listing;
        private string _connectedThread;
        public sealed class ChatChoice
        {
            public string Id { get; set; }
            public string Label { get; set; }
        }
        public MCPDockablePanel()
        {
            InitializeComponent();
            Instance = this;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _timer.Tick += async (s, e) => await TickAsync();
        }
        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _timer.Start();
            if (ThreadPicker.ItemsSource == null) await ListChatsAsync();
        }
        private void Page_Unloaded(object sender, RoutedEventArgs e) { _timer.Stop(); ReleaseWindow(); }
        private void Page_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!(bool)e.NewValue) { _timer?.Stop(); ReleaseWindow(); }
            else if (IsLoaded) _timer?.Start();
        }
        private async Task TickAsync()
        {
            try { RevitStatus.Text = SocketService.Instance.IsRunning ? "Revit MCP er online." : "Aktivér Revit MCP Switch for modelværktøjer."; }
            catch { RevitStatus.Text = "Aktivér Revit MCP Switch for modelværktøjer."; }
            var chat = _chat;
            if (chat == null || _refreshing) return;
            _refreshing = true;
            try
            {
                if (chat.State != null && DateTime.UtcNow - chat.LastSnapshotUtc > TimeSpan.FromSeconds(15))
                    throw new TimeoutException("Chatten opdateres ikke længere. Åbn den i Codex-appen, og forbind igen.");
                await chat.RefreshAsync();
            }
            catch (Exception ex) { if (_chat == chat) Disconnect(ex.Message); }
            finally { _refreshing = false; }
        }
        private async Task ListChatsAsync()
        {
            if (_listing) return;
            _listing = true;
            RefreshButton.IsEnabled = false;
            try
            {
                var settings = CodexSettings.Load();
                // Listing does not resume/acquire any thread. The desktop owns execution.
                using (var server = new CodexAppServer())
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25)))
                {
                    await server.StartAsync(settings.FindExecutable(), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), timeout.Token);
                    var response = await server.RequestAsync("thread/list", new JObject { ["limit"] = 100, ["archived"] = false, ["sortKey"] = "updated_at" }, timeout.Token);
                    var choices = ((JArray)response["data"] ?? new JArray()).OfType<JObject>()
                        .Select(t => new ChatChoice { Id = (string)t["id"], Label = ((string)t["name"] ?? (string)t["preview"] ?? "Chat").Replace('\n', ' ').Trim() })
                        .Where(t => t.Id != null).ToList();
                    foreach (var choice in choices) if (choice.Label.Length > 110) choice.Label = choice.Label.Substring(0, 110) + "…";
                    ThreadPicker.ItemsSource = choices;
                    ThreadPicker.SelectedItem = choices.FirstOrDefault(t => t.Id == (_connectedThread ?? settings.SharedThreadId)) ?? choices.FirstOrDefault();
                    if (_chat == null) StatusText.Text = "Vælg chatten, der er åben i Codex-appen, og tryk Forbind chat.";
                }
            }
            catch (Exception ex) { StatusText.Text = "Kunne ikke hente chats: " + ex.Message; }
            finally { RefreshButton.IsEnabled = true; _listing = false; }
        }
        private async void Refresh_Click(object sender, RoutedEventArgs e) => await ListChatsAsync();
        private async void Connect_Click(object sender, RoutedEventArgs e)
        {
            if (!(ThreadPicker.SelectedItem is ChatChoice choice)) return;
            ReleaseWindow();
            ConnectButton.IsEnabled = false;
            var chat = new CodexSharedChat();
            _chat = chat;
            _connectedThread = choice.Id;
            chat.Snapshot += state => Dispatcher.BeginInvoke(new Action(() => { if (_chat == chat) Render(state); }));
            chat.Disconnected += ex => Dispatcher.BeginInvoke(new Action(() => { if (_chat == chat) Disconnect(ex.Message); }));
            try
            {
                StatusText.Text = "Forbinder til Codex-appens samtale…";
                await chat.ConnectAsync(choice.Id, CancellationToken.None);
                var settings = CodexSettings.Load(); settings.SharedThreadId = choice.Id; settings.Save();
                ThreadIdentity.Text = "Chat-id: " + choice.Id;
            }
            catch (Exception ex) { if (_chat == chat) Disconnect("Åbn den valgte chat i Codex-appen, og forbind igen. " + ex.Message); }
            finally { ConnectButton.IsEnabled = true; }
        }
        private void Render(JObject state)
        {
            string text = CodexChatTranscript.Render(state);
            if (History.Text != text)
            {
                bool atBottom = HistoryScroll.ScrollableHeight - HistoryScroll.VerticalOffset < 40;
                History.Text = text;
                if (atBottom) HistoryScroll.ScrollToEnd();
            }
            bool busy = CodexSharedChat.IsBusy(state);
            SendButton.IsEnabled = !_sending && _chat != null && _chat.CanSend;
            StatusText.Text = busy ? "Codex arbejder · svar vises begge steder. Se eventuelle godkendelser i appen." : "Forbundet til samme chat · opdateres hvert 2. sekund.";
        }
        private async Task SendAsync()
        {
            var chat = _chat;
            string text = Composer.Text;
            if (chat == null || _sending || string.IsNullOrWhiteSpace(text)) return;
            _sending = true; SendButton.IsEnabled = false;
            try
            {
                await chat.SendAsync(text, CancellationToken.None);
                if (Composer.Text == text) Composer.Clear();
                StatusText.Text = "Beskeden er modtaget af Codex-appen.";
                await chat.RefreshAsync();
            }
            catch (Exception ex) { StatusText.Text = ex.Message; }
            finally { _sending = false; SendButton.IsEnabled = _chat == chat && chat.CanSend; }
        }
        private async void Send_Click(object sender, RoutedEventArgs e) => await SendAsync();
        private async void Composer_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            { e.Handled = true; await SendAsync(); }
        }
        private void OpenCodex_Click(object sender, RoutedEventArgs e)
        {
            try { DesktopWindowLease.LaunchCodex(); }
            catch (Exception ex) { StatusText.Text = ex.Message; }
        }
        private void Disconnect(string reason) { ReleaseWindow(); StatusText.Text = reason; }
        // Retained entry point used by the Revit toggle/shutdown commands.
        public void ReleaseWindow() { _chat?.Dispose(); _chat = null; if (SendButton != null) SendButton.IsEnabled = false; }
        public void Shutdown() { _timer.Stop(); ReleaseWindow(); }
        public void LogCommand(string commandName, bool success, string message, double durationMs) { }
    }
}
