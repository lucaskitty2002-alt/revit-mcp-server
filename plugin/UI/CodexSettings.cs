using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace revit_mcp_plugin.UI
{
    public sealed class CodexSettings
    {
        public string Executable { get; set; } = "";
        public string Model { get; set; } = "";
        public string SharedThreadId { get; set; } = "";
        public static string SettingsDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitMCP", "Codex");
        public static string Workspace => Path.Combine(SettingsDirectory, "workspace");
        private static string SettingsFile => Path.Combine(SettingsDirectory, "settings.json");
        public static CodexSettings Load()
        {
            return File.Exists(SettingsFile) ? JsonConvert.DeserializeObject<CodexSettings>(File.ReadAllText(SettingsFile)) ?? new CodexSettings() : new CodexSettings();
        }
        public void Save()
        {
            if (!string.IsNullOrWhiteSpace(Executable) && (!File.Exists(Executable) || !Executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Choose the Codex executable (codex.exe).");
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(SettingsFile, JsonConvert.SerializeObject(this, Formatting.Indented));
        }
        public string FindExecutable()
        {
            if (!string.IsNullOrWhiteSpace(Executable))
            {
                if (File.Exists(Executable)) return Executable;
                throw new FileNotFoundException("The saved Codex path no longer exists. Update Settings > Codex.");
            }
            string configured = Environment.GetEnvironmentVariable("CODEX_EXECUTABLE");
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
            foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(folder)) continue;
                string candidate = Path.Combine(folder.Trim('"'), "codex.exe");
                if (File.Exists(candidate)) return candidate;
            }
            string desktopBin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
            if (Directory.Exists(desktopBin))
            {
                var candidate = Directory.GetFiles(desktopBin, "codex.exe", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                if (candidate != null) return candidate;
            }
            string npm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "@openai", "codex");
            if (Directory.Exists(npm))
            {
                var candidate = Directory.GetFiles(npm, "codex.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (candidate != null) return candidate;
            }
            throw new FileNotFoundException("Codex was not found. Install Codex, sign in, or choose codex.exe in Settings > Codex.");
        }
    }
}
