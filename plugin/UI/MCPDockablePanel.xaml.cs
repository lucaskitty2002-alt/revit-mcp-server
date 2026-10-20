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
        private string _connectionState;
        private CancellationTokenSource _visibleWork = new CancellationTokenSource();
        private int _generation;
        public sealed class ChatChoice
        {
            public string Id { get; set; }
            public string Label { get; set; }
        }
        public MCPDockablePanel()
        {
            InitializeComponent();
            Instance = this;
            WorkflowList.ItemsSource = WorkflowStarter.All;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += async (s, e) => await TickAsync();
        }
        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (_visibleWork.IsCancellationRequested) _visibleWork = new CancellationTokenSource();
            UpdateConnection();
            _timer.Start();
            if (TextTab.IsSelected && ThreadPicker.ItemsSource == null) await ListChatsAsync();
        }
        private void Page_Unloaded(object sender, RoutedEventArgs e) { _timer.Stop(); _visibleWork.Cancel(); ++_generation; SafeReleaseWindow(); }
        private void Page_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!(bool)e.NewValue) { _timer?.Stop(); _visibleWork.Cancel(); ++_generation; SafeReleaseWindow(); }
            else if (IsLoaded) { if (_visibleWork.IsCancellationRequested) _visibleWork = new CancellationTokenSource(); _timer?.Start(); }
        }
        private void UpdateConnection()
        {
            var service = SocketService.Instance;
            var state = ConnectionPresentation.From(service.IsRunning, service.LastError, service.Port);
            if (_connectionState == state.StateKey) return;
            _connectionState = state.StateKey;
            PowerLabel.Text = state.Label;
            PowerDot.Fill = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(state.HasError ? "#FDAA97" : state.IsOn ? "#9DDFB3" : "#A1ADA8"));
            PowerButton.ToolTip = state.Detail + "\n" + state.Action;
            RevitStatus.Text = state.Detail;
        }
        private void Power_Click(object sender, RoutedEventArgs e)
        {
            if (!Core.Application.RequestConnectionToggle())
                RevitStatus.Text = "Forbindelsen kan ikke skiftes nu. Brug VUE Studio-fanen i Revit, og prøv igen.";
        }
        private async void WorkspaceTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source != WorkspaceTabs || !IsLoaded || !IsVisible) return;
            if (TextTab.IsSelected && ThreadPicker.ItemsSource == null) await ListChatsAsync();
        }
        private void Workflow_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || !(button.Tag is string prompt)) return;
            if (!string.IsNullOrWhiteSpace(Composer.Text))
            {
                // Keep the user's unfinished draft; put the selected starter after it.
                Composer.AppendText("\n\n" + prompt);
            }
            else Composer.Text = prompt;
            WorkspaceTabs.SelectedItem = TextTab;
            Composer.Focus();
            Composer.CaretIndex = Composer.Text.Length;
        }
        private async Task TickAsync()
        {
            UpdateConnection();
            var chat = _chat;
            if (!TextTab.IsSelected || !IsVisible || chat == null || _refreshing) return;
            _refreshing = true;
            try
            {
                if (chat.IsSnapshotOverdue)
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
            int generation = _generation;
            RefreshButton.IsEnabled = false;
            try
            {
                var settings = CodexSettings.Load();
                // Listing does not resume/acquire any thread. The desktop owns execution.
                using (var server = new CodexAppServer())
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25)))
                {
                    using (var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, _visibleWork.Token))
                    {
                    await server.StartAsync(settings.FindExecutable(), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), linked.Token);
                    var response = await server.RequestAsync("thread/list", new JObject { ["limit"] = 100, ["archived"] = false, ["sortKey"] = "updated_at" }, linked.Token);
                    if (generation != _generation || !IsVisible) return;
                    var choices = ((JArray)response["data"] ?? new JArray()).OfType<JObject>()
                        .Select(t => new ChatChoice { Id = (string)t["id"], Label = ((string)t["name"] ?? (string)t["preview"] ?? "Chat").Replace('\n', ' ').Trim() })
                        .Where(t => t.Id != null).ToList();
                    foreach (var choice in choices) if (choice.Label.Length > 110) choice.Label = choice.Label.Substring(0, 110) + "…";
                    ThreadPicker.ItemsSource = choices;
                    ThreadPicker.SelectedItem = choices.FirstOrDefault(t => t.Id == (_connectedThread ?? settings.SharedThreadId)) ?? choices.FirstOrDefault();
                    if (_chat == null) StatusText.Text = "Vælg chatten, der er åben i Codex-appen, og tryk Forbind chat.";
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (generation == _generation) StatusText.Text = "Kunne ikke hente chats: " + ex.Message; }
            finally { RefreshButton.IsEnabled = true; _listing = false; }
        }
        private async void Refresh_Click(object sender, RoutedEventArgs e) => await ListChatsAsync();
        private async void Connect_Click(object sender, RoutedEventArgs e)
        {
            if (!(ThreadPicker.SelectedItem is ChatChoice choice)) return;
            try { ReleaseWindow(); }
            catch (Exception ex) { StatusText.Text = "Frigør først Codex-vinduet: " + ex.Message; return; }
            ConnectButton.IsEnabled = false;
            var chat = new CodexSharedChat();
            _chat = chat;
            _connectedThread = choice.Id;
            chat.Snapshot += state => Dispatcher.BeginInvoke(new Action(() => { if (_chat == chat) Render(state); }));
            chat.Disconnected += ex => Dispatcher.BeginInvoke(new Action(() => { if (_chat == chat) Disconnect(ex.Message); }));
            try
            {
                StatusText.Text = "Forbinder til Codex-appens samtale…";
                await chat.ConnectAsync(choice.Id, _visibleWork.Token);
                if (_chat != chat || !IsVisible) { chat.Dispose(); return; }
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
            StatusText.Text = busy ? "Codex arbejder · svar vises begge steder. Se eventuelle godkendelser i appen." : "Delt samtale · opdateres automatisk.";
        }
        private async Task SendAsync()
        {
            var chat = _chat;
            string text = Composer.Text;
            if (chat == null || _sending || string.IsNullOrWhiteSpace(text)) return;
            _sending = true; SendButton.IsEnabled = false;
            try
            {
                await chat.SendAsync(text, _visibleWork.Token);
                if (_chat != chat) return;
                if (Composer.Text == text) Composer.Clear();
                StatusText.Text = "Beskeden er modtaget af Codex-appen.";
                await chat.RefreshAsync(force: true);
            }
            catch (Exception ex) { if (_chat == chat) StatusText.Text = ex.Message; }
            finally { _sending = false; SendButton.IsEnabled = _chat != null && _chat.CanSend; }
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
        private void Disconnect(string reason) { SafeReleaseWindow(); StatusText.Text = reason; }
        private void SafeReleaseWindow()
        {
            try { ReleaseWindow(); }
            catch (Exception ex) { if (StatusText != null) StatusText.Text = "Codex-vinduet kunne ikke frigøres: " + ex.Message; }
        }
        // Retained entry point used by the Revit toggle/shutdown commands.
        public void ReleaseWindow()
        {
            try { MirrorPanel?.Stop(); }
            finally { _chat?.Dispose(); _chat = null; if (SendButton != null) SendButton.IsEnabled = false; }
        }
        public void Shutdown() { _timer.Stop(); _visibleWork.Cancel(); ++_generation; ReleaseWindow(); }
        public void LogCommand(string commandName, bool success, string message, double durationMs) { }
    }
}
