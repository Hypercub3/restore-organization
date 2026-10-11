using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RestoreOrganization {
public static class Program {
    [STAThread] public static int Main(string[] args) {
        try {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            Engine.ExcludedFile=Assembly.GetExecutingAssembly().Location;
            string folder=args.Length>=2 && args[0]=="--snapshot"?args[1]:null;
            Application.Run(new CreatorWindow(folder));return 0;
        } catch(Exception e) {MessageBox.Show(e.Message,"Restore Organization could not start",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
    }
    public static string RestoreSource() {
        using(Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("RestoreSource"))
        using(StreamReader reader=new StreamReader(stream,Encoding.UTF8)) return reader.ReadToEnd();
    }
    public static string QuoteArg(string value) {
        // Windows CommandLineToArgvW quoting; also used for -File paths passed to PowerShell.
        StringBuilder result=new StringBuilder("\"");int slashes=0;
        foreach(char c in value) {
            if(c=='\\') {slashes++;continue;}
            if(c=='"')result.Append('\\',slashes*2+1).Append('"');
            else result.Append('\\',slashes).Append(c);
            slashes=0;
        }
        result.Append('\\',slashes*2).Append('"');return result.ToString();
    }
    public static string ShellAction(string action) {
        string script=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ContextMenu.ps1");
        if(!File.Exists(script))throw new FileNotFoundException("Keep the complete application folder together. ContextMenu.ps1 is missing.",script);
        ProcessStartInfo info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess ? "Sysnative" : "System32","WindowsPowerShell","v1.0","powershell.exe"),
            "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "+QuoteArg(script)+" -Action "+action);
        info.UseShellExecute=false;info.CreateNoWindow=true;info.RedirectStandardOutput=true;info.RedirectStandardError=true;
        using(Process p=Process.Start(info)) {
            Task<string> output=p.StandardOutput.ReadToEndAsync(),error=p.StandardError.ReadToEndAsync();
            p.WaitForExit();Task.WaitAll(output,error);
            if(p.ExitCode!=0)throw new IOException(output.Result+"\r\n"+error.Result);
            return output.Result.Trim();
        }
    }
}
public class CreatorWindow:Form {
    readonly TextBox folder=new TextBox {Dock=DockStyle.Fill};
    readonly TextBox name=new TextBox {Dock=DockStyle.Fill,Text="My arrangement"};
    readonly Label status=new Label {Dock=DockStyle.Fill,AutoSize=true,Padding=new Padding(8)};
    readonly Label menuStatus=new Label {AutoSize=true,Padding=new Padding(4)};
    readonly TextBox details=new TextBox {Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical,BackColor=SystemColors.Window};
    readonly Button browse=Ui.Button("Choose folder..."),create=Ui.Button("Create snapshot"),open=Ui.Button("Open collection folder"),
        enable=Ui.Button("Enable context menu"),disable=Ui.Button("Disable context menu");
    bool busy;
    public CreatorWindow(string selected) {
        Ui.Setup(this,"Restore Organization");Size=new Size(940,650);
        TableLayoutPanel layout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=3,RowCount=8,Padding=new Padding(18)};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for(int i=0;i<8;i++)layout.RowStyles.Add(new RowStyle(i==4?SizeType.Percent:SizeType.AutoSize,i==4?100:0));
        Label intro=new Label {Dock=DockStyle.Fill,AutoSize=true,Padding=new Padding(0,0,0,14),
            Text="Save a folder arrangement. Switch arrangements later by double-clicking a saved restore script.\r\nSnapshots include filenames, file locations, subfolders, and empty folders. File contents are preserved."};
        layout.Controls.Add(intro,0,0);layout.SetColumnSpan(intro,3);
        layout.Controls.Add(new Label {Text="Collection folder",AutoSize=true,Padding=new Padding(0,7,10,0)},0,1);layout.Controls.Add(folder,1,1);layout.Controls.Add(browse,2,1);
        layout.Controls.Add(new Label {Text="Arrangement name",AutoSize=true,Padding=new Padding(0,7,10,0)},0,2);layout.Controls.Add(name,1,2);
        FlowLayoutPanel actions=new FlowLayoutPanel {AutoSize=true,Dock=DockStyle.Fill};
        actions.Controls.Add(create);actions.Controls.Add(open);layout.Controls.Add(actions,0,3);layout.SetColumnSpan(actions,3);
        details.Text="1. Choose your collection folder and give this arrangement a name, such as By Author.\r\n"+
            "2. Click Create snapshot. A single Restore - By Author.cmd file is saved in that folder.\r\n"+
            "3. Reorganize your files and save another named snapshot, such as By Subject.\r\n"+
            "4. Double-click either script to preview its moves, then click Restore.\r\n\r\n"+
            "The scripts work without this application installed. Keep each script at the root of its collection.\r\n"+
            "Missing files, uncertain matches, and occupied destinations are reported. New files stay where they are.\r\n"+
            "Edited ebooks can be identified when their Windows file identity survives; uncertain replacements are skipped.\r\n"+
            "An undo script and recovery journal are created before every restoration.\r\n\r\n"+
            "Dropbox: make the collection available offline before scanning. Reading fingerprints may download online-only files.\r\n"+
            "Snapshot and undo scripts and the .restore-organization recovery folder are excluded automatically.\r\n"+
            "Symbolic links and junctions are not followed. Incomplete scans do not produce snapshots.";
        layout.Controls.Add(details,0,4);layout.SetColumnSpan(details,3);
        layout.Controls.Add(status,0,5);layout.SetColumnSpan(status,3);
        Label menuInfo=new Label {AutoSize=true,Dock=DockStyle.Fill,Text="Optional: Create snapshot in File Explorer's folder menu (main menu on Windows 11).\r\nEnable installs the menu for your account. Windows administrator approval is needed to trust its signing certificate. Default: off.",Padding=new Padding(0,12,0,3)};
        layout.Controls.Add(menuInfo,0,6);layout.SetColumnSpan(menuInfo,3);
        FlowLayoutPanel menuButtons=new FlowLayoutPanel {AutoSize=true,Dock=DockStyle.Fill,WrapContents=true};
        menuButtons.Controls.Add(enable);menuButtons.Controls.Add(disable);menuButtons.Controls.Add(menuStatus);layout.Controls.Add(menuButtons,0,7);layout.SetColumnSpan(menuButtons,3);
        Controls.Add(layout);status.Text="Ready.";
        if(selected!=null)folder.Text=selected;
        browse.Click+=(s,e)=>{try {string selectedFolder=FolderPicker.Choose(this,folder.Text);
            if(selectedFolder!=null)folder.Text=selectedFolder;}catch(Exception ex){Ui.Error(this,ex);}};
        create.Click+=async(s,e)=>await CaptureAsync();
        open.Click+=(s,e)=>{try {Process.Start("explorer.exe",Program.QuoteArg(Engine.RootPath(folder.Text)));}catch(Exception ex){Ui.Error(this,ex);}};
        enable.Click+=async(s,e)=>await ChangeMenu("Enable");
        disable.Click+=async(s,e)=>await ChangeMenu("Disable");
        Shown+=async(s,e)=>{try {menuStatus.Text=await Task.Run(()=>Program.ShellAction("Status"));}catch {menuStatus.Text="Not enabled."; }};
        FormClosing+=(s,e)=>{if(busy){e.Cancel=true;status.Text="Please wait for the current operation to finish."; }};
    }
    void Busy(bool value) {
        busy=value;UseWaitCursor=value;create.Enabled=!value;browse.Enabled=!value;folder.ReadOnly=value;name.ReadOnly=value;
        enable.Enabled=!value;disable.Enabled=!value;open.Enabled=!value;
    }
    void Progress(string text) {if(!IsDisposed&&IsHandleCreated)BeginInvoke((Action)(()=>status.Text=text));}
    async Task CaptureAsync() {
        string root;
        try {
            root=Engine.RootPath(folder.Text);
            if(String.IsNullOrWhiteSpace(name.Text))throw new ArgumentException("Give this arrangement a name.");
        } catch(Exception e) {Ui.Error(this,e);return;}
        string label=name.Text.Trim();Busy(true);status.Text="Scanning all subfolders and reading fingerprints...";
        try {
            string path=await Task.Run(()=>{
                using(FileStream guard=Engine.Lock(root)) {
                    if(Engine.Pending(root).Count>0)throw new IOException("Recover the interrupted restore first by opening an existing restore script.");
                    Snapshot snapshot=Engine.Capture(root,label,Progress);
                    string safe=label;foreach(char c in Path.GetInvalidFileNameChars())safe=safe.Replace(c,'_');safe=safe.TrimEnd('.',' ');
                    if(safe.Length>80)safe=safe.Substring(0,80);if(safe.Length==0)safe="Arrangement";
                    string candidate=Path.Combine(root,"Restore - "+safe+".cmd");int n=2;
                    while(File.Exists(candidate)||Directory.Exists(candidate))candidate=Path.Combine(root,"Restore - "+safe+" ("+(n++)+").cmd");
                    Engine.WriteScript(snapshot,candidate,Program.RestoreSource());return candidate;
                }
            });
            status.Text="Snapshot created: "+Path.GetFileName(path);
            MessageBox.Show(this,"Saved:\r\n"+path+"\r\n\r\nDouble-click this script to preview and restore the arrangement.","Snapshot created",MessageBoxButtons.OK,MessageBoxIcon.Information);
        } catch(Exception e) {status.Text="Snapshot was not created.";Ui.Error(this,e);}
        finally {Busy(false);}
    }
    async Task ChangeMenu(string action) {
        Busy(true);status.Text=(action=="Enable"?"Enabling":"Disabling")+" optional context menu...";
        try {string message=await Task.Run(()=>Program.ShellAction(action));status.Text=message;menuStatus.Text=await Task.Run(()=>Program.ShellAction("Status"));}
        catch(Exception e) {status.Text="Context menu setting could not be changed.";Ui.Error(this,e);}
        finally {Busy(false);}
    }
}
}

