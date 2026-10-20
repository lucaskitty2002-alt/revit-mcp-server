using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace revit_mcp_plugin.UI
{
    public sealed class CodexDesktopHost : HwndHost
    {
        private IntPtr _container;
        private readonly DesktopWindowLease _lease = new DesktopWindowLease(Path.Combine(CodexSettings.SettingsDirectory, "desktop-window-recovery.json"));
        public bool IsAttached => _lease.IsAttached;
        public event Action<string> Status;

        protected override HandleRef BuildWindowCore(HandleRef parent)
        {
            _container = CreateWindowEx(0, "static", "Revit Codex window host", 0x40000000 | 0x10000000 | 0x02000000, 0, 0, 1, 1, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (_container == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return new HandleRef(this, _container);
        }
        public void Attach(CodexDesktopWindow window)
        {
            if (_container == IntPtr.Zero) throw new InvalidOperationException("Open the Revit panel before attaching a window.");
            _lease.Attach(window, _container);
            _lease.Focus();
            Status?.Invoke("The actual Codex app is inside Revit. Its chats, tools and settings are managed by Codex.");
        }
        public void Release()
        {
            _lease.Release();
            Status?.Invoke("Codex has been released to the desktop.");
        }
        public void Recover() { _lease.Recover(); Status?.Invoke("Window recovery completed. Refresh the list to attach Codex again."); }
        protected override void OnWindowPositionChanged(Rect rectangle)
        {
            base.OnWindowPositionChanged(rectangle);
            try { _lease.Resize(); }
            catch (Exception ex) { Status?.Invoke("Codex could not be resized: " + ex.Message); }
        }
        protected override bool TabIntoCore(TraversalRequest request) => _lease.Focus();
        protected override void DestroyWindowCore(HandleRef window)
        {
            try { _lease.Release(); }
            catch (Exception ex) { Status?.Invoke("Codex could not be released: " + ex.Message); return; }
            DestroyWindow(window.Handle);
            _container = IntPtr.Zero;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowEx(int exStyle, string className, string title, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
    }
}
