using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace revit_mcp_plugin.UI
{
    public partial class CodexMirrorPanel : UserControl
    {
        private readonly DesktopOverlayLease _overlay = new DesktopOverlayLease(Path.Combine(CodexSettings.SettingsDirectory, "overlay-window-recovery.json"));
        private readonly DispatcherTimer _timer;
        private Window _hostingWindow;
        private bool _attaching;
        private int _operation;

        public CodexMirrorPanel()
        {
            InitializeComponent();
            Surface.Status += text => { if (!_attaching && !_overlay.HasLease) SetStatus("Forhåndsvisning", text, "info"); };
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _timer.Tick += (s, e) =>
            {
                try
                {
                    if (!_overlay.HasLease) { _timer.Stop(); UpdateControls(); return; }
                    _overlay.Update(ScreenBounds(), MoreButton.ContextMenu?.IsOpen != true && !WindowPicker.IsDropDownOpen);
                    if (!_overlay.IsAttached)
                    {
                        Stop();
                        SetStatus("Codex-vinduet er lukket", "Åbn appen og opdater vindueslisten for at forbinde igen.", "info");
                    }
                    else if (_overlay.IsPaused) SetStatus("Vinduesplaceringen er sat på pause", _overlay.PausedReason, "info");
                    else if (_overlay.IsVisiblyPlaced()) SetStatus("Codex-vinduet er placeret", "Klik i appens skrivefelt. Frigør sender vinduet tilbage til skrivebordet.", "info");
                    else SetStatus("Codex er i baggrunden", "Vælg Revit eller tryk Vis igen for at hente vinduet frem.", "info");
                    UpdateControls();
                }
                catch (Exception ex) { Fail(ex, "update_failed"); }
            };
        }

        private void Panel_Loaded(object sender, RoutedEventArgs e)
        {
            if (_hostingWindow != null) _hostingWindow.Closing -= Host_Closing;
            _hostingWindow = Window.GetWindow(this);
            if (_hostingWindow != null) _hostingWindow.Closing += Host_Closing;
            if (WindowPicker.ItemsSource == null) Refresh();
        }
        private void Host_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try { Stop(); }
            catch (Exception ex) { e.Cancel = true; Fail(ex, "release_failed"); }
        }
        private void Panel_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_hostingWindow != null) { _hostingWindow.Closing -= Host_Closing; _hostingWindow = null; }
            try { Stop(); } catch (Exception ex) { SetStatus("Vinduet skal frigøres", ex.Message, "error"); }
        }
        private void Panel_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!(bool)e.NewValue) try { Stop(); } catch (Exception ex) { SetStatus("Vinduet skal frigøres", ex.Message, "error"); }
        }
        private void Refresh()
        {
            if (_attaching) return;
            try
            {
                if (_overlay.HasLease || Surface.IsSharing) Stop();
                SetStatus("Finder Codex…", "Søger efter åbne appvinduer.", "info");
                var windows = DesktopWindowLease.FindCodexWindows();
                WindowPicker.ItemsSource = windows;
                if (windows.Count > 0) WindowPicker.SelectedIndex = 0;
                SetStatus(windows.Count > 0 ? "Klar til at vise Codex" : "Åbn Codex for at begynde",
                    windows.Count > 0 ? "Vælg dit vindue og tryk Vis i Revit. Et maksimeret vindue tilpasses automatisk." : "Åbn appen nedenfor, og tryk derefter på opdateringsknappen.", "info");
                UpdateControls();
            }
            catch (Exception ex) { Fail(ex, "discovery_failed"); }
        }
        private void WindowPicker_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateControls();
        private void Panel_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateLayoutMode();
        }
        private void UpdateLayoutMode()
        {
            if (ActionGroup == null) return;
            bool compact = _attaching || _overlay.HasLease;
            bool narrow = !compact && ActualWidth < 520;
            WelcomeHeader.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            Footer.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            PickerGroup.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            RefreshButton.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            CompactLabel.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
            CompactLabel.Text = _overlay.IsPaused ? "På pause" : "Codex";
            CompactLabel.ToolTip = StatusTitle.Text + "\n" + StatusText.Text;
            StatusBorder.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            PanelGrid.Margin = compact ? new Thickness(8) : new Thickness(16);
            ConnectionCard.Padding = compact ? new Thickness(8) : new Thickness(12);
            ConnectionCard.Margin = new Thickness(0, 0, 0, compact ? 6 : 12);
            Grid.SetColumnSpan(PickerGroup, narrow ? 2 : 1);
            Grid.SetRow(ActionGroup, narrow ? 1 : 0);
            Grid.SetColumn(ActionGroup, narrow ? 0 : 1);
            Grid.SetColumnSpan(ActionGroup, narrow ? 2 : 1);
            PickerGroup.Margin = narrow ? new Thickness(0) : new Thickness(0, 0, 10, 0);
            ActionGroup.Margin = narrow ? new Thickness(0, 10, 0, 0) : new Thickness(0);
            EmptyGraphic.Visibility = ActualHeight < 520 ? Visibility.Collapsed : Visibility.Visible;
            EmptyDescription.Visibility = ActualHeight < 440 ? Visibility.Collapsed : Visibility.Visible;
        }
        private void UpdateControls()
        {
            if (ShowButton == null || WindowPicker == null) return;
            bool attached = _overlay.HasLease;
            ShowButton.IsEnabled = !_attaching && (attached || WindowPicker.SelectedItem is CodexDesktopWindow);
            ShowButton.Content = _attaching ? "Åbner…" : attached ? "Vis igen" : "Vis i Revit";
            RefreshButton.IsEnabled = !_attaching;
            WindowPicker.IsEnabled = !_attaching && !attached;
            ReleaseButton.Visibility = attached || Surface.IsSharing ? Visibility.Visible : Visibility.Collapsed;
            ReleaseButton.IsEnabled = !_attaching;
            EmptyState.Visibility = _attaching || attached || Surface.IsSharing ? Visibility.Collapsed : Visibility.Visible;
            OpenButton.Visibility = WindowPicker.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateLayoutMode();
        }
        private void SetStatus(string title, string detail, string kind)
        {
            if (StatusTitle == null) return;
            StatusTitle.Text = title; StatusText.Text = detail;
            StatusBorder.Background = Brush(kind == "error" ? "#FEF3F2" : kind == "success" ? "#ECFDF3" : "#EFF4FF");
            StatusDot.Fill = Brush(kind == "error" ? "#D92D20" : kind == "success" ? "#039855" : "#2563EB");
        }
        private static Brush Brush(string color) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        private DesktopMirrorSession.Native.Rect ScreenBounds()
        {
            return DesktopScreenGeometry.GetBounds(Surface);
        }
        private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();
        private void Open_Click(object sender, RoutedEventArgs e)
        {
            try { DesktopWindowLease.LaunchCodex(); SetStatus("Codex åbnes", "Når appvinduet er klar, tryk på opdateringsknappen og vælg Vis i Revit.", "info"); }
            catch (Exception ex) { Fail(ex, "launch_failed"); }
        }
        private async void Start_Click(object sender, RoutedEventArgs e)
        {
            if (_attaching) return;
            if (_overlay.HasLease)
            {
                try { _overlay.Resume(ScreenBounds()); SaveDiagnostic("resumed", null); }
                catch (Exception ex) { Fail(ex, "resume_failed"); }
                return;
            }
            if (!(WindowPicker.SelectedItem is CodexDesktopWindow window)) { SetStatus("Vælg et Codex-vindue", "Opdater listen, hvis vinduet ikke er vist.", "info"); return; }
            int operation = -1;
            try
            {
                Stop(); _attaching = true; operation = ++_operation;
                SetStatus("Åbner Codex i Revit…", "Vinduet gendannes og tilpasses panelet.", "info"); UpdateControls();
                await Dispatcher.InvokeAsync(() => Surface.UpdateLayout(), DispatcherPriority.Loaded);
                if (operation != _operation || !IsVisible) return;
                var host = PresentationSource.FromVisual(this) as HwndSource;
                if (host == null) throw new InvalidOperationException("Åbn Revit-panelet og prøv igen.");
                _overlay.Attach(window, DesktopMirrorSession.Native.GetAncestor(host.Handle, 2), ScreenBounds());
                UpdateControls(); _timer.Start();
                await System.Threading.Tasks.Task.Delay(200);
                if (operation != _operation || !_overlay.HasLease) return;
                _overlay.Update(ScreenBounds());
                if (_overlay.IsPaused) SetStatus("Vinduesplaceringen er sat på pause", _overlay.PausedReason, "info");
                else if (_overlay.IsVisiblyPlaced()) SetStatus("Codex-vinduet er placeret", "Placeringen er kontrolleret. Klik i appens skrivefelt for at bruge den.", "info");
                else SetStatus("Codex er i baggrunden", "Windows har ikke bekræftet synligheden. Tryk Vis igen eller frigør vinduet.", "info");
                SaveDiagnostic("attached", null);
            }
            catch (Exception ex) { Fail(ex, "attach_failed"); }
            finally { if (operation == _operation) { _attaching = false; UpdateControls(); } }
        }
        private void Preview_Click(object sender, RoutedEventArgs e)
        {
            if (!(WindowPicker.SelectedItem is CodexDesktopWindow window)) { SetStatus("Vælg et vindue først", "Åbn Codex og opdater listen.", "info"); return; }
            try { Stop(); Surface.Start(window); UpdateControls(); }
            catch (Exception ex) { Fail(ex, "preview_failed"); }
        }
        private void Recover_Click(object sender, RoutedEventArgs e)
        {
            try { Stop(); _overlay.Recover(); SetStatus("Placeringen er gendannet", "Codex-vinduet er frigjort. Opdater listen for at vise det igen.", "success"); UpdateControls(); }
            catch (Exception ex) { Fail(ex, "recovery_failed"); }
        }
        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            try { Stop(); SetStatus("Codex er frigjort", "Vinduet er tilbage på skrivebordet. Du kan vise det i Revit igen.", "info"); }
            catch (Exception ex) { Fail(ex, "release_failed"); }
        }
        private void More_Click(object sender, RoutedEventArgs e)
        {
            MoreButton.ContextMenu.PlacementTarget = MoreButton;
            MoreButton.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            MoreButton.ContextMenu.IsOpen = true;
        }
        private void Diagnostics_Click(object sender, RoutedEventArgs e)
        {
            string report = _overlay.LastDiagnostic;
            if (string.IsNullOrWhiteSpace(report)) report = "Version " + GetType().Assembly.GetName().Version + "\n" + StatusTitle.Text + "\n" + StatusText.Text;
            MessageBox.Show(report, "Codex · teknisk info", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private void SaveDiagnostic(string action, string error)
        {
            try
            {
                _overlay.CaptureDiagnostic(action, error);
                Directory.CreateDirectory(CodexSettings.SettingsDirectory);
                File.WriteAllText(Path.Combine(CodexSettings.SettingsDirectory, "window-diagnostics.json"), _overlay.LastDiagnostic);
            }
            catch { }
        }
        private void Fail(Exception ex, string action)
        {
            // Capture requested/actual bounds before rollback. Never log chat content.
            if (string.IsNullOrWhiteSpace(_overlay.LastDiagnostic) || _overlay.HasLease) SaveDiagnostic(action, ex.Message);
            else try { Directory.CreateDirectory(CodexSettings.SettingsDirectory); File.WriteAllText(Path.Combine(CodexSettings.SettingsDirectory, "window-diagnostics.json"), _overlay.LastDiagnostic); } catch { }
            string message = ex.Message;
            try { Stop(); } catch (Exception releaseError) { message += " Frigørelse mislykkedes: " + releaseError.Message; }
            SetStatus("Codex kunne ikke vises", message, "error"); UpdateControls();
        }
        public void Stop()
        {
            ++_operation; _timer?.Stop(); Surface?.Stop(); _overlay.Release(); _attaching = false;
            UpdateControls();
        }
    }
}
