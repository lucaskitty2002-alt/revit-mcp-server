using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Newtonsoft.Json.Linq;
using revit_mcp_plugin.UI;
using N = revit_mcp_plugin.UI.DesktopMirrorSession.Native;
using Forms = System.Windows.Forms;

internal static class Program
{
    [STAThread] static void Main(string[] args)
    {
        if (args.FirstOrDefault() == "--fixture") { Fixture(args[1]); return; }
        if (args.FirstOrDefault() == "--preview") { Preview(args[1]); return; }
        if (args.FirstOrDefault() == "--dpi-host-probe") { ProbeSystemAwareHost(); return; }
        Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); Native.SetThreadDpiAwarenessContext(new IntPtr(-4));
        if(args.FirstOrDefault()=="--monitors")
        {
            foreach(var monitor in Monitors())Console.WriteLine("Monitor "+monitor.Handle+" DPI="+monitor.Dpi+" work="+BoundsText(monitor.Work));
            return;
        }
        string root = Path.Combine(Path.GetTempPath(), "revit-mirror-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var child = Process.Start(new ProcessStartInfo(Path.ChangeExtension(Assembly.GetExecutingAssembly().Location, ".exe"), "--fixture \"" + root + "\"") { UseShellExecute = false, CreateNoWindow = true });
        var app = new System.Windows.Application();
        var surface = new CodexMirrorSurface();
        // The user may be working in another app. Keep only our controlled test
        // surface visible without depending on permission to acquire foreground.
        var window = new Window { Title = "Revit mirror · controlled test", Width = 720, Height = 580, Left = 650, Top = 50, Content = surface, Topmost = true, ShowActivated = false };
        Exception failure = null;
        window.ContentRendered += async (s,e) => {
            try
            {
                await Until(() => File.Exists(Path.Combine(root,"handles.json")), "fixture startup");
                var handles=JObject.Parse(File.ReadAllText(Path.Combine(root,"handles.json")));
                var source = new IntPtr((long)handles["window"]);
                IntPtr originalParent=Native.GetParent(source);
                IntPtr style=Native.GetWindowLongPtr(source,-16);
                IntPtr extendedStyle=Native.GetWindowLongPtr(source,-20);
                await Task.Delay(250);
                Check(N.IsWindowVisible(new WindowInteropHelper(window).Handle)&&!N.IsIconic(new WindowInteropHelper(window).Handle),"test destination visible without requiring foreground");
                Check((Native.GetWindowLongPtr(new WindowInteropHelper(window).Handle,-20).ToInt64()&0x8)!=0,"controlled destination uses a temporary topmost band");
                surface.Start(new CodexDesktopWindow { Handle=source,ProcessId=child.Id,Title="Controlled fixture" });
                await Task.Delay(400);
                var session=(DesktopMirrorSession)typeof(CodexMirrorSurface).GetField("_session",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(surface);
                Check(session != null && session.SurfaceCount==1,"GPU thumbnail relationship");
                Check(Native.GetParent(source)==originalParent && Native.GetWindowLongPtr(source,-16)==style,"source parent/styles unchanged across different DPI modes");
                var vp=Native.GetWindowDpiAwarenessContext(source); var hp=Native.GetWindowDpiAwarenessContext(new WindowInteropHelper(window).Handle);
                Check(!Native.AreDpiAwarenessContextsEqual(vp,hp),"fixture actually has a different DPI mode");
                var a=surface.PointToScreen(new System.Windows.Point()); var b=surface.PointToScreen(new System.Windows.Point(surface.ActualWidth,surface.ActualHeight));
                var origin=new N.Point{X=(int)a.X,Y=(int)a.Y}; using(new N.DpiScope()) N.ScreenToClient(new WindowInteropHelper(window).Handle,ref origin);
                var area=new N.Rect {Left=origin.X,Top=origin.Y,Right=origin.X+(int)(b.X-a.X),Bottom=origin.Y+(int)(b.Y-a.Y)};
                string image=Path.Combine(root,"controlled-mirror.png");
                using(var bitmap=new System.Drawing.Bitmap((int)(b.X-a.X),(int)(b.Y-a.Y)))
                {
                    using(var graphics=System.Drawing.Graphics.FromImage(bitmap)) graphics.CopyFromScreen((int)a.X,(int)a.Y,0,0,bitmap.Size);
                    bitmap.Save(image);
                    int teal=0;
                    for(int row=0;row<bitmap.Height;row+=8) for(int col=0;col<bitmap.Width;col+=8)
                    {var pixel=bitmap.GetPixel(col,row);if(pixel.G>80&&pixel.B>70&&pixel.R<50)teal++;}
                    Check(teal>100,"actual composed mirror pixels, not an empty black surface");
                }
                Console.WriteLine("Controlled mirror screenshot: "+image);
                surface.Stop();
                Check(!surface.IsSharing&&!child.HasExited&&N.IsWindow(source),"stop leaves source process/window alive");
                var recovery=Path.Combine(root,"overlay-recovery.json");
                var overlay=new DesktopOverlayLease(recovery);
                bool modalVisiblyPlaced=false;
                var screenBounds=new N.Rect {Left=(int)a.X,Top=(int)a.Y,Right=(int)b.X,Bottom=(int)b.Y};
                try
                {
                    overlay.Attach(new CodexDesktopWindow {Handle=source,ProcessId=child.Id,Title="Controlled fixture"},new WindowInteropHelper(window).Handle,screenBounds);
                    Check(overlay.IsAttached && N.GetAncestor(source,2)==source,"original source remains a top-level window");
                    Check(N.GetWindow(source,4)==originalParent,"placing source preserves its original native owner");
                    Check(SameFrameStyle(Native.GetWindowLongPtr(source,-16).ToInt64(),style.ToInt64()),"placing source preserves its original native frame styles");
                    Check((Native.GetWindowLongPtr(source,-16).ToInt64()&0x40000000L)==0,"source never becomes a WS_CHILD window");
                    Check(Native.AreDpiAwarenessContextsEqual(vp,Native.GetWindowDpiAwarenessContext(source)),"overlay preserves source DPI context");
                    using(new N.DpiScope())
                    {
                        Native.GetWindowRect(source,out var placed);
                        Check(Math.Abs(placed.Left-screenBounds.Left)<3 && Math.Abs(placed.Width-screenBounds.Width)<3,"original window fits the host panel in physical pixels");
                    }
                    await Task.Delay(250);
                    Check(overlay.IsVisiblyPlaced(),"normal overlay reports verified visible placement");
                    VerifyOverlayPixels(source,screenBounds,Path.Combine(root,"controlled-overlay-normal.png"));
                    await VerifyPauseLifecycle(overlay,source,window,screenBounds,root,(extendedStyle.ToInt64()&0x8)!=0);
                    File.WriteAllText(Path.Combine(root,"open-modal.txt"),"open");
                    await Until(()=>File.Exists(Path.Combine(root,"popup.txt")),"normal source control opens its owned modal");
                    Check((Native.GetWindowLongPtr(source,-16).ToInt64()&0x08000000L)!=0,"modal disables its real owner normally");
                    await Task.Delay(250);
                    modalVisiblyPlaced=overlay.IsVisiblyPlaced();
                    if(!modalVisiblyPlaced)
                    {
                        overlay.CaptureDiagnostic("controlled_modal_visibility",null);
                        Console.WriteLine("Owned modal visibility diagnostic: "+overlay.LastDiagnostic);
                        var popup=new IntPtr(long.Parse(File.ReadAllText(Path.Combine(root,"popup.txt"))));
                        Console.WriteLine("Controlled popup="+popup+" visible="+N.IsWindowVisible(popup)+" owner="+N.GetWindow(popup,4)+" lastActive="+Native.GetLastActivePopup(source)+" foreground="+N.GetForegroundWindow());
                    }
                    overlay.Release();
                    Check((Native.GetWindowLongPtr(source,-16).ToInt64()&0x08000000L)!=0,"release preserves a live modal's disabled-owner state");
                    File.WriteAllText(Path.Combine(root,"close-modal.txt"),"close");
                    await Until(()=>(Native.GetWindowLongPtr(source,-16).ToInt64()&0x08000000L)==0,"modal closes through the source app's normal UI loop");
                    Check(Native.GetParent(source)==originalParent && Native.GetWindowLongPtr(source,-16)==style && Native.GetWindowLongPtr(source,-20)==extendedStyle && !File.Exists(recovery),"release restores owner, styles including topmost state, and recovery state");
                }
                finally {overlay.Release();}
                await VerifyWindowStateTransitions(source,child.Id,new WindowInteropHelper(window).Handle,screenBounds,root);
                Check(modalVisiblyPlaced,"visible owned modal counts as the original app interface");
                Console.WriteLine("PASS GPU mirror, original-window overlay, DPI independence, native modal lifetime, restoration and cleanup. No user apps were operated.");
            }
            catch(Exception ex) { failure=ex; }
            finally { surface.Stop(); window.Close(); }
        };
        try { app.Run(window); }
        finally { if(!child.HasExited) child.Kill(); child.Dispose(); }
        if(failure!=null) throw failure;
    }
    static void ProbeSystemAwareHost()
    {
        Native.SetProcessDpiAwarenessContext(new IntPtr(-2));Native.SetThreadDpiAwarenessContext(new IntPtr(-2));
        var monitors=Monitors();var destination=monitors.OrderByDescending(m=>m.Work.Left).First();
        if(monitors.Count<2)Console.WriteLine("SKIP secondary-monitor transition: only one display is currently available.");
        var surface=new System.Windows.Controls.Border {Background=System.Windows.Media.Brushes.Teal};
        var app=new System.Windows.Application();
        var window=new Window {Title="Controlled SystemAware DPI probe",Width=900,Height=700,Topmost=true,ShowActivated=false,Content=surface};
        window.ContentRendered+=async(s,e)=>
        {
            try
            {
                IntPtr hwnd=new WindowInteropHelper(window).Handle;
                using(new N.DpiScope())DesktopWindowLease.Native.SetWindowPos(hwnd,IntPtr.Zero,destination.Work.Left+60,destination.Work.Top+60,1200,850,0x0004|0x0010);
                await Task.Delay(350);
                var physical=DesktopScreenGeometry.GetBounds(surface);
                var first=surface.PointToScreen(new System.Windows.Point());var last=surface.PointToScreen(new System.Windows.Point(surface.ActualWidth,surface.ActualHeight));
                var logicalFirst=new N.Point {X=(int)first.X,Y=(int)first.Y};var logicalLast=new N.Point {X=(int)last.X,Y=(int)last.Y};
                Native.LogicalToPhysicalPointForPerMonitorDPI(hwnd,ref logicalFirst);Native.LogicalToPhysicalPointForPerMonitorDPI(hwnd,ref logicalLast);
                Console.WriteLine("SystemAware hostDpi="+Native.GetDpiForWindow(hwnd)+" monitorDpi="+destination.Dpi+" actualDIP="+surface.ActualWidth+"x"+surface.ActualHeight);
                Console.WriteLine("default PointToScreen="+first+" -> "+last);
                Console.WriteLine("converted default="+logicalFirst.X+","+logicalFirst.Y+" -> "+logicalLast.X+","+logicalLast.Y);
                using(new N.DpiScope())
                {
                    var pmFirst=surface.PointToScreen(new System.Windows.Point());var pmLast=surface.PointToScreen(new System.Windows.Point(surface.ActualWidth,surface.ActualHeight));
                    Native.GetWindowRect(hwnd,out var outer);N.GetClientRect(hwnd,out var client);
                    var nativeFirst=new N.Point();var nativeLast=new N.Point {X=client.Width,Y=client.Height};N.ClientToScreen(hwnd,ref nativeFirst);N.ClientToScreen(hwnd,ref nativeLast);
                    Check(SameBounds(physical,new N.Rect {Left=nativeFirst.X,Top=nativeFirst.Y,Right=nativeLast.X,Bottom=nativeLast.Y}),"SystemAware WPF surface converts to exact physical native client bounds");
                    Console.WriteLine("PMv2 PointToScreen="+pmFirst+" -> "+pmLast);
                    Console.WriteLine("PMv2 native outer="+BoundsText(outer)+" client="+BoundsText(client)+" screenclient="+nativeFirst.X+","+nativeFirst.Y+" -> "+nativeLast.X+","+nativeLast.Y);
                }
            }
            finally {window.Close();}
        };
        app.Run(window);
    }
    static async Task VerifyPauseLifecycle(DesktopOverlayLease overlay,IntPtr source,Window hostWindow,N.Rect bounds,string root,bool originalTopmost)
    {
        IntPtr host=new WindowInteropHelper(hostWindow).Handle;
        try
        {
            hostWindow.WindowState=WindowState.Minimized;
            await Until(()=>N.IsIconic(host),"controlled host is minimized");
            for(int attempt=0;attempt<3;attempt++){overlay.Update(bounds);await Task.Delay(60);}
            Check(overlay.HasLease&&overlay.IsAttached&&overlay.IsPaused&&!string.IsNullOrWhiteSpace(overlay.PausedReason),"host minimize pauses placement while retaining the lease");
            Check(N.IsIconic(host),"paused update never restores the minimized host");
            Check(!N.IsWindowVisible(source)&&!N.IsIconic(source),"host minimize temporarily hides source without minimizing it");
            hostWindow.WindowState=WindowState.Normal;
            await Until(()=>!N.IsIconic(host)&&N.IsWindowVisible(host),"controlled host is restored");
            overlay.Update(bounds);
            await Task.Delay(250);
            Check(overlay.HasLease&&!overlay.IsPaused&&N.IsWindowVisible(source)&&!N.IsIconic(source),"host restore resumes the original source window");
            if(!overlay.IsVisiblyPlaced())
            {
                overlay.CaptureDiagnostic("controlled_host_restore",null);
                Console.WriteLine("Host restore visibility diagnostic: "+overlay.LastDiagnostic);
            }
            Check(overlay.IsVisiblyPlaced(),"restored host recovers verified overlay visibility");
            VerifyOverlayPixels(source,bounds,Path.Combine(root,"controlled-overlay-host-restored.png"));

            Native.ShowWindow(source,6);
            await Until(()=>N.IsIconic(source),"controlled source is independently minimized");
            for(int attempt=0;attempt<3;attempt++){overlay.Update(bounds);await Task.Delay(60);}
            Check(overlay.HasLease&&overlay.IsAttached&&overlay.IsPaused&&!string.IsNullOrWhiteSpace(overlay.PausedReason),"source minimize pauses placement while retaining the lease");
            Check(N.IsIconic(source),"paused update never forces a minimized source to restore");
            Check(((Native.GetWindowLongPtr(source,-20).ToInt64()&0x8)!=0)==originalTopmost,"source minimize pause restores its original topmost band");
            overlay.Resume(bounds);
            await Task.Delay(250);
            Check(overlay.HasLease&&!overlay.IsPaused&&!N.IsIconic(source)&&overlay.IsVisiblyPlaced(),"explicit resume restores the source and verifies placement");
            VerifyOverlayPixels(source,bounds,Path.Combine(root,"controlled-overlay-source-resumed.png"));

            Native.ShowWindow(source,3);
            await Until(()=>Native.IsZoomed(source),"controlled source is independently maximized");
            overlay.Update(bounds);
            Check(overlay.HasLease&&overlay.IsAttached&&overlay.IsPaused&&Native.IsZoomed(source),"source maximize pauses placement and preserves the user show state");
            Check(((Native.GetWindowLongPtr(source,-20).ToInt64()&0x8)!=0)==originalTopmost,"source maximize pause restores its original topmost band");
            overlay.Resume(bounds);
            await Task.Delay(250);
            Check(!overlay.IsPaused&&!Native.IsZoomed(source)&&overlay.IsVisiblyPlaced(),"explicit resume after maximize restores visible panel placement");
            VerifyOverlayPixels(source,bounds,Path.Combine(root,"controlled-overlay-source-max-resumed.png"));
        }
        finally {hostWindow.WindowState=WindowState.Normal;}
    }
    static bool SameFrameStyle(long a,long b)=>(a&~(0x01000000L|0x20000000L))==(b&~(0x01000000L|0x20000000L));
    static async Task VerifyWindowStateTransitions(IntPtr source,int processId,IntPtr host,N.Rect bounds,string root)
    {
        // Only this test's own fixture is operated. Real app windows are never selected here.
        var failures=new List<Exception>();
        foreach(int showCommand in new[]{3,2})
        {
            string state=showCommand==3?"maximized":"minimized";
            string recovery=Path.Combine(root,"overlay-"+state+"-recovery.json");
            var overlay=new DesktopOverlayLease(recovery);
            try
            {
                Native.ShowWindow(source,showCommand);
                await Until(()=>showCommand==3?Native.IsZoomed(source):N.IsIconic(source),state+" fixture show state");
                var original=new DesktopWindowLease.Native.WindowPlacement {Length=Marshal.SizeOf(typeof(DesktopWindowLease.Native.WindowPlacement))};
                Check(DesktopWindowLease.Native.GetWindowPlacement(source,ref original),"read "+state+" placement");
                long originalStyle=Native.GetWindowLongPtr(source,-16).ToInt64();
                long originalExtendedStyle=Native.GetWindowLongPtr(source,-20).ToInt64();
                IntPtr originalOwner=N.GetWindow(source,4);
                overlay.Attach(new CodexDesktopWindow {Handle=source,ProcessId=processId,Title="Controlled "+state+" fixture"},host,bounds);
                await Task.Delay(250);
                Check(overlay.IsAttached&&!Native.IsZoomed(source)&&!N.IsIconic(source),state+" source is restored to a normal interactive window while placed");
                Check(overlay.IsVisiblyPlaced(),state+" overlay reports verified visible placement");
                Check(N.GetWindow(source,4)==originalOwner&&SameFrameStyle(Native.GetWindowLongPtr(source,-16).ToInt64(),originalStyle),state+" placement preserves native owner and frame styles");
                Check(N.GetAncestor(source,2)==source&&(Native.GetWindowLongPtr(source,-16).ToInt64()&0x40000000L)==0,state+" source remains top-level");
                using(new N.DpiScope())
                {
                    Check(Native.GetWindowRect(source,out var placed),"read placed "+state+" physical bounds");
                    Console.WriteLine(state+" requested="+BoundsText(bounds)+" actual="+BoundsText(placed));
                    Check(SameBounds(placed,bounds),state+" source fits all four host bounds in physical pixels");
                }
                VerifyOverlayPixels(source,bounds,Path.Combine(root,"controlled-overlay-"+state+".png"));
                overlay.Release();
                await Until(()=>showCommand==3?Native.IsZoomed(source):N.IsIconic(source),state+" original show state restored");
                var restored=new DesktopWindowLease.Native.WindowPlacement {Length=Marshal.SizeOf(typeof(DesktopWindowLease.Native.WindowPlacement))};
                Check(DesktopWindowLease.Native.GetWindowPlacement(source,ref restored),"read restored "+state+" placement");
                Check(restored.ShowCommand==original.ShowCommand&&
                    Math.Abs(restored.NormalPosition.Left-original.NormalPosition.Left)<3&&
                    Math.Abs(restored.NormalPosition.Top-original.NormalPosition.Top)<3&&
                    Math.Abs(restored.NormalPosition.Right-original.NormalPosition.Right)<3&&
                    Math.Abs(restored.NormalPosition.Bottom-original.NormalPosition.Bottom)<3,
                    state+" release restores saved show command and normal placement");
                Check(Native.GetWindowLongPtr(source,-16).ToInt64()==originalStyle&&Native.GetWindowLongPtr(source,-20).ToInt64()==originalExtendedStyle&&!File.Exists(recovery),state+" release restores original styles including topmost state and removes recovery record");
            }
            catch(Exception ex)
            {
                failures.Add(new Exception(state+" overlay regression failed: "+ex.Message,ex));
                Console.WriteLine("FAIL "+state+" overlay regression: "+ex.Message);
            }
            finally {overlay.Release();Native.ShowWindow(source,9);}
        }
        if(failures.Count>0)throw new AggregateException("Window-state overlay regressions failed.",failures);
    }
    static bool SameBounds(N.Rect a,N.Rect b)=>Math.Abs(a.Left-b.Left)<3&&Math.Abs(a.Top-b.Top)<3&&Math.Abs(a.Right-b.Right)<3&&Math.Abs(a.Bottom-b.Bottom)<3;
    sealed class MonitorData {public IntPtr Handle;public N.Rect Work;public uint Dpi;}
    static List<MonitorData> Monitors()
    {
        var monitors=new List<MonitorData>();
        Native.EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,(IntPtr handle,IntPtr dc,ref N.Rect rectangle,IntPtr data)=>
        {
            var info=new Native.MonitorInfo {Size=Marshal.SizeOf(typeof(Native.MonitorInfo))};
            if(Native.GetMonitorInfo(handle,ref info)&&Native.GetDpiForMonitor(handle,0,out uint x,out _)==0)monitors.Add(new MonitorData {Handle=handle,Work=info.Work,Dpi=x});
            return true;
        },IntPtr.Zero);
        return monitors;
    }
    static string BoundsText(N.Rect r)=>r.Left+","+r.Top+","+r.Width+"x"+r.Height;
    static void VerifyOverlayPixels(IntPtr source,N.Rect bounds,string image)
    {
        using(new N.DpiScope())
        {
            var center=new N.Point {X=(bounds.Left+bounds.Right)/2,Y=(bounds.Top+bounds.Bottom)/2};
            Check(N.GetAncestor(Native.WindowFromPoint(center),2)==source,"original overlay receives the center-point hit test");
            using(var bitmap=new System.Drawing.Bitmap(bounds.Width,bounds.Height))
            {
                using(var graphics=System.Drawing.Graphics.FromImage(bitmap))graphics.CopyFromScreen(bounds.Left,bounds.Top,0,0,bitmap.Size);
                bitmap.Save(image);
                int teal=0;
                for(int row=0;row<bitmap.Height;row+=8)for(int col=0;col<bitmap.Width;col+=8)
                {var pixel=bitmap.GetPixel(col,row);if(pixel.G>80&&pixel.B>70&&pixel.R<50)teal++;}
                Check(teal>100,"actual original overlay pixels are visible over the host panel");
            }
        }
        Console.WriteLine("Controlled original overlay screenshot: "+image);
    }
    static async Task Until(Func<bool> test,string label)
    {
        var end=DateTime.UtcNow.AddSeconds(8);
        while(DateTime.UtcNow<end){try{if(test()){Console.WriteLine("PASS "+label);return;}}catch(IOException){} await Task.Delay(100);}
        throw new Exception("FAIL "+label);
    }
    static void Check(bool ok,string label){if(!ok)throw new Exception("FAIL "+label);Console.WriteLine("PASS "+label);}
    static void Fixture(string root)
    {
        Native.SetProcessDpiAwarenessContext(new IntPtr(-1)); Native.SetThreadDpiAwarenessContext(new IntPtr(-1));
        var form=new Forms.Form { Text="Controlled mirror source",StartPosition=Forms.FormStartPosition.Manual,Location=new System.Drawing.Point(40,50),ClientSize=new System.Drawing.Size(500,400),BackColor=System.Drawing.Color.Teal };
        var text=new Forms.TextBox { Location=new System.Drawing.Point(20,70),Size=new System.Drawing.Size(440,50),Multiline=true };
        var button=new Forms.Button { Text="Open controlled modal",Location=new System.Drawing.Point(20,160),Size=new System.Drawing.Size(230,45) };
        text.TextChanged+=(s,e)=>File.WriteAllText(Path.Combine(root,"text.txt"),text.Text);
        Forms.Form dialog=null;bool opened=false;
        button.Click+=(s,e)=>{
            using(var modal=new Forms.Form {Text="Controlled owned modal",ClientSize=new System.Drawing.Size(300,160)})
            {dialog=modal;modal.Shown+=(s,e)=>File.WriteAllText(Path.Combine(root,"popup.txt"),modal.Handle.ToInt64().ToString());modal.ShowDialog(form);dialog=null;}
        };
        var fixtureTimer=new Forms.Timer {Interval=100};
        fixtureTimer.Tick+=(s,e)=>{
            if(!opened&&File.Exists(Path.Combine(root,"open-modal.txt"))){opened=true;button.PerformClick();}
            if(dialog!=null&&File.Exists(Path.Combine(root,"close-modal.txt")))dialog.Close();
        };
        fixtureTimer.Start();
        form.Controls.Add(text);form.Controls.Add(button);
        form.Shown+=(s,e)=>File.WriteAllText(Path.Combine(root,"handles.json"),new JObject{["window"]=form.Handle.ToInt64()}.ToString());
        Forms.Application.Run(form);
    }
    static void Preview(string path)
    {
        path=Path.GetFullPath(path);string dir=Path.GetDirectoryName(path);
        AssemblyLoadContext.Default.Resolving+=(context,name)=>{
            foreach(string folder in new[]{dir,@"C:\Program Files\Autodesk\Revit 2026"}){string file=Path.Combine(folder,name.Name+".dll");if(File.Exists(file))return context.LoadFromAssemblyPath(file);}return null;};
        var assembly=AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        var panel=(System.Windows.UIElement)Activator.CreateInstance(assembly.GetType("revit_mcp_plugin.UI.CodexMirrorPanel"));
        var app=new System.Windows.Application();
        app.Run(new Window { Title="Codex for Revit · spejlingstest (ikke installeret)",Width=1000,Height=820,Content=panel });
    }
    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]public struct MonitorInfo {public int Size;public N.Rect Monitor,Work;public uint Flags;}
        public delegate bool MonitorCallback(IntPtr monitor,IntPtr dc,ref N.Rect bounds,IntPtr data);
        [DllImport("user32.dll")]public static extern bool EnumDisplayMonitors(IntPtr dc,IntPtr clip,MonitorCallback callback,IntPtr data);
        [DllImport("user32.dll")]public static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
        [DllImport("shcore.dll")]public static extern int GetDpiForMonitor(IntPtr monitor,int kind,out uint x,out uint y);
        [DllImport("user32.dll")]public static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")]public static extern bool LogicalToPhysicalPointForPerMonitorDPI(IntPtr window,ref N.Point point);
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr hwnd);
        [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
        [DllImport("user32.dll")] public static extern bool AreDpiAwarenessContextsEqual(IntPtr a,IntPtr b);
        [DllImport("user32.dll")] public static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd,out N.Rect rect);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd,int command);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(N.Point point);
        [DllImport("user32.dll")] public static extern IntPtr GetLastActivePopup(IntPtr hwnd);
    }
}
