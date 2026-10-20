using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace revit_mcp_plugin.UI
{
    // Optional view-only preview. Full interaction uses the real top-level overlay.
    public sealed class CodexMirrorSurface : FrameworkElement
    {
        private DesktopMirrorSession _session;
        private HwndSource _host;
        private readonly DispatcherTimer _timer;
        public event Action<string> Status;
        public bool IsSharing => _session != null;
        public CodexMirrorSurface()
        {
            ClipToBounds = true;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _timer.Tick += (s, e) => Refresh();
            Unloaded += (s, e) => Stop();
            IsVisibleChanged += (s, e) => { if (!(bool)e.NewValue) Stop(); };
        }
        public void Start(CodexDesktopWindow window)
        {
            Stop();
            _host = PresentationSource.FromVisual(this) as HwndSource;
            if (_host == null) throw new InvalidOperationException("Åbn panelet før du starter forhåndsvisningen.");
            _session = new DesktopMirrorSession(DesktopMirrorSession.Native.GetAncestor(_host.Handle, 2), window.Handle, window.ProcessId);
            _timer.Start(); Refresh();
        }
        public void Stop()
        {
            _timer.Stop(); _session?.Dispose(); _session = null; _host = null; InvalidateVisual();
        }
        private void Refresh()
        {
            if (_session == null || ActualWidth < 2 || ActualHeight < 2) return;
            try
            {
                var a = PointToScreen(new Point()); var b = PointToScreen(new Point(ActualWidth,ActualHeight));
                var top = new DesktopMirrorSession.Native.Point { X = (int)a.X, Y = (int)a.Y };
                var bottom = new DesktopMirrorSession.Native.Point { X = (int)b.X, Y = (int)b.Y };
                using(new DesktopMirrorSession.Native.DpiScope())
                {
                    var root=DesktopMirrorSession.Native.GetAncestor(_host.Handle,2);
                    DesktopMirrorSession.Native.ScreenToClient(root,ref top); DesktopMirrorSession.Native.ScreenToClient(root,ref bottom);
                }
                _session.Update(new DesktopMirrorSession.Native.Rect { Left=top.X,Top=top.Y,Right=bottom.X,Bottom=bottom.Y });
                Status?.Invoke(_session.IsPaused ? "Forhåndsvisning pauset · gendan Codex-vinduet." : "Levende forhåndsvisning · vælg Vis Codex i Revit for at betjene hele appen.");
            }
            catch(Exception ex) { Stop(); Status?.Invoke(ex.Message); }
        }
        protected override void OnRender(DrawingContext drawing)
        {
            base.OnRender(drawing);
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(25,25,25)),null,new Rect(0,0,ActualWidth,ActualHeight));
            if(_session == null)
            {
                var text=new FormattedText("Vælg Codex-vinduet og tryk Vis Codex i Revit.",System.Globalization.CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,new Typeface("Segoe UI"),15,Brushes.White,VisualTreeHelper.GetDpi(this).PixelsPerDip);
                drawing.DrawText(text,new Point(18,20));
            }
        }
    }
}
