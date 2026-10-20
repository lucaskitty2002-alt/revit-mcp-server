using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json;

namespace revit_mcp_plugin.UI
{
    public sealed class CodexDesktopWindow
    {
        public IntPtr Handle { get; set; }
        public int ProcessId { get; set; }
        public string Title { get; set; }
        public string DisplayName => string.IsNullOrWhiteSpace(Title) || Title.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase) ? "Codex-app" : Title;
        public override string ToString() => Title + " (" + ProcessId + ")";
    }

    // Owns only a temporary parent/style change. It never closes or kills the external app.
    public sealed class DesktopWindowLease
    {
        private readonly string _recoveryFile;
        private WindowSnapshot _snapshot;
        public IntPtr Window => _snapshot == null ? IntPtr.Zero : new IntPtr(_snapshot.Window);
        public bool IsAttached => _snapshot != null && Native.IsWindow(Window) && Native.GetParent(Window) == new IntPtr(_snapshot.Host);
        public DesktopWindowLease(string recoveryFile) { _recoveryFile = recoveryFile; }

        public static List<CodexDesktopWindow> FindCodexWindows()
        {
            var windows = new List<CodexDesktopWindow>();
            Native.EnumWindows((window, parameter) =>
            {
                if (!Native.IsWindowVisible(window) || Native.GetWindow(window, 4) != IntPtr.Zero) return true;
                Native.GetWindowThreadProcessId(window, out uint pid);
                try
                {
                    using (var process = Process.GetProcessById((int)pid))
                    {
                        string name = process.ProcessName;
                        if (!name.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase) && !name.Equals("Codex", StringComparison.OrdinalIgnoreCase)) return true;
                        string path = process.MainModule.FileName;
                        // The packaged Codex GUI is ChatGPT.exe. The command-line codex.exe is not a GUI.
                        bool codex = path.IndexOf("\\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            (FileVersionInfo.GetVersionInfo(path).ProductName ?? "").IndexOf("Codex", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!codex) return true;
                        var title = new StringBuilder(512);
                        Native.GetWindowText(window, title, title.Capacity);
                        windows.Add(new CodexDesktopWindow { Handle = window, ProcessId = (int)pid, Title = title.Length == 0 ? "Codex" : title.ToString() });
                    }
                }
                catch (Win32Exception) { }
                catch (InvalidOperationException) { }
                catch (ArgumentException) { }
                return true;
            }, IntPtr.Zero);
            return windows;
        }

        public static void LaunchCodex()
        {
            // Official Microsoft Store app registration; no guessed executable or command-line login.
            Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\OpenAI.Codex_2p2nqsd0c76g0!App") { UseShellExecute = true });
        }

        public void Attach(CodexDesktopWindow selected, IntPtr host)
        {
            if (_snapshot != null) throw new InvalidOperationException("Release the current window first.");
            if (File.Exists(_recoveryFile)) throw new InvalidOperationException("A previous window-sharing record exists. Choose Recover window first.");
            if (selected == null || !Native.IsWindow(selected.Handle) || !Native.IsWindow(host)) throw new InvalidOperationException("The selected window is no longer available. Refresh the list.");
            Native.GetWindowThreadProcessId(selected.Handle, out uint actualPid);
            if (actualPid != selected.ProcessId) throw new InvalidOperationException("The selected window changed. Refresh the list.");
            if (Native.SendMessageTimeout(selected.Handle, 0, IntPtr.Zero, IntPtr.Zero, 2, 1000, out _) == IntPtr.Zero)
                throw new InvalidOperationException("Codex is not responding. Wait for the app, then try again.");
            // Cross-process SetParent can reset an app's DPI mode. Refuse that mutation rather than changing its scaling globally.
            if (!Native.AreDpiAwarenessContextsEqual(Native.GetWindowDpiAwarenessContext(selected.Handle), Native.GetWindowDpiAwarenessContext(host)))
                throw new InvalidOperationException("Windows reports incompatible display-scaling modes for Codex and Revit. The window was left unchanged. Use Codex beside Revit on this setup.");
            var placement = new Native.WindowPlacement { Length = Marshal.SizeOf(typeof(Native.WindowPlacement)) };
            Check(Native.GetWindowPlacement(selected.Handle, ref placement), "Read Codex window position");
            using (var process = Process.GetProcessById(selected.ProcessId))
            {
                _snapshot = new WindowSnapshot
                {
                    Window = selected.Handle.ToInt64(), Host = host.ToInt64(), ProcessId = selected.ProcessId,
                    ProcessStartTicks = process.StartTime.ToUniversalTime().Ticks,
                    WasVisible = Native.IsWindowVisible(selected.Handle),
                    Parent = Native.GetParent(selected.Handle).ToInt64(),
                    Style = Native.GetWindowLongPtr(selected.Handle, -16).ToInt64(),
                    ExtendedStyle = Native.GetWindowLongPtr(selected.Handle, -20).ToInt64(), Placement = placement
                };
            }
            Directory.CreateDirectory(Path.GetDirectoryName(_recoveryFile));
            try { File.WriteAllText(_recoveryFile, JsonConvert.SerializeObject(_snapshot)); }
            catch { _snapshot = null; throw; }
            try
            {
                Native.ShowWindow(selected.Handle, 9); // Restore before converting a minimized top-level window.
                long childStyle = (_snapshot.Style & ~(0x80000000L | 0x00C00000L | 0x00040000L | 0x00080000L | 0x00030000L)) | 0x40000000L;
                SetStyle(selected.Handle, -16, childStyle);
                SetStyle(selected.Handle, -20, _snapshot.ExtendedStyle & ~0x00040000L);
                SetParentChecked(selected.Handle, host);
                Resize();
                Native.ShowWindow(selected.Handle, 5);
            }
            catch
            {
                // Keep the recovery record if restoration itself fails.
                Release();
                throw;
            }
        }

        public void Resize()
        {
            if (!IsAttached) return;
            Native.GetClientRect(new IntPtr(_snapshot.Host), out var rectangle);
            Check(Native.SetWindowPos(Window, IntPtr.Zero, 0, 0, Math.Max(1, rectangle.Right), Math.Max(1, rectangle.Bottom), 0x4000 | 0x0004 | 0x0010 | 0x0020), "Resize Codex window");
        }

        public bool Focus()
        {
            if (!IsAttached) return false;
            uint foreignThread = Native.GetWindowThreadProcessId(Window, out _);
            uint ourThread = Native.GetCurrentThreadId();
            bool attached = foreignThread != ourThread && Native.AttachThreadInput(ourThread, foreignThread, true);
            try { Native.SetFocus(Window); return true; }
            finally { if (attached) Native.AttachThreadInput(ourThread, foreignThread, false); }
        }

        public void Release()
        {
            if (_snapshot == null) return;
            Restore(_snapshot);
            _snapshot = null;
            File.Delete(_recoveryFile);
        }

        public void Recover()
        {
            if (_snapshot != null) { Release(); return; }
            if (!File.Exists(_recoveryFile)) return;
            var snapshot = JsonConvert.DeserializeObject<WindowSnapshot>(File.ReadAllText(_recoveryFile));
            if (snapshot == null) throw new InvalidOperationException("The recovery record cannot be read.");
            IntPtr host = new IntPtr(snapshot.Host);
            if (Native.IsWindow(host)) throw new InvalidOperationException("The window is still hosted by a running Revit panel. Release it there first.");
            Restore(snapshot);
            File.Delete(_recoveryFile);
        }

        private static void Restore(WindowSnapshot snapshot)
        {
            IntPtr window = new IntPtr(snapshot.Window);
            if (!Native.IsWindow(window)) return; // App already closed; never restart or terminate it.
            Native.GetWindowThreadProcessId(window, out uint pid);
            if (pid != snapshot.ProcessId) return; // Reused HWND.
            try { using (var process = Process.GetProcessById((int)pid)) if (process.StartTime.ToUniversalTime().Ticks != snapshot.ProcessStartTicks) return; }
            catch (ArgumentException) { return; }
            IntPtr parent = new IntPtr(snapshot.Parent);
            if (parent != IntPtr.Zero && !Native.IsWindow(parent)) parent = IntPtr.Zero;
            SetParentChecked(window, parent);
            SetStyle(window, -16, snapshot.Style);
            SetStyle(window, -20, snapshot.ExtendedStyle);
            var placement = snapshot.Placement;
            placement.Length = Marshal.SizeOf(typeof(Native.WindowPlacement));
            if (placement.ShowCommand == 2) placement.ShowCommand = 1;
            Check(Native.SetWindowPlacement(window, ref placement), "Restore Codex window position");
            Check(Native.SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0, 0x4000 | 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020), "Restore Codex window frame");
            Native.ShowWindow(window, snapshot.WasVisible ? placement.ShowCommand : 0);
        }

        private static void SetStyle(IntPtr window, int index, long value)
        {
            Native.SetLastError(0);
            IntPtr previous = Native.SetWindowLongPtr(window, index, new IntPtr(value));
            if (previous == IntPtr.Zero && Marshal.GetLastWin32Error() != 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        private static void SetParentChecked(IntPtr child, IntPtr parent)
        {
            Native.SetLastError(0);
            if (Native.SetParent(child, parent) == IntPtr.Zero && Marshal.GetLastWin32Error() != 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not move the Codex window. Run both apps with the same permissions.");
            IntPtr actualParent = Native.GetParent(child);
            // A WS_CHILD window temporarily reports the desktop until its original style is restored.
            if (actualParent != parent && !(parent == IntPtr.Zero && actualParent == Native.GetDesktopWindow()))
                throw new InvalidOperationException("Windows did not apply the requested window parent.");
        }
        private static void Check(bool success, string operation) { if (!success) throw new Win32Exception(Marshal.GetLastWin32Error(), operation); }

        public sealed class WindowSnapshot
        {
            public long Window, Host, Parent, Style, ExtendedStyle, ProcessStartTicks;
            public int ProcessId;
            public bool WasVisible;
            public Native.WindowPlacement Placement;
        }

        public static class Native
        {
            [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
            [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
            [StructLayout(LayoutKind.Sequential)] public struct WindowPlacement { public int Length, Flags, ShowCommand; public Point MinPosition, MaxPosition; public Rect NormalPosition; }
            public delegate bool EnumWindowCallback(IntPtr hwnd, IntPtr parameter);
            [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowCallback callback, IntPtr parameter);
            [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
            [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
            [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint command);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder title, int capacity);
            [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
            [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr hwnd);
            [DllImport("user32.dll")] public static extern IntPtr GetDesktopWindow();
            [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetParent(IntPtr child, IntPtr parent);
            [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
            [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
            [DllImport("user32.dll", SetLastError = true)] public static extern bool GetWindowPlacement(IntPtr hwnd, ref WindowPlacement placement);
            [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowPlacement(IntPtr hwnd, ref WindowPlacement placement);
            [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd, out Rect rectangle);
            [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
            [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int command);
            [DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr hwnd);
            [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint first, uint second, bool attach);
            [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
            [DllImport("user32.dll")] public static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);
            [DllImport("user32.dll")] public static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);
            [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
            [DllImport("kernel32.dll")] public static extern void SetLastError(uint error);
        }
    }
}
