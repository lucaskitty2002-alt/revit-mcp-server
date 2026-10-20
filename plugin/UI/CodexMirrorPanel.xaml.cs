using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace revit_mcp_plugin.UI
{
    public partial class CodexMirrorPanel : UserControl
    {
        private readonly DesktopOverlayLease _overlay = new DesktopOverlayLease(Path.Combine(CodexSettings.SettingsDirectory,"overlay-window-recovery.json"));
        private readonly DispatcherTimer _timer;
        private Window _hostingWindow;
        public CodexMirrorPanel()
        {
            InitializeComponent();
            Surface.Status += text => StatusText.Text = text;
            _timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(150)};
            _timer.Tick+=(s,e)=>{
                try { if(_overlay.IsAttached) _overlay.Update(ScreenBounds()); }
                catch(Exception ex) { try {Stop();} catch{} StatusText.Text=ex.Message; }
            };
        }
        private void Panel_Loaded(object sender,RoutedEventArgs e)
        {
            if(_hostingWindow!=null)_hostingWindow.Closing-=Host_Closing;
            _hostingWindow=Window.GetWindow(this);
            if(_hostingWindow!=null)_hostingWindow.Closing+=Host_Closing;
            if(WindowPicker.ItemsSource==null)Refresh();
        }
        private void Host_Closing(object sender,System.ComponentModel.CancelEventArgs e)
        {
            try{Stop();}catch(Exception ex){e.Cancel=true;StatusText.Text="Frigør Codex før panelet lukkes: "+ex.Message;}
        }
        private void Panel_Unloaded(object sender,RoutedEventArgs e)
        {
            if(_hostingWindow!=null){_hostingWindow.Closing-=Host_Closing;_hostingWindow=null;}
            try{Stop();}catch(Exception ex){StatusText.Text=ex.Message;}
        }
        private void Panel_IsVisibleChanged(object sender,DependencyPropertyChangedEventArgs e)
        {
            if(!(bool)e.NewValue)try{Stop();}catch(Exception ex){StatusText.Text=ex.Message;}
        }
        private void Refresh()
        {
            try
            {
                if(_overlay.IsAttached)Stop();
                var windows=DesktopWindowLease.FindCodexWindows(); WindowPicker.ItemsSource=windows;
                if(windows.Count>0)WindowPicker.SelectedIndex=0;
                if(!_overlay.IsAttached&&!Surface.IsSharing)StatusText.Text=windows.Count>0?"Vælg vinduet og tryk Vis Codex i Revit.":"Intet Codex-vindue fundet. Åbn appen først.";
            }
            catch(Exception ex){StatusText.Text=ex.Message;}
        }
        private DesktopMirrorSession.Native.Rect ScreenBounds()
        {
            var a=Surface.PointToScreen(new Point());var b=Surface.PointToScreen(new Point(Surface.ActualWidth,Surface.ActualHeight));
            return new DesktopMirrorSession.Native.Rect {Left=(int)a.X,Top=(int)a.Y,Right=(int)b.X,Bottom=(int)b.Y};
        }
        private void Refresh_Click(object sender,RoutedEventArgs e)=>Refresh();
        private void Open_Click(object sender,RoutedEventArgs e){try{DesktopWindowLease.LaunchCodex();}catch(Exception ex){StatusText.Text=ex.Message;}}
        private void Start_Click(object sender,RoutedEventArgs e)
        {
            if(!(WindowPicker.SelectedItem is CodexDesktopWindow window)){StatusText.Text="Vælg et vindue først.";return;}
            try
            {
                Stop();
                var host=PresentationSource.FromVisual(this) as HwndSource;
                if(host==null)throw new InvalidOperationException("Åbn panelet først.");
                _overlay.Attach(window,DesktopMirrorSession.Native.GetAncestor(host.Handle,2),ScreenBounds());
                _timer.Start();StatusText.Text="Det originale Codex-vindue er placeret her. Brug appen normalt; frigør den med Tilbage til skrivebord.";
            }
            catch(Exception ex){StatusText.Text=ex.Message;}
        }
        private void Preview_Click(object sender,RoutedEventArgs e)
        {
            if(!(WindowPicker.SelectedItem is CodexDesktopWindow window))return;
            try{Stop();Surface.Start(window);}catch(Exception ex){StatusText.Text=ex.Message;}
        }
        private void Recover_Click(object sender,RoutedEventArgs e)
        {
            try{Stop();_overlay.Recover();StatusText.Text="Den oprindelige vinduesplacering er gendannet.";}catch(Exception ex){StatusText.Text=ex.Message;}
        }
        private void Stop_Click(object sender,RoutedEventArgs e){try{Stop();StatusText.Text="Codex er tilbage på skrivebordet.";}catch(Exception ex){StatusText.Text=ex.Message;}}
        public void Stop(){_timer.Stop();Surface.Stop();_overlay.Release();}
    }
}
