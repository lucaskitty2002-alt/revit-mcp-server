using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using revit_mcp_plugin.UI;
using N = revit_mcp_plugin.UI.DesktopWindowLease.Native;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
        if (args.FirstOrDefault() == "--test-window") { TestWindow(args[1]); return; }
        string root = Path.Combine(Path.GetTempPath(), "revit-window-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string handleFile = Path.Combine(root, "window.txt");
        string recovery = Path.Combine(root, "recovery.json");
        using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath, "--test-window \"" + handleFile + "\"") { UseShellExecute = false, CreateNoWindow = true });
        IntPtr host = IntPtr.Zero;
        var lease = new DesktopWindowLease(recovery);
        try
        {
            Until(() => File.Exists(handleFile), "Child window startup");
            IntPtr external = new IntPtr(long.Parse(File.ReadAllText(handleFile)));
            host = MakeWindow();
            IntPtr originalStyle = N.GetWindowLongPtr(external, -16);
            IntPtr originalExStyle = N.GetWindowLongPtr(external, -20);
            var candidate = new CodexDesktopWindow { Handle = external, ProcessId = child.Id, Title = "Controlled test window" };
            lease.Attach(candidate, host);
            Assert(lease.IsAttached && N.GetParent(external) == host, "Cross-process window is parented to the test host");
            Assert(File.Exists(recovery), "Recovery state is saved before native window changes");
            Until(() => { N.GetClientRect(host, out var expected); N.GetClientRect(external, out var actual); return expected.Right == actual.Right && expected.Bottom == actual.Bottom; }, "Embedded window resizes to the host client area");
            IntPtr text = Marshal.StringToHGlobalUni("Codex test — æøå");
            try { Assert(N.SendMessageTimeout(external, 0x000C, IntPtr.Zero, text, 2, 1000, out _) != IntPtr.Zero, "External window remains responsive while embedded"); }
            finally { Marshal.FreeHGlobal(text); }
            lease.Release();
            Assert(N.GetParent(external) == IntPtr.Zero && N.GetWindowLongPtr(external, -16) == originalStyle && N.GetWindowLongPtr(external, -20) == originalExStyle, "Release restores original parent and window styles");
            Assert(!File.Exists(recovery) && !child.HasExited && N.IsWindow(external), "Release preserves the external process and removes recovery state");
            bool rejected = false;
            try { lease.Attach(new CodexDesktopWindow { Handle = external, ProcessId = child.Id + 1 }, host); } catch (InvalidOperationException) { rejected = true; }
            Assert(rejected && N.GetParent(external) == IntPtr.Zero && !File.Exists(recovery), "A reused or mismatched window identity is rejected without mutation");
            var previousContext = Native.SetThreadDpiAwarenessContext(new IntPtr(-1));
            IntPtr incompatible;
            try { incompatible = MakeWindow(); } finally { Native.SetThreadDpiAwarenessContext(previousContext); }
            try
            {
                rejected = false;
                try { lease.Attach(candidate, incompatible); } catch (InvalidOperationException) { rejected = true; }
                Assert(rejected && N.GetParent(external) == IntPtr.Zero && !File.Exists(recovery), "Incompatible DPI modes are refused before changing the external app");
            }
            finally { Native.DestroyWindow(incompatible); }
            lease.Attach(candidate, host);
            // Simulate a surviving external window after its former host is gone.
            N.SetParent(external, IntPtr.Zero);
            Native.DestroyWindow(host); host = IntPtr.Zero;
            var recovered = new DesktopWindowLease(recovery);
            recovered.Recover();
            Assert(N.GetParent(external) == IntPtr.Zero && N.GetWindowLongPtr(external, -16) == originalStyle && !File.Exists(recovery), "Orphan recovery restores only the recorded external window");
            host = MakeWindow();
            lease = new DesktopWindowLease(recovery);
            lease.Attach(candidate, host);
            uint thread = N.GetWindowThreadProcessId(external, out _);
            Native.PostThreadMessage(thread, 0x0012, IntPtr.Zero, IntPtr.Zero);
            Assert(child.WaitForExit(5000), "Controlled external process exits on its own message loop");
            lease.Release();
            Assert(!File.Exists(recovery), "Closing the external app while embedded is handled without terminating another process");
            Console.WriteLine("All native desktop-window tests passed. No user application windows were moved.");
        }
        finally
        {
            try { lease.Release(); }
            finally
            {
                if (host != IntPtr.Zero) Native.DestroyWindow(host);
                if (!child.HasExited) { Native.PostThreadMessage((uint)child.Threads[0].Id, 0x0012, IntPtr.Zero, IntPtr.Zero); if (!child.WaitForExit(3000)) child.Kill(); }
            }
        }
    }
    private static void TestWindow(string file)
    {
        IntPtr window = MakeWindow();
        File.WriteAllText(file, window.ToInt64().ToString());
        while (Native.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0) { Native.TranslateMessage(ref message); Native.DispatchMessage(ref message); }
        Native.DestroyWindow(window);
    }
    private static IntPtr MakeWindow()
    {
        IntPtr window = Native.CreateWindowEx(0, "static", "Revit MCP controlled test window", 0x00CF0000, 100, 100, 640, 480, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (window == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return window;
    }
    private static void Until(Func<bool> test, string label)
    {
        var time = Stopwatch.StartNew();
        while (!test()) { if (time.ElapsedMilliseconds > 5000) throw new Exception("FAILED: " + label); Thread.Sleep(20); }
        Console.WriteLine("PASS: " + label);
    }
    private static void Assert(bool value, string label) { if (!value) throw new Exception("FAILED: " + label); Console.WriteLine("PASS: " + label); }
    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct Message { public IntPtr Window; public uint Id; public UIntPtr WParam; public IntPtr LParam; public uint Time; public N.Point Point; public uint Private; }
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr CreateWindowEx(int extendedStyle, string className, string title, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
        [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr window);
        [DllImport("user32.dll")] public static extern int GetMessage(out Message message, IntPtr window, uint min, uint max);
        [DllImport("user32.dll")] public static extern bool TranslateMessage(ref Message message);
        [DllImport("user32.dll")] public static extern IntPtr DispatchMessage(ref Message message);
        [DllImport("user32.dll")] public static extern bool PostThreadMessage(uint thread, uint message, IntPtr wParam, IntPtr lParam);
    }
}
