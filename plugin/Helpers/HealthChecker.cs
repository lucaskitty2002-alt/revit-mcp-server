using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using Newtonsoft.Json.Linq;

namespace revit_mcp_plugin.Helpers
{
    /// <summary>
    /// Simple data class representing the result of a single health check.
    /// </summary>
    public class HealthCheckResult
    {
        public string Name { get; set; }
        public bool Passed { get; set; }
        public bool AutoFixed { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// Runs diagnostic checks that cover common installation and connectivity
    /// issues: blocked DLLs, missing server files, Codex availability,
    /// and TCP port connectivity. Every method is safe to call at any time —
    /// exceptions are caught internally and reported as failed checks.
    /// </summary>
    public static class HealthChecker
    {
        private const string Tag = "HealthChecker";



        // -----------------------------------------------------------------
        // P/Invoke for DLL unblocking (Zone.Identifier ADS removal)
        // -----------------------------------------------------------------

        private static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("kernel32.dll",
                SetLastError = true,
                CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
            public static extern bool DeleteFile(string lpFileName);
        }

        // -----------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------

        /// <summary>
        /// Runs every available health check and returns the aggregated results.
        /// </summary>
        public static List<HealthCheckResult> RunAll(string pluginDir, int port)
        {
            var results = new List<HealthCheckResult>
            {
                CheckDllBlocked(pluginDir),
                CheckServerFiles(pluginDir),
                CheckCodex(),
                CheckPortConnectivity(port)
            };
            return results;
        }

        /// <summary>
        /// Checks whether any DLL in the plugin directory is blocked by Windows
        /// (Zone.Identifier alternate data stream) and automatically unblocks them.
        /// </summary>
        public static HealthCheckResult CheckDllBlocked(string pluginDir)
        {
            var result = new HealthCheckResult { Name = "DLL Blocked" };
            try
            {
                if (string.IsNullOrEmpty(pluginDir) || !Directory.Exists(pluginDir))
                {
                    result.Passed = false;
                    result.Message = $"Plugin directory not found: {pluginDir}";
                    return result;
                }

                string[] dlls = Directory.GetFiles(pluginDir, "*.dll", SearchOption.AllDirectories);
                int unblockedCount = 0;

                foreach (string dll in dlls)
                {
                    try
                    {
                        // DeleteFile returns true if the ADS existed and was deleted.
                        bool hadStream = NativeMethods.DeleteFile(dll + ":Zone.Identifier");
                        if (hadStream)
                        {
                            unblockedCount++;
                            McpLogger.Info(Tag, $"Unblocked: {dll}");
                        }
                    }
                    catch (Exception ex)
                    {
                        McpLogger.Warn(Tag, $"Could not check/unblock {dll}: {ex.Message}");
                    }
                }

                if (unblockedCount > 0)
                {
                    result.Passed = true;
                    result.AutoFixed = true;
                    result.Message = $"Unblocked {unblockedCount} DLL(s)";
                    McpLogger.Info(Tag, result.Message);
                }
                else
                {
                    result.Passed = true;
                    result.Message = "No blocked DLLs found";
                }
            }
            catch (Exception ex)
            {
                result.Passed = false;
                result.Message = $"Error checking DLLs: {ex.Message}";
                McpLogger.Error(Tag, "DLL block check failed", ex);
            }
            return result;
        }

        /// <summary>
        /// Verifies that the bundled Node runtime and server entry point exist
        /// in the expected locations under the plugin directory.
        /// </summary>
        public static HealthCheckResult CheckServerFiles(string pluginDir)
        {
            var result = new HealthCheckResult { Name = "Server Files" };
            try
            {
                string nodePath = Path.Combine(
                    pluginDir, "Commands", "RevitMCPCommandSet", "server", "runtime", "node.exe");
                string indexPath = Path.Combine(
                    pluginDir, "Commands", "RevitMCPCommandSet", "server", "build", "index.js");

                if (!File.Exists(nodePath))
                {
                    // Bundled node.exe not found — check if Node.js is installed system-wide
                    bool systemNodeFound = false;
                    try
                    {
                        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
                        foreach (var dir in pathEnv.Split(';'))
                        {
                            if (!string.IsNullOrWhiteSpace(dir))
                            {
                                var candidate = Path.Combine(dir.Trim(), "node.exe");
                                if (File.Exists(candidate))
                                {
                                    systemNodeFound = true;
                                    McpLogger.Info(Tag, $"Bundled node.exe not found, using system Node.js: {candidate}");
                                    break;
                                }
                            }
                        }
                    }
                    catch { /* best-effort PATH scan */ }

                    if (!systemNodeFound)
                    {
                        result.Passed = false;
                        result.Message = $"node.exe not found (bundled or system). Install Node.js or reinstall the plugin.";
                        McpLogger.Warn(Tag, result.Message);
                        return result;
                    }
                }

                if (!File.Exists(indexPath))
                {
                    result.Passed = false;
                    result.Message = $"index.js not found at {indexPath}";
                    McpLogger.Warn(Tag, result.Message);
                    return result;
                }

                result.Passed = true;
                result.Message = "node.exe and index.js present";
            }
            catch (Exception ex)
            {
                result.Passed = false;
                result.Message = $"Error checking server files: {ex.Message}";
                McpLogger.Error(Tag, "Server files check failed", ex);
            }
            return result;
        }

        public static HealthCheckResult CheckCodex()
        {
            var result = new HealthCheckResult { Name = "Codex" };
            try
            {
                result.Message = UI.CodexSettings.Load().FindExecutable();
                result.Passed = true;
            }
            catch (Exception ex) { result.Message = ex.Message; }
            return result;
        }
        /// <summary>
        /// Attempts a quick TCP connection to 127.0.0.1 on the specified port
        /// to verify that the MCP server is listening.
        /// </summary>
        public static HealthCheckResult CheckPortConnectivity(int port)
        {
            var result = new HealthCheckResult { Name = "Port Connectivity" };
            try
            {
                if (port <= 0)
                {
                    result.Passed = false;
                    result.Message = "Server not started";
                    return result;
                }

                using (var client = new TcpClient())
                {
                    var connectResult = client.BeginConnect("127.0.0.1", port, null, null);
                    bool connected = connectResult.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(500));

                    if (connected && client.Connected)
                    {
                        client.EndConnect(connectResult);
                        result.Passed = true;
                        result.Message = $"Port {port} responding";
                    }
                    else
                    {
                        result.Passed = false;
                        result.Message = $"Port {port} not responding \u2014 click Revit MCP Switch";
                    }
                }
            }
            catch (Exception ex)
            {
                result.Passed = false;
                result.Message = $"Port {port} not responding \u2014 click Revit MCP Switch";
                McpLogger.Warn(Tag, $"Port connectivity check failed: {ex.Message}");
            }
            return result;
        }

    }
}
