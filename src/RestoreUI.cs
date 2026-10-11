using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RestoreOrganization {
public static class Ui {
    public static void Setup(Form form,string title) {
        form.Text=title;form.Font=new Font("Segoe UI",10);form.StartPosition=FormStartPosition.CenterScreen;
        form.Size=new Size(1100,730);form.MinimumSize=new Size(860,560);form.AutoScaleMode=AutoScaleMode.Dpi;
    }
    public static void Error(IWin32Window owner,Exception error) {
        using(Form f=new Form {Text="Restore Organization - details",StartPosition=FormStartPosition.CenterParent,Size=new Size(780,420),MinimumSize=new Size(540,280)}) {
            TextBox text=new TextBox {Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,Dock=DockStyle.Fill,Text=error.Message,Font=new Font("Segoe UI",10)};
            Button close=new Button {Text="Close",Dock=DockStyle.Bottom,Height=38,DialogResult=DialogResult.OK};
            f.Controls.Add(text);f.Controls.Add(close);f.AcceptButton=close;f.ShowDialog(owner);
        }
    }
    public static Button Button(string text) {return new Button {Text=text,AutoSize=true,Height=36,Padding=new Padding(8,3,8,3),Margin=new Padding(4)};}
}
public class RestoreWindow:Form {
    readonly string root,source;
    readonly Snapshot snapshot;
    readonly Label heading=new Label {Dock=DockStyle.Fill,AutoSize=true,Padding=new Padding(8)};
    readonly Label status=new Label {Dock=DockStyle.Fill,AutoSize=true,Padding=new Padding(8)};
    readonly DataGridView grid=new DataGridView {Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,
        RowHeadersVisible=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false};
    readonly Button restore=Ui.Button("Restore"),refresh=Ui.Button("Refresh preview"),recover=Ui.Button("Recover interrupted restore"),report=Ui.Button("Open report");
    Plan plan;
    bool busy;
    string reportPath;
    public static void Run(string root,string json,string source) {
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new RestoreWindow(root,Engine.ReadSnapshot(json),source));
    }
    public RestoreWindow(string folder,Snapshot saved,string code) {
        root=Engine.RootPath(folder);snapshot=saved;source=code;
        Ui.Setup(this,"Restore Organization - "+snapshot.Name);
        TableLayoutPanel layout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(12)};
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        heading.Text="Arrangement: "+snapshot.Name+"\r\nRestore in: "+root+"\r\nSaved: "+snapshot.CreatedUtc+
            "\r\nCurrent file contents are preserved. Missing, ambiguous, and conflicting entries are skipped.";
        foreach(string column in new[]{"Status","Current location","Saved location","Details"}) grid.Columns.Add(column,column);
        grid.Columns[0].FillWeight=22;grid.Columns[1].FillWeight=60;grid.Columns[2].FillWeight=60;grid.Columns[3].FillWeight=85;
        grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells;grid.DefaultCellStyle.WrapMode=DataGridViewTriState.True;
        FlowLayoutPanel buttons=new FlowLayoutPanel {Dock=DockStyle.Fill,AutoSize=true,WrapContents=true};
        buttons.Controls.Add(restore);buttons.Controls.Add(refresh);buttons.Controls.Add(recover);buttons.Controls.Add(report);
        layout.Controls.Add(heading,0,0);layout.Controls.Add(grid,0,1);layout.Controls.Add(status,0,2);layout.Controls.Add(buttons,0,3);Controls.Add(layout);
        restore.Enabled=false;recover.Enabled=false;report.Enabled=false;
        Shown+=async (s,e)=>await RefreshAsync();
        refresh.Click+=async(s,e)=>await RefreshAsync();
        restore.Click+=async(s,e)=>await RestoreAsync();
        recover.Click+=async(s,e)=>await RecoverAsync();
        report.Click+=(s,e)=>{try {System.Diagnostics.Process.Start(reportPath);}catch(Exception ex){Ui.Error(this,ex);}};
        FormClosing+=(s,e)=>{if(busy){e.Cancel=true;status.Text="Please wait for the current operation to finish before closing."; }};
    }
    void SetBusy(bool value) {
        busy=value;restore.Enabled=false;refresh.Enabled=!value;recover.Enabled=false;UseWaitCursor=value;
    }
    void Progress(string text) {
        if(!IsDisposed&&IsHandleCreated) BeginInvoke((Action)(()=>status.Text=text));
    }
    async Task RefreshAsync() {
        SetBusy(true);status.Text="Scanning collection and checking file identities...";
        try {
            int pending=await Task.Run(()=>Engine.Pending(root).Count);
            if(pending>0) {
                plan=null;grid.Rows.Clear();status.Text="An interrupted restoration needs recovery before another snapshot can be applied.";
                SetBusy(false);recover.Enabled=true;return;
            }
            plan=await Task.Run(()=>Engine.Preview(root,snapshot,Progress));
            grid.Rows.Clear();
            foreach(PlanItem item in plan.Items) {
                int n=grid.Rows.Add(item.Status,item.Source,item.Destination,item.Detail);
                if(item.Status=="Move"||item.Status=="Rename folder")grid.Rows[n].DefaultCellStyle.BackColor=Color.Honeydew;
                else if(item.Status=="Conflict"||item.Status=="Folder conflict"||item.Status=="Unreadable")grid.Rows[n].DefaultCellStyle.BackColor=Color.MistyRose;
                else if(item.Status=="Missing / unidentified"||item.Status=="Ambiguous")grid.Rows[n].DefaultCellStyle.BackColor=Color.LemonChiffon;
            }
            int recognized=plan.Items.Count(x=>x.Current!=null&&x.Status!="New / unmatched");
            int skipped=plan.Items.Count(x=>x.Status=="Conflict"||x.Status=="Folder conflict"||x.Status=="Missing / unidentified"||x.Status=="Ambiguous");
            status.Text=plan.MoveCount+" files to move; "+plan.FolderCount+" folders to create; "+plan.CaseFolderCount+" folders to rename; "+skipped+" entries skipped; "+
                plan.Items.Count(x=>x.Status=="New / unmatched")+" new/unmatched files left in place.";
            if(plan.Scan.Errors.Count>0) status.Text+=" Resolve unreadable entries before restoring.";
            else if(snapshot.Files.Count>0&&recognized==0) status.Text="No saved files were recognized. Check that this script is in the correct collection folder.";
            SetBusy(false);
            restore.Text="Restore ("+plan.MoveCount+" moves)";
            restore.Enabled=plan.Scan.Errors.Count==0 && (snapshot.Files.Count==0||recognized>0) && (plan.MoveCount>0||plan.FolderCount>0||plan.CaseFolderCount>0);
        } catch(Exception e) {SetBusy(false);status.Text="Preview could not be completed.";Ui.Error(this,e);}
    }
    async Task RestoreAsync() {
        if(plan==null)return;SetBusy(true);status.Text="Preparing undo snapshot and restoring...";
        try {
            Plan approved=plan;
            RestoreResult result=await Task.Run(()=>Engine.Restore(approved,(snap,path)=>Engine.WriteScript(snap,path,source),Progress));
            reportPath=result.Report;report.Enabled=true;MessageBox.Show(this,result.Message,"Restore Organization",MessageBoxButtons.OK,MessageBoxIcon.Information);
        } catch(Exception e) {Ui.Error(this,e);}
        finally {SetBusy(false);}
        await RefreshAsync();
    }
    async Task RecoverAsync() {
        SetBusy(true);status.Text="Recovering the arrangement from before the interrupted restoration...";
        try {await Task.Run(()=>Engine.Recover(root,Progress));MessageBox.Show(this,"Recovery completed. Refreshing the preview.","Restore Organization");}
        catch(Exception e) {Ui.Error(this,e);}
        finally {SetBusy(false);}
        await RefreshAsync();
    }
}
}

