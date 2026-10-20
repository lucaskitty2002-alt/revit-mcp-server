using System;
using System.Windows;
using System.Windows.Controls;
namespace revit_mcp_plugin.UI
{
    public partial class CodexSettingsPage : Page
    {
        public CodexSettingsPage() { InitializeComponent(); }
        private void OpenCodex_Click(object sender, RoutedEventArgs e)
        {
            try { DesktopWindowLease.LaunchCodex(); Status.Text = "Opening Codex. Choose your account and settings in the app."; }
            catch (Exception ex) { Status.Text = ex.Message; }
        }
    }
}
