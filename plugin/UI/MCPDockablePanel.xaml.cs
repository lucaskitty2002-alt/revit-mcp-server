using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using revit_mcp_plugin.Core;

namespace revit_mcp_plugin.UI
{
    public partial class MCPDockablePanel : Page
    {
        public static MCPDockablePanel Instance { get; private set; }
        private readonly DispatcherTimer _statusTimer;
        public MCPDockablePanel()
        {
            InitializeComponent();
            Instance = this;
            DesktopHost.Status += text => StatusText.Text = text;
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _statusTimer.Tick += (sender, e) => UpdateStatus();
        }
        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _statusTimer.Start();
            UpdateStatus();
            if (!DesktopHost.IsAttached) RefreshWindows();
        }
        private void Page_Unloaded(object sender, RoutedEventArgs e) { _statusTimer.Stop(); ReleaseWindow(); }
        private void Page_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!(bool)e.NewValue) { _statusTimer?.Stop(); ReleaseWindow(); }
            else if (IsLoaded) _statusTimer?.Start();
        }
        private void UpdateStatus()
        {
            try { RevitStatus.Text = SocketService.Instance.IsRunning ? "Revit MCP is online. Use the revit-mcp tools from Codex." : "Revit MCP is offline. Enable Revit MCP Switch for model tools."; }
            catch { RevitStatus.Text = "Enable Revit MCP Switch to connect model tools."; }
            if (DesktopHost.Visibility == Visibility.Visible && !DesktopHost.IsAttached)
            {
                ReleaseWindow();
                StatusText.Text = "The Codex window closed or moved. Refresh windows to select it again.";
            }
        }
        private void RefreshWindows()
        {
            try
            {
                var windows = DesktopWindowLease.FindCodexWindows();
                WindowPicker.ItemsSource = windows;
                if (windows.Count > 0) WindowPicker.SelectedIndex = 0;
                StatusText.Text = windows.Count == 0 ? "No Codex desktop window found. Open Codex, then refresh. Run Codex and Revit with the same permissions." : "Select a Codex window and choose Show in Revit.";
            }
            catch (Exception ex) { StatusText.Text = ex.Message; }
        }
        private void OpenCodex_Click(object sender, RoutedEventArgs e)
        {
            try { DesktopWindowLease.LaunchCodex(); StatusText.Text = "Opening Codex. Once its window is ready, choose Refresh windows."; }
            catch (Exception ex) { StatusText.Text = "Codex could not be opened: " + ex.Message; }
        }
        private void Refresh_Click(object sender, RoutedEventArgs e) { if (!DesktopHost.IsAttached) RefreshWindows(); }
        private async void Attach_Click(object sender, RoutedEventArgs e)
        {
            if (DesktopHost.IsAttached) return;
            if (!(WindowPicker.SelectedItem is CodexDesktopWindow window)) { StatusText.Text = "Select a Codex window first."; return; }
            AttachButton.IsEnabled = false;
            try
            {
                Instructions.Visibility = Visibility.Collapsed;
                DesktopHost.Visibility = Visibility.Visible;
                await Dispatcher.InvokeAsync(() => { DesktopHost.UpdateLayout(); DesktopHost.Attach(window); }, DispatcherPriority.Loaded);
                WindowPicker.IsEnabled = false;
                RefreshButton.IsEnabled = false;
            }
            catch (Exception ex)
            {
                DesktopHost.Visibility = Visibility.Collapsed;
                Instructions.Visibility = Visibility.Visible;
                StatusText.Text = ex.Message;
            }
            finally { AttachButton.IsEnabled = true; }
        }
        public void ReleaseWindow()
        {
            if (DesktopHost == null || StatusText == null) return;
            try
            {
                DesktopHost.Release();
                DesktopHost.Visibility = Visibility.Collapsed;
                Instructions.Visibility = Visibility.Visible;
                WindowPicker.IsEnabled = true;
                RefreshButton.IsEnabled = true;
            }
            catch (Exception ex) { StatusText.Text = "Could not release Codex: " + ex.Message; }
        }
        private void Release_Click(object sender, RoutedEventArgs e) => ReleaseWindow();
        private void Recover_Click(object sender, RoutedEventArgs e)
        {
            try { DesktopHost.Recover(); ReleaseWindow(); RefreshWindows(); }
            catch (Exception ex) { StatusText.Text = ex.Message; }
        }
        public void Shutdown() { _statusTimer.Stop(); ReleaseWindow(); }
        public void LogCommand(string commandName, bool success, string message, double durationMs) { }
    }
}
