using System;
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
        Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); Native.SetThreadDpiAwarenessContext(new IntPtr(-4));
        string root = Path.Combine(Path.GetTempPath(), "revit-mirror-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var child = Process.Start(new ProcessStartInfo(Path.ChangeExtension(Assembly.GetExecutingAssembly().Location, ".exe"), "--fixture \"" + root + "\"") { UseShellExecute = false, CreateNoWindow = true });
        var app = new System.Windows.Application();
        var surface = new CodexMirrorSurface();
        var window = new Window { Title = "Revit mirror · controlled test", Width = 720, Height = 580, Left = 650, Top = 50, Content = surface };
        Exception failure = null;
        window.ContentRendered += async (s,e) => {
            try
            {
                await Until(() => File.Exists(Path.Combine(root,"handles.json")), "fixture startup");
                var handles=JObject.Parse(File.ReadAllText(Path.Combine(root,"handles.json")));
                var source = new IntPtr((long)handles["window"]);
                IntPtr originalParent=Native.GetParent(source);
                IntPtr style=Native.GetWindowLongPtr(source,-16);
                window.Activate();
                await Task.Delay(250);
                Check(N.GetAncestor(N.GetForegroundWindow(),2) == new WindowInteropHelper(window).Handle,"test destination foreground");
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
                var screenBounds=new N.Rect {Left=(int)a.X,Top=(int)a.Y,Right=(int)b.X,Bottom=(int)b.Y};
                try
                {
                    overlay.Attach(new CodexDesktopWindow {Handle=source,ProcessId=child.Id,Title="Controlled fixture"},new WindowInteropHelper(window).Handle,screenBounds);
                    Check(overlay.IsAttached && N.GetAncestor(source,2)==source,"original source remains a top-level window");
                    Check((Native.GetWindowLongPtr(source,-16).ToInt64()&0x40000000L)==0,"source never becomes a WS_CHILD window");
                    Check(Native.AreDpiAwarenessContextsEqual(vp,Native.GetWindowDpiAwarenessContext(source)),"overlay preserves source DPI context");
                    using(new N.DpiScope())
                    {
                        Native.GetWindowRect(source,out var placed);
                        Check(Math.Abs(placed.Left-screenBounds.Left)<3 && Math.Abs(placed.Width-screenBounds.Width)<3,"original window fits the host panel in physical pixels");
                    }
                    File.WriteAllText(Path.Combine(root,"open-modal.txt"),"open");
                    await Until(()=>File.Exists(Path.Combine(root,"popup.txt")),"normal source control opens its owned modal");
                    Check((Native.GetWindowLongPtr(source,-16).ToInt64()&0x08000000L)!=0,"modal disables its real owner normally");
                    overlay.Release();
                    Check((Native.GetWindowLongPtr(source,-16).ToInt64()&0x08000000L)!=0,"release preserves a live modal's disabled-owner state");
                    File.WriteAllText(Path.Combine(root,"close-modal.txt"),"close");
                    await Until(()=>(Native.GetWindowLongPtr(source,-16).ToInt64()&0x08000000L)==0,"modal closes through the source app's normal UI loop");
                    Check(Native.GetParent(source)==originalParent && Native.GetWindowLongPtr(source,-16)==style && !File.Exists(recovery),"release restores owner, styles and recovery state");
                }
                finally {overlay.Release();}
                Console.WriteLine("PASS GPU mirror, original-window overlay, DPI independence, native modal lifetime, restoration and cleanup. No user apps were operated.");
            }
            catch(Exception ex) { failure=ex; }
            finally { surface.Stop(); window.Close(); }
        };
        try { app.Run(window); }
        finally { if(!child.HasExited) child.Kill(); child.Dispose(); }
        if(failure!=null) throw failure;
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
            {dialog=modal;File.WriteAllText(Path.Combine(root,"popup.txt"),"opened");modal.ShowDialog(form);dialog=null;}
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
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr hwnd);
        [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
        [DllImport("user32.dll")] public static extern bool AreDpiAwarenessContextsEqual(IntPtr a,IntPtr b);
        [DllImport("user32.dll")] public static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd,out N.Rect rect);
    }
}
