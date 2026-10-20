using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace revit_mcp_plugin.UI
{
    // Local view-only GPU thumbnails. No SetParent, screen recording,
    // network listener, global input injection, or persistent keyboard/mouse hooks.
    public sealed class DesktopMirrorSession : IDisposable
    {
        private readonly IntPtr _destination, _source;
        private readonly uint _pid;
        private readonly long _startTicks;
        private readonly Dictionary<IntPtr, IntPtr> _thumbnails = new Dictionary<IntPtr, IntPtr>();
        private List<Surface> _surfaces = new List<Surface>();

        private Native.Rect _union, _view;
        private double _scale;
        private bool _disposed;
        public IntPtr Source => _source;
        public bool IsPaused { get; private set; }
        public int SurfaceCount => _surfaces.Count;

        private sealed class Surface { public IntPtr Window; public Native.Rect Bounds; }

        public DesktopMirrorSession(IntPtr destination, IntPtr source, int processId)
        {
            _destination = destination; _source = source; _pid = (uint)processId;
            if (!Native.IsWindow(destination) || Native.GetAncestor(destination, 2) != destination)
                throw new ArgumentException("Spejlet skal være i et synligt programvindue.");
            using (var process = Process.GetProcessById(processId)) _startTicks = process.StartTime.ToUniversalTime().Ticks;
            Validate();
        }

        private void Validate()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DesktopMirrorSession));
            Native.GetWindowThreadProcessId(_source, out uint pid);
            if (!Native.IsWindow(_source) || pid != _pid) throw new InvalidOperationException("Codex-vinduet er lukket. Vælg et vindue igen.");
            using (var process = Process.GetProcessById((int)_pid))
                if (process.StartTime.ToUniversalTime().Ticks != _startTicks) throw new InvalidOperationException("Det valgte program er genstartet.");
        }

        public void Update(Native.Rect viewport)
        {
            Validate();
            using (new Native.DpiScope())
            {
                IsPaused = Native.IsIconic(_source) || !Native.IsWindowVisible(_source);
                if (IsPaused || viewport.Width < 1 || viewport.Height < 1) { Hide(); return; }
                var surfaces = new List<Surface> { new Surface { Window = _source, Bounds = ClientBounds(_source) } };
                var popups = new List<Surface>();
                Native.EnumWindows((window, p) =>
                {
                    if (window == _source || !Native.IsWindowVisible(window) || Native.IsIconic(window)) return true;
                    Native.GetWindowThreadProcessId(window, out uint pid);
                    if (pid == _pid && IsRelated(window))
                    {
                        var bounds = ClientBounds(window);
                        if (bounds.Width > 0 && bounds.Height > 0) popups.Add(new Surface { Window = window, Bounds = bounds });
                    }
                    return true;
                }, IntPtr.Zero);
                popups.Reverse(); // EnumWindows is front to back; register back to front.
                surfaces.AddRange(popups);
                _union = surfaces[0].Bounds;
                foreach (var surface in surfaces.Skip(1)) _union = Native.Rect.Union(_union, surface.Bounds);
                if (_union.Width < 1 || _union.Height < 1) throw new InvalidOperationException("Vinduet har ingen synlig flade.");
                _view = Fit(viewport, _union.Width, _union.Height);
                _scale = Math.Min((double)_view.Width / _union.Width, (double)_view.Height / _union.Height);
                if (!_surfaces.Select(s => s.Window).SequenceEqual(surfaces.Select(s => s.Window))) ClearThumbnails();
                _surfaces = surfaces;
                foreach (var surface in surfaces)
                {
                    if (!_thumbnails.TryGetValue(surface.Window, out IntPtr thumbnail))
                    {
                        Marshal.ThrowExceptionForHR(Native.DwmRegisterThumbnail(_destination, surface.Window, out thumbnail));
                        _thumbnails.Add(surface.Window, thumbnail);
                    }
                    var destination = new Native.Rect
                    {
                        Left = _view.Left + (int)Math.Round((surface.Bounds.Left - _union.Left) * _scale),
                        Top = _view.Top + (int)Math.Round((surface.Bounds.Top - _union.Top) * _scale)
                    };
                    destination.Right = destination.Left + (int)Math.Round(surface.Bounds.Width * _scale);
                    destination.Bottom = destination.Top + (int)Math.Round(surface.Bounds.Height * _scale);
                    var properties = new Native.ThumbnailProperties { Flags = 1 | 4 | 8 | 16, Destination = destination, Opacity = 255, Visible = true, ClientOnly = true };
                    Marshal.ThrowExceptionForHR(Native.DwmUpdateThumbnailProperties(thumbnail, ref properties));
                }
            }
        }

        public static Native.Rect Fit(Native.Rect area, int width, int height)
        {
            if (width <= 0 || height <= 0 || area.Width <= 0 || area.Height <= 0) return new Native.Rect();
            double scale = Math.Min((double)area.Width / width, (double)area.Height / height);
            int w = Math.Max(1, (int)Math.Round(width * scale)), h = Math.Max(1, (int)Math.Round(height * scale));
            int x = area.Left + (area.Width - w) / 2, y = area.Top + (area.Height - h) / 2;
            return new Native.Rect { Left = x, Top = y, Right = x + w, Bottom = y + h };
        }

        private static Native.Rect ClientBounds(IntPtr window)
        {
            Native.GetClientRect(window, out var r);
            var point = new Native.Point(); Native.ClientToScreen(window, ref point);
            return new Native.Rect { Left = point.X, Top = point.Y, Right = point.X + r.Width, Bottom = point.Y + r.Height };
        }

        private bool IsRelated(IntPtr window)
        {
            for (int count = 0; window != IntPtr.Zero && count < 24; count++)
            {
                if (window == _source) return true;
                window = Native.GetWindow(window, 4); // GW_OWNER; exclude unrelated Codex chats/windows.
            }
            return false;
        }

        public void Hide()
        {
            foreach (var thumbnail in _thumbnails.Values)
            {
                var properties = new Native.ThumbnailProperties { Flags = 8, Visible = false };
                Native.DwmUpdateThumbnailProperties(thumbnail, ref properties);
            }
        }
        private void ClearThumbnails() { foreach (var thumbnail in _thumbnails.Values) Native.DwmUnregisterThumbnail(thumbnail); _thumbnails.Clear(); }
        public void Dispose() { if (_disposed) return; _disposed = true; ClearThumbnails(); }

        public static class Native
        {
            [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
            [StructLayout(LayoutKind.Sequential)] public struct Rect
            {
                public int Left, Top, Right, Bottom;
                public int Width => Right - Left; public int Height => Bottom - Top;
                public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
                public static Rect Union(Rect a, Rect b) => new Rect { Left = Math.Min(a.Left,b.Left), Top = Math.Min(a.Top,b.Top), Right = Math.Max(a.Right,b.Right), Bottom = Math.Max(a.Bottom,b.Bottom) };
            }
            [StructLayout(LayoutKind.Sequential)] public struct ThumbnailProperties
            {
                public uint Flags; public Rect Destination, Source; public byte Opacity;
                [MarshalAs(UnmanagedType.Bool)] public bool Visible;
                [MarshalAs(UnmanagedType.Bool)] public bool ClientOnly;
            }
public sealed class DpiScope : IDisposable
            {
                private readonly IntPtr _previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
                public void Dispose() { if (_previous != IntPtr.Zero) SetThreadDpiAwarenessContext(_previous); }
            }
            public delegate bool EnumProc(IntPtr window, IntPtr param);
            [DllImport("dwmapi.dll")] public static extern int DwmRegisterThumbnail(IntPtr destination, IntPtr source, out IntPtr thumbnail);
            [DllImport("dwmapi.dll")] public static extern int DwmUpdateThumbnailProperties(IntPtr thumbnail, ref ThumbnailProperties properties);
            [DllImport("dwmapi.dll")] public static extern int DwmUnregisterThumbnail(IntPtr thumbnail);
            [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr window);
            [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
            [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr window);
            [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr window, out Rect rect);
            [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr window, ref Point point);
            [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr window, ref Point point);
            [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr window, uint flags);
            [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr window, uint flags);
            [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
            [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr parameter);
            [DllImport("user32.dll")] public static extern IntPtr ChildWindowFromPointEx(IntPtr parent, Point point, uint flags);
            [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
            [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        }
    }
}
