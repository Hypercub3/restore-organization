using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using RestoreOrganization;

public static class EngineTests {
    static int checks;
    static string work,code;
    static void Check(bool ok,string name) {if(!ok)throw new Exception("FAIL: "+name);checks++;Console.WriteLine("PASS: "+name);}
    static string Fixture(string name) {string p=Path.Combine(work,name+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(p);return p;}
    static void Put(string root,string path,string text) {string full=Engine.Full(root,path);Directory.CreateDirectory(Path.GetDirectoryName(full));File.WriteAllText(full,text,Encoding.UTF8);}
    static void Move(string root,string from,string to) {string full=Engine.Full(root,to);Directory.CreateDirectory(Path.GetDirectoryName(full));File.Move(Engine.Full(root,from),full);}
    static string Text(string root,string path) {return File.ReadAllText(Engine.Full(root,path),Encoding.UTF8);}
    static Snapshot Capture(string root) {return Engine.Capture(root,"Test arrangement",null);}
    static Dictionary<string,string> Tree(string root) {return Engine.ScanFolder(root,null).Files.ToDictionary(x=>x.Path,x=>x.Hash,StringComparer.OrdinalIgnoreCase);}
    static bool Equal(Dictionary<string,string> a,Dictionary<string,string> b) {return a.Count==b.Count&&a.All(x=>b.ContainsKey(x.Key)&&b[x.Key]==x.Value);}
    static RestoreResult Apply(string root,Snapshot s) {return Engine.Restore(Engine.Preview(root,s,null),Write,null);}
    static void Write(Snapshot s,string path) {Engine.WriteScript(s,path,code);}
    static void ExpectError(Action a,string name) {try{a();}catch(Exception){Check(true,name);return;}Check(false,name);}
    static void Cycle(string root) {Move(root,"a.epub","temp");Move(root,"b.epub","a.epub");Move(root,"temp","b.epub");}
    [STAThread] public static int Main(string[] args) {
        try {
            code=Program.RestoreSource();
            if(args.Length>0&&args[0]=="--crash") {
                string root=args[1];Snapshot s=Engine.ReadSnapshot(File.ReadAllText(args[2]));
                int seen=0;Engine.FaultHook=eventName=>{if(eventName==args[3]&&++seen==Int32.Parse(args[4]))Environment.Exit(77);};
                Apply(root,s);return 78;
            }
            work=Path.GetFullPath("tests\\work");Directory.CreateDirectory(work);
            Application.EnableVisualStyles();
            Basic();Conflicts();IdentityAndDuplicates();Recovery();FolderNames();Validation();Scripts();StandaloneApply();Scale();Render();
            Console.WriteLine("PASS: "+checks+" checks");File.WriteAllText("build\\test-result.txt","PASS: "+checks+" checks\r\n"+DateTime.UtcNow.ToString("o"));return 0;
        } catch(Exception e) {Console.Error.WriteLine(e.ToString());return 1;}
    }
    static void Basic() {
        string root=Fixture("basic");Put(root,"Authors\\A\\a.epub","book a");Put(root,"Authors\\B\\b.pdf","book b");Directory.CreateDirectory(Engine.Full(root,"Empty"));
        Snapshot author=Capture(root);Check(author.Files.Count==2&&author.Directories.Contains("Empty"),"snapshot includes empty folders and nested files");
        Write(author,Engine.Full(root,"Restore - Authors.cmd"));
        Move(root,"Authors\\A\\a.epub","Subjects\\Math\\renamed.epub");Move(root,"Authors\\B\\b.pdf","Subjects\\Science\\b.pdf");
        File.WriteAllText(Engine.Full(root,"Subjects\\Math\\renamed.epub"),"edited book a");
        Snapshot subject=Capture(root);
        Check(subject.Files.Count==2,"generated scripts excluded from snapshots");
        Put(root,"Extras\\new.epub","new book");
        Directory.CreateDirectory(Engine.Full(root,"UnrelatedEmpty"));
        Plan plan=Engine.Preview(root,author,null);
        Check(plan.MoveCount==2,"renamed and edited file matched by identity");
        Check(plan.Items.Any(x=>x.Detail.Contains("Edited content")),"edited content explained in preview");
        RestoreResult result=Apply(root,author);
        Check(result.Moved==2&&Text(root,"Authors\\A\\a.epub")=="edited book a","restoration preserves edited content");
        Check(Text(root,"Extras\\new.epub")=="new book","new file remains untouched");
        Check(Directory.Exists(Engine.Full(root,"Empty"))&&Directory.Exists(Engine.Full(root,"UnrelatedEmpty")),"saved and unrelated empty folders retained");
        Check(!Directory.Exists(Engine.Full(root,"Subjects")),"only ancestors emptied by moves cleaned up");
        Check(File.Exists(result.Undo)&&File.Exists(result.Report),"independent undo script and report created");
        Check(Engine.Pending(root).Count==0,"successful journal marked completed");
        Apply(root,subject);Check(Text(root,"Subjects\\Math\\renamed.epub")=="edited book a","switch back to second arrangement");
        Apply(root,author);Check(Engine.Preview(root,author,null).MoveCount==0,"repeat restoration is idempotent");
        File.Delete(Engine.Full(root,"Authors\\B\\b.pdf"));
        Plan missing=Engine.Preview(root,subject,null);
        Check(missing.Items.Any(x=>x.Status=="Missing / unidentified"),"missing files reported");
        Apply(root,subject);Check(Text(root,"Subjects\\Math\\renamed.epub")=="edited book a","remaining files restored despite missing file");
        string relocated=root+"-relocated";Directory.Move(root,relocated);
        Check(Engine.Preview(relocated,author,null).MoveCount==1,"whole collection can be relocated");
    }
    static void Conflicts() {
        string root=Fixture("conflict");Put(root,"a.epub","A");Put(root,"b.epub","B");Put(root,"free.epub","F");Snapshot s=Capture(root);
        Move(root,"a.epub","away\\a.epub");Move(root,"b.epub","a.epub");Move(root,"free.epub","away\\free.epub");Put(root,"b.epub","new occupant");
        Plan p=Engine.Preview(root,s,null);
        Check(p.Items.Count(x=>x.Status=="Conflict")==2,"destination conflicts propagate to dependent moves");
        Apply(root,s);
        Check(Text(root,"a.epub")=="B"&&Text(root,"b.epub")=="new occupant"&&Text(root,"away\\a.epub")=="A","conflicting files all preserved");
        Check(Text(root,"free.epub")=="F","unrelated safe move still restored");
        string cycle=Fixture("cycle");Put(cycle,"a.epub","A");Put(cycle,"b.epub","B");Snapshot saved=Capture(cycle);Cycle(cycle);Apply(cycle,saved);
        Check(Text(cycle,"a.epub")=="A"&&Text(cycle,"b.epub")=="B","filename swap restored without overwriting");
        Move(cycle,"a.epub","A.EPUB");Apply(cycle,saved);
        Check(Directory.GetFiles(cycle).Any(x=>Path.GetFileName(x)=="a.epub"),"case-only file rename restored");
        string blocked=Fixture("blocked");Put(blocked,"Folder\\book.epub","content");Snapshot snapshot=Capture(blocked);
        Move(blocked,"Folder\\book.epub","book.epub");Directory.Delete(Engine.Full(blocked,"Folder"));Put(blocked,"Folder","blocker");
        Apply(blocked,snapshot);Check(Text(blocked,"Folder")=="blocker"&&Text(blocked,"book.epub")=="content","file blocking required parent folder preserved");
    }
    static void IdentityAndDuplicates() {
        string root=Fixture("copies");Put(root,"a.epub","same");Put(root,"b.epub","same");Snapshot snapshot=Capture(root);
        Move(root,"a.epub","x.epub");Move(root,"b.epub","y.epub");
        Check(Engine.Preview(root,snapshot,null).MoveCount==2,"identical copies distinguished by Windows file identity");
        foreach(FileRecord f in snapshot.Files){f.Identity=null;f.Created=0;}
        Check(Engine.Preview(root,snapshot,null).Items.Count(x=>x.Status=="Ambiguous")==2,"ambiguous identical copies skipped when identity is unavailable");
        string hash=Fixture("hash");Put(hash,"one.epub","unique");Snapshot s=Capture(hash);Move(hash,"one.epub","elsewhere.epub");s.Files[0].Identity=null;
        Check(Engine.Preview(hash,s,null).MoveCount==1,"unique content fingerprint fallback");
        File.WriteAllText(Engine.Full(hash,"elsewhere.epub"),"replacement");
        Check(Engine.Preview(hash,s,null).MoveCount==0,"edited replacement without identity is not guessed");
        string path=Fixture("duplicate-path");Put(path,"a.epub","same");Put(path,"b.epub","same");Snapshot both=Capture(path);
        foreach(FileRecord f in both.Files)f.Identity=null;
        Check(Engine.Preview(path,both,null).Items.All(x=>x.Status=="Already correct"),"unchanged identical copies matched at saved paths");
    }
    static void Recovery() {
        foreach(string point in new[]{"before-stage","staged","before-place","placed"}) {
            string root=Fixture("rollback-"+point);Put(root,"a.epub","A");Put(root,"b.epub","B");Snapshot saved=Capture(root);Cycle(root);
            Dictionary<string,string> before=Tree(root);bool fired=false;
            Engine.FaultHook=e=>{if(e==point&&!fired){fired=true;throw new IOException("Injected sharing failure");}};
            ExpectError(()=>Apply(root,saved),"injected "+point+" failure reported");Engine.FaultHook=null;
            Check(Equal(before,Tree(root)),"automatic rollback after "+point+" preserves arrangement and hashes");
            Check(Engine.Pending(root).Count==0,"rollback journal finalized after "+point);
        }
        foreach(string point in new[]{"staged","placed"}) {
            string root=Fixture("crash-"+point);Put(root,"a.epub","A");Put(root,"b.epub","B");Snapshot saved=Capture(root);Cycle(root);
            Dictionary<string,string> before=Tree(root);string manifest=Path.Combine(work,Guid.NewGuid().ToString("N")+".json");File.WriteAllText(manifest,Engine.Json(saved));
            ProcessStartInfo start=new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--crash "+Program.QuoteArg(root)+" "+Program.QuoteArg(manifest)+" "+point+" 1");
            start.UseShellExecute=false;start.CreateNoWindow=true;
            using(Process p=Process.Start(start)){p.WaitForExit();Check(p.ExitCode==77,"real process interruption after "+point);}
            Check(Engine.Pending(root).Count==1,"unfinished journal detected after "+point);
            ExpectError(()=>Apply(root,saved),"new restore refused until recovery after "+point);
            Engine.Recover(root,null);Check(Equal(before,Tree(root)),"recovery after process interruption preserves original hashes and arrangement");
            Check(Engine.Pending(root).Count==0,"recovery is finalized");
        }
        string changed=Fixture("stale-preview");Put(changed,"a.epub","A");Snapshot s=Capture(changed);Move(changed,"a.epub","b.epub");
        Plan plan=Engine.Preview(changed,s,null);Put(changed,"new.epub","new");
        ExpectError(()=>Engine.Restore(plan,Write,null),"stale preview rejected before moving files");
        Check(File.Exists(Engine.Full(changed,"b.epub")),"stale preview causes no moves");
        using(FileStream guard=Engine.Lock(changed))ExpectError(()=>Apply(changed,s),"concurrent operation blocked by folder lock");
        string locked=Fixture("locked");Put(locked,"a.epub","A");
        using(FileStream guard=new FileStream(Engine.Full(locked,"a.epub"),FileMode.Open,FileAccess.ReadWrite,FileShare.None)) {
            Check(Engine.ScanFolder(locked,null).Errors.Count==1,"locked file produces readable scan error");
            ExpectError(()=>Capture(locked),"incomplete snapshot refused");
        }
        string replaced=Fixture("recover-occupied");Put(replaced,"a.epub","A");Snapshot original=Capture(replaced);Move(replaced,"a.epub","b.epub");
        string data=Path.Combine(work,Guid.NewGuid().ToString("N")+".json");File.WriteAllText(data,Engine.Json(original));
        ProcessStartInfo crash=new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--crash "+Program.QuoteArg(replaced)+" "+Program.QuoteArg(data)+" staged 1");
        crash.UseShellExecute=false;crash.CreateNoWindow=true;using(Process p=Process.Start(crash)){p.WaitForExit();}
        Put(replaced,"b.epub","new occupant");
        ExpectError(()=>Engine.Recover(replaced,null),"recovery preserves new occupant rather than overwriting");
        Check(Text(replaced,"b.epub")=="new occupant"&&Engine.Pending(replaced).Count==1,"blocked recovery retains new file and recovery journal");
        File.Delete(Engine.Full(replaced,"b.epub"));Engine.Recover(replaced,null);Check(Text(replaced,"b.epub")=="A","recovery can be retried after conflict resolved");
    }
    static void FolderNames() {
        string root=Fixture("folder-case");Put(root,"By Author\\Nested\\a.epub","A");Directory.CreateDirectory(Engine.Full(root,"EmptyFolder"));Snapshot saved=Capture(root);
        Directory.Move(Engine.Full(root,"By Author"),Engine.Full(root,"temporary"));Directory.Move(Engine.Full(root,"temporary"),Engine.Full(root,"by author"));
        Directory.Move(Engine.Full(root,"EmptyFolder"),Engine.Full(root,"temp-empty"));Directory.Move(Engine.Full(root,"temp-empty"),Engine.Full(root,"emptyfolder"));Put(root,"by author\\new.epub","new");
        Plan p=Engine.Preview(root,saved,null);Check(p.CaseFolderCount==2&&p.MoveCount==0,"case-only folder changes shown separately in preview");
        Apply(root,saved);
        Check(Directory.GetDirectories(root).Any(x=>Path.GetFileName(x)=="By Author")&&Directory.GetDirectories(root).Any(x=>Path.GetFileName(x)=="EmptyFolder"),"folder-name capitalization restored");
        Check(Text(root,"By Author\\new.epub")=="new","new files inside case-renamed folder preserved");
        Check(Engine.Preview(root,saved,null).MoveCount==0&&Engine.Preview(root,saved,null).CaseFolderCount==0,"folder-case restoration is idempotent");
        foreach(string point in new[]{"folder-case-staged","folder-case-placed"}) {
            string crashRoot=Fixture(point);Put(crashRoot,"Upper\\a.epub","A");Snapshot snapshot=Capture(crashRoot);
            Directory.Move(Engine.Full(crashRoot,"Upper"),Engine.Full(crashRoot,"temp"));Directory.Move(Engine.Full(crashRoot,"temp"),Engine.Full(crashRoot,"upper"));Put(crashRoot,"upper\\new.epub","new");
            string data=Path.Combine(work,Guid.NewGuid().ToString("N")+".json");File.WriteAllText(data,Engine.Json(snapshot));
            ProcessStartInfo info=new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--crash "+Program.QuoteArg(crashRoot)+" "+Program.QuoteArg(data)+" "+point+" 1");
            info.UseShellExecute=false;info.CreateNoWindow=true;using(Process process=Process.Start(info)){process.WaitForExit();Check(process.ExitCode==77,"process interruption during "+point);}
            Engine.Recover(crashRoot,null);
            Check(Directory.GetDirectories(crashRoot).Any(x=>Path.GetFileName(x)=="upper")&&Text(crashRoot,"upper\\new.epub")=="new","folder-case crash recovery preserves old name and new files");
        }
        string transition=Fixture("file-directory-transition");Put(transition,"Folder\\book.epub","A");Put(transition,"blocker.epub","B");Snapshot target=Capture(transition);
        Move(transition,"Folder\\book.epub","loose.epub");Directory.Delete(Engine.Full(transition,"Folder"));Move(transition,"blocker.epub","Folder");
        Dictionary<string,string> before=Tree(transition);bool fired=false;
        Engine.FaultHook=e=>{if(e=="placed"&&!fired){fired=true;throw new IOException("failure after creating target folder");}};
        ExpectError(()=>Apply(transition,target),"file-to-folder transition interrupted safely");Engine.FaultHook=null;
        Check(Equal(before,Tree(transition)),"rollback removes only transaction-created empty folder blocking original file");
        Apply(transition,target);Check(Text(transition,"Folder\\book.epub")=="A"&&Text(transition,"blocker.epub")=="B","file-to-folder transition restores normally");
    }

    static void Validation() {
        foreach(string path in new[]{"..\\outside.epub","C:\\outside.epub","foo:bar","a\\..\\b","a/b","NUL.txt","bad. ","\\.epub"})
            ExpectError(()=>Engine.ValidateRelative(path,false),"unsafe path rejected: "+path);
        ExpectError(()=>Engine.ValidateRelative(".restore-organization\\stage",false),"snapshot cannot target recovery state");
        string root=Fixture("validation");Put(root,"a.epub","A");Snapshot s=Capture(root);
        s.Files.Add(s.Files[0]);ExpectError(()=>Engine.ReadSnapshot(Engine.Json(s)),"duplicate snapshot destinations rejected");
        ExpectError(()=>Engine.ReadSnapshot("{\"Version\":2}"),"unsupported snapshot version rejected");
        string reserved=Fixture("reserved-state");Put(reserved,".restore-organization\\user.epub","user data");ExpectError(()=>Capture(reserved),"unowned reserved folder is never silently ignored");
        string unicode=Fixture("unicode & ! %");Put(unicode,"Crème & 100% ! [一]\\Müller.epub","Unicode");
        Snapshot u=Capture(unicode);Move(unicode,u.Files[0].Path," elsewhere ! % &.epub");Apply(unicode,u);
        Check(Text(unicode,u.Files[0].Path)=="Unicode","Unicode and shell-special filenames restored");
    }
    static void Scripts() {
        string root=Fixture("standalone & ! %");Put(root,"Books\\one.epub","A");Snapshot saved=Capture(root);string script=Engine.Full(root,"Restore - 一 & 100% !.cmd");
        Write(saved,script);Move(root,"Books\\one.epub","renamed.epub");
        ProcessStartInfo info=new ProcessStartInfo("cmd.exe","/d /s /c \"\""+script+"\"\"");
        Environment.SetEnvironmentVariable("RO_HEADLESS","preview");info.UseShellExecute=false;info.CreateNoWindow=true;info.RedirectStandardOutput=true;info.RedirectStandardError=true;
        using(Process p=Process.Start(info)) {
            string output=p.StandardOutput.ReadToEnd(),error=p.StandardError.ReadToEnd();p.WaitForExit();
            Console.WriteLine(output);Console.WriteLine(error);
            Check(p.ExitCode==0,"generated CMD runs under stock Windows PowerShell");
            Check(output.Contains("Move")&&output.Contains("Books"),"standalone script decodes snapshot and previews real moves");
        }
        Check(!File.Exists(Engine.Full(root,"Books\\one.epub")),"standalone preview does not move files");
        ExpectError(()=>Write(saved,script),"existing generated script is never overwritten");
        Check(Directory.GetFiles(root,"*.tmp").Length==0,"failed script generation leaves no partial output");
        string isolated=Fixture("isolated");File.Copy(script,Engine.Full(isolated,"Only restore script.cmd"));Put(isolated,"copied.epub","A");
        ProcessStartInfo second=new ProcessStartInfo("cmd.exe","/d /s /c \"\""+Engine.Full(isolated,"Only restore script.cmd")+"\"\"");
        Environment.SetEnvironmentVariable("RO_HEADLESS","preview");second.UseShellExecute=false;second.CreateNoWindow=true;second.RedirectStandardOutput=true;second.RedirectStandardError=true;
        using(Process p=Process.Start(second)){string output=p.StandardOutput.ReadToEnd(),error=p.StandardError.ReadToEnd();p.WaitForExit();
            Check(p.ExitCode==0&&output.Contains("copied.epub"),"single script works in another folder without the generator or companion files");}
    }
    static void StandaloneApply() {
        string root=Fixture("embedded-apply");
        Put(root,"Books\\one.epub","A");Snapshot s=Capture(root);string script=Engine.Full(root,"Restore.cmd");Write(s,script);
        Move(root,"Books\\one.epub","copied.epub");
        string helper=Path.GetFullPath("tests\\StandaloneIntegration.ps1");
        ProcessStartInfo start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32","WindowsPowerShell","v1.0","powershell.exe"),
            "-NoLogo -NoProfile -NonInteractive -STA -ExecutionPolicy Bypass -File "+Program.QuoteArg(helper)+" -ScriptPath "+Program.QuoteArg(script));
        start.UseShellExecute=false;start.CreateNoWindow=true;start.RedirectStandardOutput=true;start.RedirectStandardError=true;
        using(Process p=Process.Start(start)) {
            string output=p.StandardOutput.ReadToEnd(),error=p.StandardError.ReadToEnd();p.WaitForExit();Console.WriteLine(output);Console.WriteLine(error);
            Check(p.ExitCode==0,"embedded restore and generated undo work without the application");
        }
    }
    static void Scale() {
        string root=Fixture("hundreds");
        for(int i=0;i<250;i++)Put(root,"Authors\\Group"+(i%10)+"\\Book"+i+".epub","Unique ebook "+i);
        Snapshot saved=Capture(root);Dictionary<string,string> before=Tree(root);
        foreach(FileRecord f in saved.Files)Move(root,f.Path,"Subjects\\Shelf"+(f.Path.Length%7)+"\\"+Path.GetFileName(f.Path));
        Check(Engine.Preview(root,saved,null).MoveCount==250,"hundreds of files recognized");
        Apply(root,saved);Check(Equal(before,Tree(root)),"250-file restoration preserves every content hash");
    }
    static void Render() {
        using(CreatorWindow f=new CreatorWindow("D:\\Ebooks")) {
            f.StartPosition=FormStartPosition.Manual;f.Location=new Point(-32000,-32000);f.ShowInTaskbar=false;f.Opacity=0;f.Show();
            for(int i=0;i<300;i++){Application.DoEvents();System.Threading.Thread.Sleep(10);}f.PerformLayout();using(Bitmap b=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height));b.Save("build\\creator.png");}
        }
        string root=Fixture("preview-ui");Put(root,"By Author\\Book.epub","Book");Snapshot s=Capture(root);Move(root,"By Author\\Book.epub","By Subject\\Renamed.epub");Put(root,"New.epub","new");
        using(RestoreWindow f=new RestoreWindow(root,s,code)) {
            DataGridView grid=(DataGridView)typeof(RestoreWindow).GetField("grid",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(f);
            foreach(PlanItem item in Engine.Preview(root,s,null).Items)grid.Rows.Add(item.Status,item.Source,item.Destination,item.Detail);
            f.StartPosition=FormStartPosition.Manual;f.Location=new Point(-32000,-32000);f.ShowInTaskbar=false;f.Opacity=0;f.Show();
            for(int i=0;i<300;i++){Application.DoEvents();System.Threading.Thread.Sleep(10);}f.PerformLayout();using(Bitmap b=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height));b.Save("build\\restore-preview.png");}
        }
        Check(File.Exists("build\\creator.png")&&File.Exists("build\\restore-preview.png"),"creator and restore preview forms render");
    }
}
