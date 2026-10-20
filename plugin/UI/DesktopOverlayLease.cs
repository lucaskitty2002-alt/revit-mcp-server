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
        private DesktopMirrorSession.Native.Rect _lastBounds;
        public bool HasLease => _snapshot != null;
        public bool IsAttached => _snapshot != null && SameWindow(_snapshot) && N.GetWindow(new IntPtr(_snapshot.Window), 4) == new IntPtr(_snapshot.Host);
        public string LastDiagnostic { get; private set; } = "";
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
                // Save the original show state above, then genuinely restore the app.
                // Reapplying the saved WS_MAXIMIZE/WS_MINIMIZE bits here undoes that
                // restore and makes a full-screen app reject the smaller panel bounds.
                N.ShowWindow(window.Handle, 9);
                long normalStyle = N.GetWindowLongPtr(window.Handle, -16).ToInt64();
                Set(window.Handle, -16, (normalStyle & ~(0x00C00000L | 0x00040000L | 0x01000000L | 0x20000000L)) | 0x80000000L);
                Set(window.Handle, -8, host.ToInt64());
                Update(bounds);
                RaiseWithHost(window.Handle, host);
                // Activation can be refused by Windows; visibility is checked separately.
                SetForegroundWindow(window.Handle); // Only called by the user's attach button.
                CaptureDiagnostic("attached", null);
            }
            catch (Exception ex) { CaptureDiagnostic("attach_failed", ex.Message); Release(); throw; }
        }
        public void Update(DesktopMirrorSession.Native.Rect bounds, bool allowZOrder = true)
        {
            if (_snapshot == null) return;
            if (!SameWindow(_snapshot)) { Release(); return; }
            if (bounds.Width < 1 || bounds.Height < 1) throw new InvalidOperationException("Panelet er ikke klar endnu. Åbn det og prøv igen.");
            _lastBounds = bounds;
            using (new DesktopMirrorSession.Native.DpiScope())
            {
                var source = new IntPtr(_snapshot.Window);
                if (!GetWindowRect(source, out var current) || !Fits(current, bounds))
                {
                    Check(N.SetWindowPos(source, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x0004 | 0x0010 | 0x0020 | 0x0200));
                    Check(GetWindowRect(source, out current));
                    if (!Fits(current, bounds))
                    {
                        CaptureDiagnostic("size_rejected", "Requested and actual window bounds differ.");
                        if (Math.Abs(current.Width - bounds.Width) <= 3 && Math.Abs(current.Height - bounds.Height) <= 3)
                            throw new InvalidOperationException("Windows flyttede ikke Codex hen til panelet. Frigør vinduet og prøv igen.");
                        throw new InvalidOperationException("Codex passer ikke i panelet. Ønsket " + bounds.Width + " × " + bounds.Height + " px, faktisk " + current.Width + " × " + current.Height + " px. Gør panelet større og prøv igen.");
                    }
                }
                // A docked/floating native host may cover an otherwise correctly sized
                // window. Raise only while this host/app family is in the foreground.
                IntPtr foreground = DesktopMirrorSession.Native.GetAncestor(DesktopMirrorSession.Native.GetForegroundWindow(), 2);
                if (allowZOrder && BelongsTo(foreground, new IntPtr(_snapshot.Host)) && !IsVisiblyPlaced())
                    RaiseWithHost(source, new IntPtr(_snapshot.Host));
            }
        }
        private static void RaiseWithHost(IntPtr source, IntPtr host)
        {
            // An owned top-level window does not automatically inherit a newly
            // assigned owner's topmost band. Match that host only; Release restores
            // the app's original band, so ordinary Revit stays ordinary as well.
            bool hostTopmost = (N.GetWindowLongPtr(host, -20).ToInt64() & 0x8) != 0;
            bool sourceTopmost = (N.GetWindowLongPtr(source, -20).ToInt64() & 0x8) != 0;
            IntPtr insertAfter = hostTopmost ? new IntPtr(-1) : sourceTopmost ? new IntPtr(-2) : IntPtr.Zero;
            Check(N.SetWindowPos(source, insertAfter, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040 | 0x0200));
        }
        public bool IsVisiblyPlaced()
        {
            if (!IsAttached) return false;
            var source = new IntPtr(_snapshot.Window);
            if (!N.IsWindowVisible(source) || DesktopMirrorSession.Native.IsIconic(source) || IsZoomed(source)) return false;
            using (new DesktopMirrorSession.Native.DpiScope())
            {
                if (!GetWindowRect(source, out var actual) || !Fits(actual, _lastBounds)) return false;
                // A native modal is a valid active part of the source app's UI.
                if (!IsWindowEnabled(source))
                {
                    IntPtr popup = GetLastActivePopup(source);
                    if (popup != source && N.IsWindowVisible(popup) && BelongsTo(popup, source)) return true;
                    bool ownedDialog = false;
                    N.EnumWindows((window, parameter) =>
                    {
                        if (window != source && N.IsWindowVisible(window) && IsWindowEnabled(window) && BelongsTo(window, source))
                        {
                            ownedDialog = true;
                            return false;
                        }
                        return true;
                    }, IntPtr.Zero);
                    return ownedDialog;
                }
                var point = new DesktopMirrorSession.Native.Point { X = (actual.Left + actual.Right) / 2, Y = (actual.Top + actual.Bottom) / 2 };
                return BelongsTo(DesktopMirrorSession.Native.GetAncestor(WindowFromPoint(point), 2), source);
            }
        }
        private static bool Fits(DesktopMirrorSession.Native.Rect actual, DesktopMirrorSession.Native.Rect expected) =>
            Math.Abs(actual.Left - expected.Left) <= 3 && Math.Abs(actual.Top - expected.Top) <= 3 &&
            Math.Abs(actual.Right - expected.Right) <= 3 && Math.Abs(actual.Bottom - expected.Bottom) <= 3;
        private static bool BelongsTo(IntPtr window, IntPtr root)
        {
            for (int i = 0; window != IntPtr.Zero && i < 24; i++, window = N.GetWindow(window, 4)) if (window == root) return true;
            return false;
        }
        public void CaptureDiagnostic(string action, string error)
        {
            try
            {
                var source = _snapshot == null ? IntPtr.Zero : new IntPtr(_snapshot.Window);
                using (new DesktopMirrorSession.Native.DpiScope())
                {
                    GetWindowRect(source, out var actual);
                    LastDiagnostic = JsonConvert.SerializeObject(new { version = typeof(DesktopOverlayLease).Assembly.GetName().Version.ToString(),
                        utc = DateTime.UtcNow, action, error, source = source.ToInt64(), host = _snapshot?.Host,
                        expected = _lastBounds, actual, visible = N.IsWindowVisible(source), minimized = DesktopMirrorSession.Native.IsIconic(source),
                        maximized = IsZoomed(source), owner = N.GetWindow(source, 4).ToInt64(), visuallyPlaced = IsVisiblyPlaced() }, Formatting.Indented);
                }
            }
            catch { /* Diagnostics must never prevent releasing the app. */ }
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
            IntPtr originalBand = new IntPtr((snapshot.ExtendedStyle & 0x8) != 0 ? -1 : -2);
            Check(N.SetWindowPos(window, originalBand, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0020 | 0x0200));
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
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetLastActivePopup(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(DesktopMirrorSession.Native.Point point);
    }
}
