using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using N = revit_mcp_plugin.UI.DesktopWindowLease.Native;

namespace revit_mcp_plugin.UI
{
    // Position the original app without cross-process parenting OR ownership.
    // Both relationships can couple input queues. Preserve Chromium's native frame.
    public sealed class DesktopOverlayLease
    {
        private readonly string _recoveryFile;
        private Snapshot _snapshot;
        private DesktopMirrorSession.Native.Rect _lastBounds;
        private bool _hiddenByHost;
        public bool HasLease => _snapshot != null;
        public bool IsAttached => _snapshot != null && SameWindow(_snapshot) && N.IsWindow(new IntPtr(_snapshot.Host));
        public bool IsPaused { get; private set; }
        public string PausedReason { get; private set; } = "";
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
                    WasVisible = N.IsWindowVisible(window.Handle), Placement = placement, PreserveNativeWindow = true };
            Directory.CreateDirectory(Path.GetDirectoryName(_recoveryFile));
            try { File.WriteAllText(_recoveryFile, JsonConvert.SerializeObject(_snapshot)); }
            catch { _snapshot = null; throw; }
            try
            {
                // Restore from maximize/minimize, keeping all native frame/owner
                // behavior. Forcing WS_POPUP breaks compositor-based app windows.
                N.ShowWindow(window.Handle, 9);
                Update(bounds);
                if (!IsPaused) RaiseWithHost(window.Handle, host);
                // Activation can be refused by Windows; visibility is checked separately.
                SetForegroundWindow(window.Handle); // Only called by the user's attach button.
                CaptureDiagnostic("attached", null);
            }
            catch (Exception ex) { CaptureDiagnostic("attach_failed", ex.Message); Release(); throw; }
        }
        public void Resume(DesktopMirrorSession.Native.Rect bounds)
        {
            if (!IsAttached) throw new InvalidOperationException("Vinduet er lukket. Opdater listen.");
            var source = new IntPtr(_snapshot.Window);
            N.ShowWindow(source, 9); // Explicit user action only.
            _hiddenByHost = false;
            Update(bounds);
            if (!IsPaused) { RaiseWithHost(source, new IntPtr(_snapshot.Host)); SetForegroundWindow(source); }
        }
        public void Update(DesktopMirrorSession.Native.Rect bounds, bool allowZOrder = true)
        {
            if (_snapshot == null) return;
            if (!SameWindow(_snapshot) || !N.IsWindow(new IntPtr(_snapshot.Host))) { Release(); return; }
            var source = new IntPtr(_snapshot.Window);
            var host = new IntPtr(_snapshot.Host);
            var rootOwner = DesktopMirrorSession.Native.GetAncestor(host, 3);
            if (Unavailable(host) || (rootOwner != IntPtr.Zero && Unavailable(rootOwner)))
            {
                if (!_hiddenByHost && N.IsWindowVisible(source) && !DesktopMirrorSession.Native.IsIconic(source) && !IsZoomed(source))
                { N.ShowWindow(source, 0); _hiddenByHost = true; }
                Pause("Revit-panelet er skjult eller minimeret."); return;
            }
            bool resumedFromHost = _hiddenByHost;
            if (_hiddenByHost) { N.ShowWindow(source, 4); _hiddenByHost = false; }
            if (DesktopMirrorSession.Native.IsIconic(source)) { PauseSource("Codex er minimeret. Tryk Vis igen for at fortsætte."); return; }
            if (IsZoomed(source)) { PauseSource("Codex er maksimeret. Tryk Vis igen for at tilpasse det til panelet."); return; }
            if (!N.IsWindowVisible(source) || IsCloaked(source)) { PauseSource("Codex-vinduet er skjult. Tryk Vis igen eller åbn appen."); return; }
            IsPaused = false; PausedReason = "";
            if (bounds.Width < 1 || bounds.Height < 1) throw new InvalidOperationException("Panelet er ikke klar endnu. Åbn det og prøv igen.");
            _lastBounds = bounds;
            using (new DesktopMirrorSession.Native.DpiScope())
            {
                if (!GetWindowRect(source, out var current) || !Fits(current, bounds))
                {
                    uint previousDpi = GetDpiForWindow(source);
                    Check(N.SetWindowPos(source, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x0004 | 0x0010 | 0x0200));
                    Check(GetWindowRect(source, out current));
                    // Moving between monitors can make the app apply WM_DPICHANGED's
                    // suggested size. Reapply once at the new DPI, never loop on a
                    // genuine minimum-size constraint.
                    if (!Fits(current, bounds) && previousDpi != GetDpiForWindow(source))
                    {
                        Check(N.SetWindowPos(source, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x0004 | 0x0010 | 0x0200));
                        Check(GetWindowRect(source, out current));
                    }
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
                bool activeFamily = BelongsTo(foreground, host) || BelongsTo(host, foreground) || BelongsTo(foreground, source);
                if (allowZOrder && resumedFromHost) PlaceAboveHost(source, host);
                else if (allowZOrder && activeFamily && !IsVisiblyPlaced()) RaiseWithHost(source, host);
            }
        }
        private void Pause(string reason) { IsPaused = true; PausedReason = reason; }
        private void PauseSource(string reason)
        {
            var source = new IntPtr(_snapshot.Window);
            bool originalTopmost = (_snapshot.ExtendedStyle & 0x8) != 0;
            bool currentTopmost = (N.GetWindowLongPtr(source, -20).ToInt64() & 0x8) != 0;
            if (originalTopmost != currentTopmost)
                Check(N.SetWindowPos(source, new IntPtr(originalTopmost ? -1 : -2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0200));
            Pause(reason);
        }
        private static bool IsCloaked(IntPtr window) => DwmGetWindowAttribute(window, 14, out int cloaked, 4) == 0 && cloaked != 0;
        private static bool Unavailable(IntPtr window) => !N.IsWindowVisible(window) || DesktopMirrorSession.Native.IsIconic(window) || IsCloaked(window);
        private static void RaiseWithHost(IntPtr source, IntPtr host)
        {
            // Match the host's band only while placing the app; Release restores
            // the app's original band, so ordinary Revit stays ordinary as well.
            bool hostTopmost = (N.GetWindowLongPtr(host, -20).ToInt64() & 0x8) != 0;
            bool sourceTopmost = (N.GetWindowLongPtr(source, -20).ToInt64() & 0x8) != 0;
            IntPtr insertAfter = hostTopmost ? new IntPtr(-1) : sourceTopmost ? new IntPtr(-2) : IntPtr.Zero;
            Check(N.SetWindowPos(source, insertAfter, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040 | 0x0200));
        }
        private static void PlaceAboveHost(IntPtr source, IntPtr host)
        {
            // Re-show relative to the host, preserving any unrelated window above it.
            IntPtr previous = N.GetWindow(host, 3); // GW_HWNDPREV
            bool sameBand = (N.GetWindowLongPtr(source, -20).ToInt64() & 0x8) == (N.GetWindowLongPtr(host, -20).ToInt64() & 0x8);
            if (previous == source && sameBand) return;
            RaiseWithHost(source, host);
            if (previous != source && N.IsWindow(previous) && !BelongsTo(previous, source))
                Check(N.SetWindowPos(source, previous, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040 | 0x0200));
        }
        public bool IsVisiblyPlaced()
        {
            if (!IsAttached || IsPaused) return false;
            var source = new IntPtr(_snapshot.Window);
            if (Unavailable(source) || IsZoomed(source)) return false;
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
                        maximized = IsZoomed(source), owner = N.GetWindow(source, 4).ToInt64(), visuallyPlaced = IsVisiblyPlaced(),
                        IsPaused, PausedReason, sourceMetadata = Metadata(source), hostMetadata = Metadata(_snapshot == null ? IntPtr.Zero : new IntPtr(_snapshot.Host)),
                        foreground = DesktopMirrorSession.Native.GetForegroundWindow().ToInt64() }, Formatting.Indented);
                }
            }
            catch { /* Diagnostics must never prevent releasing the app. */ }
        }
        public void Release()
        {
            if (_snapshot == null) return;
            Restore(_snapshot);
            _snapshot = null;
            _hiddenByHost = false; IsPaused = false; PausedReason = "";
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
            // Old 2.6/2.6.1 recovery records did mutate owner/frame; new leases don't.
            if (!snapshot.PreserveNativeWindow)
            {
                Set(window, -8, owner.ToInt64());
                long disabled = N.GetWindowLongPtr(window, -16).ToInt64() & 0x08000000L;
                Set(window, -16, (snapshot.Style & ~0x08000000L) | disabled); Set(window, -20, snapshot.ExtendedStyle);
            }
            var placement = snapshot.Placement;
            placement.Length = Marshal.SizeOf(typeof(N.WindowPlacement));
            Check(N.SetWindowPlacement(window, ref placement));
            IntPtr originalBand = new IntPtr((snapshot.ExtendedStyle & 0x8) != 0 ? -1 : -2);
            Check(N.SetWindowPos(window, originalBand, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0020 | 0x0200));
            // SetWindowPlacement has restored the show state. Re-show without an
            // additional activating ShowWindow call during automatic panel cleanup.
            int show = snapshot.PreserveNativeWindow ? (placement.ShowCommand == 2 ? 7 : 8) : placement.ShowCommand;
            N.ShowWindow(window, snapshot.WasVisible ? show : 0);
        }
        private static object Metadata(IntPtr window)
        {
            var className = new System.Text.StringBuilder(256);
            GetClassName(window, className, className.Capacity);
            uint thread = N.GetWindowThreadProcessId(window, out uint pid);
            var gui = new GuiThreadInfo { Size = Marshal.SizeOf(typeof(GuiThreadInfo)) };
            bool available = thread != 0 && GetGUIThreadInfo(thread, ref gui);
            return new { hwnd = window.ToInt64(), pid, thread, className = className.ToString(),
                dpi = GetDpiForWindow(window),
                style = N.GetWindowLongPtr(window, -16).ToInt64().ToString("X"), extendedStyle = N.GetWindowLongPtr(window, -20).ToInt64().ToString("X"),
                visible = N.IsWindowVisible(window), minimized = DesktopMirrorSession.Native.IsIconic(window), cloaked = IsCloaked(window),
                guiAvailable = available, active = gui.Active.ToInt64(), focus = gui.Focus.ToInt64() };
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
            public int ProcessId; public bool WasVisible, PreserveNativeWindow; public N.WindowPlacement Placement;
        }
        [StructLayout(LayoutKind.Sequential)] private struct GuiThreadInfo
        { public int Size, Flags; public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret; public DesktopMirrorSession.Native.Rect CaretRect; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, System.Text.StringBuilder text, int capacity);
        [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out DesktopMirrorSession.Native.Rect bounds);
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetLastActivePopup(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(DesktopMirrorSession.Native.Point point);
    }
}
