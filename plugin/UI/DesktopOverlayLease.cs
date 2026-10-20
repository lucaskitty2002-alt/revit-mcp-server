using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using N = revit_mcp_plugin.UI.DesktopWindowLease.Native;

namespace revit_mcp_plugin.UI
{
    // Place the original top-level window over the panel. Owner != child parent:
    // this preserves the app's input loop and avoids cross-process SetParent/DPI resets.
    public sealed class DesktopOverlayLease
    {
        private readonly string _recoveryFile;
        private Snapshot _snapshot;
        public bool IsAttached => _snapshot != null && SameWindow(_snapshot);
        public DesktopOverlayLease(string recoveryFile) { _recoveryFile = recoveryFile; }
        public void Attach(CodexDesktopWindow window, IntPtr host, DesktopMirrorSession.Native.Rect bounds)
        {
            if (_snapshot != null) throw new InvalidOperationException("Frigør det aktuelle vindue først.");
            if (File.Exists(_recoveryFile)) throw new InvalidOperationException("En tidligere placering skal gendannes først.");
            if (window == null || !N.IsWindow(window.Handle) || !N.IsWindow(host)) throw new InvalidOperationException("Vinduet er lukket.");
            if (DesktopMirrorSession.Native.GetAncestor(window.Handle, 2) != window.Handle) throw new InvalidOperationException("Vælg et topniveauvindue.");
            N.GetWindowThreadProcessId(window.Handle, out uint pid);
            if (pid != window.ProcessId) throw new InvalidOperationException("Vinduets identitet er ændret.");
            var placement = new N.WindowPlacement { Length = Marshal.SizeOf(typeof(N.WindowPlacement)) };
            Check(N.GetWindowPlacement(window.Handle, ref placement));
            using (var process = Process.GetProcessById(window.ProcessId))
                _snapshot = new Snapshot { Window = window.Handle.ToInt64(), Host = host.ToInt64(), ProcessId = window.ProcessId,
                    StartTicks = process.StartTime.ToUniversalTime().Ticks, Owner = N.GetWindow(window.Handle, 4).ToInt64(),
                    Style = N.GetWindowLongPtr(window.Handle, -16).ToInt64(), ExtendedStyle = N.GetWindowLongPtr(window.Handle, -20).ToInt64(),
                    WasVisible = N.IsWindowVisible(window.Handle), Placement = placement };
            Directory.CreateDirectory(Path.GetDirectoryName(_recoveryFile));
            try { File.WriteAllText(_recoveryFile, JsonConvert.SerializeObject(_snapshot)); }
            catch { _snapshot = null; throw; }
            try
            {
                N.ShowWindow(window.Handle, 4);
                Set(window.Handle, -16, (_snapshot.Style & ~(0x00C00000L | 0x00040000L)) | 0x80000000L);
                Set(window.Handle, -8, host.ToInt64());
                Update(bounds);
                SetForegroundWindow(window.Handle); // Only called by the user's attach button.
            }
            catch { Release(); throw; }
        }
        public void Update(DesktopMirrorSession.Native.Rect bounds)
        {
            if (_snapshot == null) return;
            if (!SameWindow(_snapshot)) { Release(); return; }
            if (bounds.Width < 1 || bounds.Height < 1) return;
            using (new DesktopMirrorSession.Native.DpiScope())
            {
                var source = new IntPtr(_snapshot.Window);
                if (GetWindowRect(source, out var current) && current.Left == bounds.Left && current.Top == bounds.Top && current.Width == bounds.Width && current.Height == bounds.Height) return;
                Check(N.SetWindowPos(source, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x0004 | 0x0010 | 0x0020 | 0x0200));
                if (GetWindowRect(source, out var actual) && (Math.Abs(actual.Width - bounds.Width) > 3 || Math.Abs(actual.Height - bounds.Height) > 3))
                    throw new InvalidOperationException("Codex kunne ikke tilpasses panelets størrelse. Gør Revit-panelet større og prøv igen.");
            }
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
            var snapshot = JsonConvert.DeserializeObject<Snapshot>(File.ReadAllText(_recoveryFile));
            if (snapshot == null) throw new IOException("Gendannelsesfilen er ugyldig.");
            if (N.IsWindow(new IntPtr(snapshot.Host))) throw new InvalidOperationException("Det tidligere panel er stadig åbent. Frigør vinduet dér.");
            Restore(snapshot); File.Delete(_recoveryFile);
        }
        private static bool SameWindow(Snapshot snapshot)
        {
            IntPtr window = new IntPtr(snapshot.Window);
            if (!N.IsWindow(window)) return false;
            N.GetWindowThreadProcessId(window, out uint pid);
            if (pid != snapshot.ProcessId) return false;
            try { using (var p = Process.GetProcessById(snapshot.ProcessId)) return p.StartTime.ToUniversalTime().Ticks == snapshot.StartTicks; }
            catch (ArgumentException) { return false; }
        }
        private static void Restore(Snapshot snapshot)
        {
            if (!SameWindow(snapshot)) return;
            var window = new IntPtr(snapshot.Window);
            IntPtr owner = new IntPtr(snapshot.Owner);
            if (!N.IsWindow(owner)) owner = IntPtr.Zero;
            Set(window, -8, owner.ToInt64());
            // An owned modal may currently disable the app. Preserve that live state.
            long disabled = N.GetWindowLongPtr(window, -16).ToInt64() & 0x08000000L;
            Set(window, -16, (snapshot.Style & ~0x08000000L) | disabled); Set(window, -20, snapshot.ExtendedStyle);
            var placement = snapshot.Placement;
            placement.Length = Marshal.SizeOf(typeof(N.WindowPlacement));
            Check(N.SetWindowPlacement(window, ref placement));
            Check(N.SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020));
            N.ShowWindow(window, snapshot.WasVisible ? placement.ShowCommand : 0);
        }
        private static void Set(IntPtr window, int index, long value)
        {
            N.SetLastError(0);
            if (N.SetWindowLongPtr(window, index, new IntPtr(value)) == IntPtr.Zero && Marshal.GetLastWin32Error() != 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        private static void Check(bool value) { if (!value) throw new Win32Exception(Marshal.GetLastWin32Error()); }
        public sealed class Snapshot
        {
            public long Window, Host, Owner, Style, ExtendedStyle, StartTicks;
            public int ProcessId; public bool WasVisible; public N.WindowPlacement Placement;
        }
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out DesktopMirrorSession.Native.Rect bounds);
    }
}
