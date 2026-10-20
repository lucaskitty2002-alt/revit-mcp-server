using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using N = revit_mcp_plugin.UI.DesktopMirrorSession.Native;

namespace revit_mcp_plugin.UI
{
    // WPF uses its HWND's cached DPI transform. Convert its screen coordinates
    // through that HWND, rather than mixing them with a PMv2 native resize call.
    public static class DesktopScreenGeometry
    {
        public static N.Rect GetBounds(FrameworkElement visual)
        {
            var source = PresentationSource.FromVisual(visual) as HwndSource;
            if (source == null || visual.ActualWidth < 1 || visual.ActualHeight < 1)
                throw new InvalidOperationException("Panelet skal være åbent, før Codex kan vises.");
            N.Point first, last;
            using (new N.DpiScope(DesktopWindowLease.Native.GetWindowDpiAwarenessContext(source.Handle)))
            {
                var a = visual.PointToScreen(new Point());
                var b = visual.PointToScreen(new Point(visual.ActualWidth, visual.ActualHeight));
                first = new N.Point { X = (int)Math.Round(a.X), Y = (int)Math.Round(a.Y) };
                last = new N.Point { X = (int)Math.Round(b.X), Y = (int)Math.Round(b.Y) };
                if (!LogicalToPhysicalPointForPerMonitorDPI(source.Handle, ref first) ||
                    !LogicalToPhysicalPointForPerMonitorDPI(source.Handle, ref last))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Skærmkoordinaterne kunne ikke omregnes.");
            }
            using (new N.DpiScope())
            {
                if (!N.GetClientRect(source.Handle, out var client)) throw new Win32Exception(Marshal.GetLastWin32Error());
                var origin = new N.Point();
                var end = new N.Point { X = client.Width, Y = client.Height };
                if (!N.ClientToScreen(source.Handle, ref origin) || !N.ClientToScreen(source.Handle, ref end))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                if (first.X < origin.X - 3 || first.Y < origin.Y - 3 || last.X > end.X + 3 || last.Y > end.Y + 3)
                    throw new InvalidOperationException("Panelets skærmkoordinater passer ikke til vinduet. Flyt panelet og prøv igen.");
            }
            return new N.Rect { Left = first.X, Top = first.Y, Right = last.X, Bottom = last.Y };
        }
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool LogicalToPhysicalPointForPerMonitorDPI(IntPtr window, ref N.Point point);
    }
}
