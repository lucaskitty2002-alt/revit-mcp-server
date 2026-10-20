using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace revit_mcp_plugin.UI
{
    public partial class CodexSettingsPage : Page
    {
        public CodexSettingsPage()
        {
            InitializeComponent();
            try
            {
                var settings = CodexSettings.Load();
                ExecutablePath.Text = settings.Executable;
                ModelName.Text = settings.Model;
                Status.Text = "Codex found: " + settings.FindExecutable();
            }
            catch (Exception ex) { Status.Text = ex.Message; }
        }
        private CodexSettings Current => new CodexSettings { Executable = ExecutablePath.Text.Trim(), Model = ModelName.Text.Trim() };
        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Codex executable (codex.exe)|codex.exe", Title = "Select Codex" };
            if (dialog.ShowDialog() == true) ExecutablePath.Text = dialog.FileName;
        }
        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try { Current.Save(); Status.Text = "Saved. Start a new chat to apply these settings."; }
            catch (Exception ex) { Status.Text = ex.Message; }
        }
        private async void CheckLogin_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string executable = Current.FindExecutable();
                Status.Text = "Checking Codex login…";
                Status.Text = await Task.Run(() =>
                {
                    using (var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = executable, Arguments = "login status", UseShellExecute = false, CreateNoWindow = true,
                        RedirectStandardOutput = true, RedirectStandardError = true
                    }))
                    {
                        var output = process.StandardOutput.ReadToEndAsync();
                        var error = process.StandardError.ReadToEndAsync();
                        if (!process.WaitForExit(15000)) { process.Kill(); return "Codex login check timed out."; }
                        // login status reports account state, never access-token contents.
                        return output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult();
                    }
                });
            }
            catch (Exception ex) { Status.Text = ex.Message; }
        }
        private void SignIn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Current.Save();
                // This window is intentionally visible: the user requested interactive sign-in.
                Process.Start(new ProcessStartInfo { FileName = Current.FindExecutable(), Arguments = "login", UseShellExecute = true });
                Status.Text = "Complete sign-in in the opened window, then choose Check login.";
            }
            catch (Exception ex) { Status.Text = ex.Message; }
        }
    }
}
