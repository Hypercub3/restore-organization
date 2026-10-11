using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using RestoreOrganization;
class FolderPickerTests {
    delegate bool WindowCallback(IntPtr window,IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumThreadWindows(uint thread,WindowCallback callback,IntPtr data);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window,StringBuilder text,int length);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int cx,int cy,uint flags);
    static bool seen,timedOut;
    static string Exercise(string initial,bool accept) {
        seen=false;timedOut=false;bool sent=false;
        DateTime started=DateTime.UtcNow;
        using(Timer timer=new Timer {Interval=100}) {
            timer.Tick+=(s,e)=>{
                EnumThreadWindows(GetCurrentThreadId(),(window,data)=>{
                    StringBuilder text=new StringBuilder(256);GetWindowText(window,text,text.Capacity);
                    if(text.ToString()!="Choose the root of your collection")return true;
                    seen=true;SetWindowPos(window,IntPtr.Zero,-32000,-32000,0,0,0x15);
                    if(!sent&&(DateTime.UtcNow-started).TotalMilliseconds>=700) {
                        sent=true;PostMessage(window,accept?0x0111u:0x0010u,accept?new IntPtr(1):IntPtr.Zero,IntPtr.Zero);
                    }
                    if((DateTime.UtcNow-started).TotalSeconds>5) {
                        timedOut=true;PostMessage(window,0x0010,IntPtr.Zero,IntPtr.Zero);
                    }
                    return true;
                },IntPtr.Zero);
            };
            timer.Start();
            try {return FolderPicker.Choose(null,initial);}
            finally {timer.Stop();}
        }
    }
    [STAThread] static int Main() {
        try {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            string root=Path.Combine(Path.GetFullPath("tests\\work"),"picker-"+Guid.NewGuid().ToString("N"),"Crème 一 & books");
            Directory.CreateDirectory(root);
            string chosen=Exercise(root,true);
            if(!seen||timedOut||chosen!=root)throw new Exception("Native folder selection failed: "+chosen);
            Console.WriteLine("PASS: Explorer-style native dialog selects an existing Unicode folder and returns its filesystem path.");
            chosen=Exercise(root,false);
            if(!seen||timedOut||chosen!=null)throw new Exception("Cancellation did not return an empty result.");
            Console.WriteLine("PASS: cancellation returns no selection.");
            chosen=Exercise("Z:\\Missing folder\\",false);
            if(!seen||timedOut||chosen!=null)throw new Exception("Unavailable initial folder was not handled.");
            Console.WriteLine("PASS: unavailable initial folder still opens a cancellable native picker.");
            return 0;
        } catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
