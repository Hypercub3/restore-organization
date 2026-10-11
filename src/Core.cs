using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32.SafeHandles;

namespace RestoreOrganization {
public class FileRecord {
    public string Path {get;set;}
    public string Hash {get;set;}
    public long Length {get;set;}
    public string Identity {get;set;}
    public long Created {get;set;}
}
public class Snapshot {
    public int Version {get;set;}
    public string Name {get;set;}
    public string CreatedUtc {get;set;}
    public string FolderHint {get;set;}
    public List<FileRecord> Files {get;set;}
    public List<string> Directories {get;set;}
}
public class Scan {
    public List<FileRecord> Files = new List<FileRecord>();
    public List<string> Directories = new List<string>();
    public List<string> Errors = new List<string>();
}
public class PlanItem {
    public string Status {get;set;}
    public string Source {get;set;}
    public string Destination {get;set;}
    public string Detail {get;set;}
    public FileRecord Current {get;set;}
}
public class Plan {
    public string Root;
    public Snapshot Snapshot;
    public Scan Scan;
    public List<PlanItem> Items = new List<PlanItem>();
    public string Signature;
    public int MoveCount {get {return Items.Count(x=>x.Status=="Move");}}
    public int CaseFolderCount {get{return Items.Count(x=>x.Status=="Rename folder");}}
    public int FolderCount {get {return Snapshot.Directories.Count(x=>!Directory.Exists(Engine.Full(Root,x)) && !File.Exists(Engine.Full(Root,x)));}}
}
public class MoveRecord {
    public string Source {get;set;}
    public string Destination {get;set;}
    public string Stage {get;set;}
    public string Hash {get;set;}
    public string Identity {get;set;}
    public long Created {get;set;}
}
public class FolderRename {
    public string Source {get;set;}
    public string Destination {get;set;}
    public string Stage {get;set;}
}
public class Journal {
    public List<FolderRename> FolderRenames {get;set;}
    public int Version {get;set;}
    public string State {get;set;}
    public string SnapshotName {get;set;}
    public List<MoveRecord> Moves {get;set;}
    public List<string> OriginalDirectories {get;set;}
    public List<string> NewDirectories {get;set;}
}
public class RestoreResult {
    public string Message, ReportText, Undo;
    public int Moved;
}
public static class Engine {
    public const string StateFolder=".restore-organization";
    const string StateMarker="Restore Organization recovery data version 1";
    public const string ScriptHeader="rem Restore Organization standalone snapshot v1";
    public static Action<string> FaultHook; // In-memory fault injection for transaction tests only.
    public static string ExcludedFile;
    static readonly StringComparer CI=StringComparer.OrdinalIgnoreCase;
    public static JavaScriptSerializer Serializer() {return new JavaScriptSerializer {MaxJsonLength=64*1024*1024, RecursionLimit=128};}
    public static string Json(object value) {return Serializer().Serialize(value);}
    public static Snapshot ReadSnapshot(string json) {
        Snapshot s=Serializer().Deserialize<Snapshot>(json);
        if(s==null || s.Version!=1 || s.Files==null || s.Directories==null) throw new InvalidDataException("Unsupported or damaged snapshot.");
        HashSet<string> files=new HashSet<string>(CI), dirs=new HashSet<string>(CI);
        foreach(FileRecord f in s.Files) {
            ValidateRelative(f.Path,false);
            if(!files.Add(f.Path) || f.Hash==null || f.Hash.Length!=64 || !f.Hash.All(Uri.IsHexDigit) || f.Length<0)
                throw new InvalidDataException("Invalid file entry: "+f.Path);
        }
        foreach(string d in s.Directories) {
            ValidateRelative(d,false);
            if(!dirs.Add(d) || files.Contains(d)) throw new InvalidDataException("Duplicate or conflicting folder: "+d);
        }
        foreach(string p in files.Concat(dirs)) {
            string parent=Path.GetDirectoryName(p);
            while(!String.IsNullOrEmpty(parent)) {
                if(files.Contains(parent) || !dirs.Contains(parent)) throw new InvalidDataException("Invalid parent folder: "+parent);
                parent=Path.GetDirectoryName(parent);
            }
        }
        return s;
    }
    public static void ValidateRelative(string path,bool allowState) {
        if(String.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.IndexOf('/')>=0 || path.IndexOf(':')>=0)
            throw new InvalidDataException("Unsafe relative path: "+path);
        string[] parts=path.Split('\\');
        string[] reserved={"CON","PRN","AUX","NUL","COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9","LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"};
        foreach(string p in parts)
            if(p.Length==0 || p=="." || p==".." || p.EndsWith(".") || p.EndsWith(" ") || p.IndexOfAny(Path.GetInvalidFileNameChars())>=0 || reserved.Contains(p.Split('.')[0],CI))
                throw new InvalidDataException("Unsafe path component: "+path);
        if(!allowState && CI.Equals(parts[0],StateFolder)) throw new InvalidDataException("Snapshot contains the reserved recovery folder.");
    }
    public static string RootPath(string root) {
        string r=Path.GetFullPath(root);
        if(!Directory.Exists(r)) throw new DirectoryNotFoundException("Folder does not exist: "+r);
        NoLinks(r,r);
        return r.TrimEnd('\\')+"\\";
    }
    public static string Full(string root,string relative) {
        ValidateRelative(relative,true);
        string r=Path.GetFullPath(root).TrimEnd('\\')+"\\", p=Path.GetFullPath(Path.Combine(r,relative));
        if(!p.StartsWith(r,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Path escapes collection folder.");
        return p;
    }
    [StructLayout(LayoutKind.Sequential)] struct AttributeTag {public uint Attributes,Tag;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetFileInformationByHandleEx(SafeFileHandle h,int infoClass,out AttributeTag info,uint size);
    static bool IsCloudPoint(string path) {
        using(SafeFileHandle h=CreateFile(path,0,7,IntPtr.Zero,3,0x02200000,IntPtr.Zero)) {
            AttributeTag n;
            if(h.IsInvalid || !GetFileInformationByHandleEx(h,9,out n,8)) return false;
            return (n.Tag & 0xffff0fffU)==0x9000001aU; // CLOUD, CLOUD_1 ... CLOUD_F; not name-surrogate links.
        }
    }
    public static void NoLinks(string root,string path) {
        string r=Path.GetFullPath(root).TrimEnd('\\'), p=Path.GetFullPath(path).TrimEnd('\\');
        if(!CI.Equals(r,p) && !p.StartsWith(r+"\\",StringComparison.OrdinalIgnoreCase)) throw new IOException("Path escapes root.");
        string c=p;
        while(!String.IsNullOrEmpty(c)) {
            if((File.Exists(c)||Directory.Exists(c)) && (File.GetAttributes(c)&FileAttributes.ReparsePoint)!=0 && !IsCloudPoint(c))
                throw new IOException("Symbolic links, junctions, or unsupported reparse points are not followed: "+c);
            string parent=Path.GetDirectoryName(c); if(CI.Equals(parent,c)) break; c=parent;
        }
    }
    [StructLayout(LayoutKind.Sequential)] struct NativeInfo {
        public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME Creation,Access,Write;
        public uint Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow;
    }
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetFileInformationByHandle(SafeFileHandle h,out NativeInfo info);
    static void SetIdentity(FileRecord r,FileStream stream) {
        NativeInfo n;
        if(GetFileInformationByHandle(stream.SafeFileHandle,out n) && (n.IndexHigh!=0 || n.IndexLow!=0)) {
            r.Identity=n.Volume.ToString("x8")+":"+n.IndexHigh.ToString("x8")+n.IndexLow.ToString("x8");
            r.Created=((long)(uint)n.Creation.dwHighDateTime<<32)|(uint)n.Creation.dwLowDateTime;
        }
    }
    public static string Hash(Stream stream) {
        stream.Position=0;
        using(SHA256 sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
    }
    public static FileRecord Inspect(string root,string relative) {
        string path=Full(root,relative); NoLinks(root,path);
        using(FileStream s=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete)) {
            FileRecord r=new FileRecord {Path=relative,Length=s.Length,Hash=Hash(s)}; SetIdentity(r,s); return r;
        }
    }
    public static bool IsSnapshotScript(string path) {
        if(!path.EndsWith(".cmd",StringComparison.OrdinalIgnoreCase)) return false;
        using(StreamReader r=new StreamReader(path)) {r.ReadLine();return r.ReadLine()==ScriptHeader;}
    }
    public static Scan ScanFolder(string root,Action<string> progress) {
        root=RootPath(root); CheckState(root); Scan result=new Scan(); Queue<string> queue=new Queue<string>(); queue.Enqueue(root);
        while(queue.Count>0) {
            string folder=queue.Dequeue();string[] entries;
            try {NoLinks(root,folder);entries=Directory.GetFileSystemEntries(folder);}
            catch(Exception e) {result.Errors.Add(folder+": "+e.Message);continue;}
            foreach(string path in entries.OrderBy(x=>x,CI)) {
                string relative=path.Substring(root.Length);
                if(CI.Equals(relative,StateFolder) || (!String.IsNullOrEmpty(ExcludedFile)&&CI.Equals(path,ExcludedFile))) continue;
                try {
                    ValidateRelative(relative,false);NoLinks(root,path);
                    if(Directory.Exists(path)) {result.Directories.Add(relative);queue.Enqueue(path);}
                    else {
                        if(IsSnapshotScript(path)) continue;
                        if(progress!=null) progress("Reading "+relative);
                        result.Files.Add(Inspect(root,relative));
                    }
                } catch(Exception e) {result.Errors.Add(relative+": "+e.Message);}
            }
        }
        result.Directories.Sort(CI);result.Files.Sort((a,b)=>CI.Compare(a.Path,b.Path));return result;
    }
    public static Snapshot Capture(string root,string name,Action<string> progress) {
        Scan scan=ScanFolder(root,progress);
        if(scan.Errors.Count>0) throw new IOException("Snapshot was not created because entries could not be read:\r\n"+String.Join("\r\n",scan.Errors));
        return FromScan(root,name,scan);
    }
    public static Snapshot FromScan(string root,string name,Scan scan) {
        return new Snapshot {Version=1,Name=name,CreatedUtc=DateTime.UtcNow.ToString("o"),FolderHint=root,Files=scan.Files,Directories=scan.Directories};
    }
    static string Key(FileRecord f) {return String.IsNullOrEmpty(f.Identity)?null:f.Identity+":"+f.Created;}
    public static Plan Preview(string root,Snapshot snapshot,Action<string> progress) {
        root=RootPath(root);ReadSnapshot(Json(snapshot));
        Plan p=new Plan {Root=root,Snapshot=snapshot,Scan=ScanFolder(root,progress)};
        Dictionary<FileRecord,FileRecord> matches=new Dictionary<FileRecord,FileRecord>();HashSet<FileRecord> used=new HashSet<FileRecord>();
        // Identity matches run globally first, so hash matching cannot steal another file's identity.
        foreach(FileRecord old in snapshot.Files) {
            string key=Key(old); if(key==null || snapshot.Files.Count(x=>Key(x)==key)!=1) continue;
            List<FileRecord> candidates=p.Scan.Files.Where(x=>Key(x)==key).ToList();
            if(candidates.Count==1) {matches[old]=candidates[0];used.Add(candidates[0]);}
        }
        foreach(FileRecord old in snapshot.Files.Where(x=>!matches.ContainsKey(x))) {
            FileRecord c=p.Scan.Files.FirstOrDefault(x=>!used.Contains(x)&&CI.Equals(x.Path,old.Path)&&x.Hash==old.Hash);
            if(c!=null) {matches[old]=c;used.Add(c);}
        }
        foreach(FileRecord old in snapshot.Files.Where(x=>!matches.ContainsKey(x))) {
            List<FileRecord> candidates=p.Scan.Files.Where(x=>!used.Contains(x)&&x.Hash==old.Hash).ToList();
            int remaining=snapshot.Files.Count(x=>!matches.ContainsKey(x)&&x.Hash==old.Hash);
            if(candidates.Count==1 && remaining==1) {matches[old]=candidates[0];used.Add(candidates[0]);}
        }
        foreach(FileRecord old in snapshot.Files) {
            FileRecord current;
            if(matches.TryGetValue(old,out current))
                p.Items.Add(new PlanItem {Status=CI.Equals(old.Path,current.Path)&&String.Equals(Path.GetFileName(old.Path),Path.GetFileName(current.Path),StringComparison.Ordinal)?"Already correct":"Move",
                    Source=current.Path,Destination=old.Path,Current=current,Detail=current.Hash==old.Hash?"":"Edited content preserved (same Windows file identity)."});
            else {
                bool ambiguous=p.Scan.Files.Any(x=>!used.Contains(x)&&x.Hash==old.Hash);
                p.Items.Add(new PlanItem {Status=ambiguous?"Ambiguous":"Missing / unidentified",Source="",Destination=old.Path,
                    Detail=ambiguous?"Identical copies cannot be distinguished safely.":"Missing, or edited and replaced so identity cannot be confirmed."});
            }
        }
        bool changed;
        do {
            changed=false;HashSet<string> moving=new HashSet<string>(p.Items.Where(x=>x.Status=="Move").Select(x=>x.Source),CI);
            foreach(PlanItem item in p.Items.Where(x=>x.Status=="Move")) {
                string target=Full(root,item.Destination), conflict=null;
                try {
                    NoLinks(root,target);
                    if(Directory.Exists(target)) conflict="A folder occupies the file destination.";
                    else if(File.Exists(target)&&!moving.Contains(item.Destination)) conflict="Destination is occupied by a file that will remain there.";
                    string parent=Path.GetDirectoryName(item.Destination);
                    while(!String.IsNullOrEmpty(parent)) {
                        if(File.Exists(Full(root,parent))&&!moving.Contains(parent)) conflict="A file blocks a parent folder: "+parent;
                        parent=Path.GetDirectoryName(parent);
                    }
                } catch(Exception e) {conflict=e.Message;}
                if(conflict!=null) {item.Status="Conflict";item.Detail=conflict;changed=true;}
            }
        } while(changed);
        foreach(FileRecord f in p.Scan.Files.Where(x=>!used.Contains(x)))
            p.Items.Add(new PlanItem {Status="New / unmatched",Source=f.Path,Destination="",Detail="Leave in place.",Current=f});
        foreach(string d in snapshot.Directories) {
            string actual=p.Scan.Directories.FirstOrDefault(x=>CI.Equals(x,d));
            if(actual!=null&&!String.Equals(Path.GetFileName(actual),Path.GetFileName(d),StringComparison.Ordinal))
                p.Items.Add(new PlanItem {Status="Rename folder",Source=actual,Destination=d,Detail="Restore folder-name capitalization; preserve its contents."});
            string target=Full(root,d);
            if(File.Exists(target)&&!p.Items.Any(x=>x.Status=="Move"&&CI.Equals(x.Source,d)))
                p.Items.Add(new PlanItem {Status="Folder conflict",Source="",Destination=d,Detail="An existing file blocks this folder."});
        }
        foreach(string e in p.Scan.Errors) p.Items.Add(new PlanItem {Status="Unreadable",Source="",Destination="",Detail=e});
        p.Signature=Json(p.Items.Select(x=>new {x.Status,x.Source,x.Destination,Hash=x.Current==null?"":x.Current.Hash,Identity=x.Current==null?"":Key(x.Current)}).ToArray())+Json(p.Scan.Directories);
        return p;
    }
    static void CheckState(string root) {
        string state=Full(root,StateFolder);NoLinks(root,state);
        if(File.Exists(state))throw new IOException("A file occupies the reserved recovery folder: "+state);
        if(!Directory.Exists(state))return;
        string marker=Path.Combine(state,"owner.txt");NoLinks(root,marker);
        if(!File.Exists(marker)||new FileInfo(marker).Length>512||File.ReadAllText(marker,Encoding.UTF8)!=StateMarker)
            throw new IOException("An existing .restore-organization folder is not recognized as this application's recovery data. Move or rename that folder before using snapshots.");
    }
    public sealed class CollectionLock:IDisposable {
        readonly string root;
        Mutex mutex;
        FileStream file;
        internal CollectionLock(string folder,Mutex gate,FileStream guard) {root=folder;mutex=gate;file=guard;}
        public void Dispose() {
            if(mutex==null)return;
            try {
                CleanupFinished(root);
            } finally {
                try {
                    RemoveIdleState(root,file);
                } finally {
                    file.Dispose();file=null;
                    mutex.ReleaseMutex();mutex.Dispose();mutex=null;
                }
            }
        }
    }
    static string LockName(string root) {
        string identity=RootPath(root).ToUpperInvariant();
        // Physical directory identity also coordinates drive aliases and renamed collection roots.
        using(SafeFileHandle h=CreateFile(root,0,7,IntPtr.Zero,3,0x02000000,IntPtr.Zero)) {
            NativeInfo n;
            if(!h.IsInvalid&&GetFileInformationByHandle(h,out n)&&(n.IndexHigh!=0||n.IndexLow!=0))
                identity=n.Volume.ToString("x8")+":"+n.IndexHigh.ToString("x8")+n.IndexLow.ToString("x8")+":"+n.Creation.dwHighDateTime+":"+n.Creation.dwLowDateTime;
        }
        using(SHA256 sha=SHA256.Create())
            return "Global\\RestoreOrganization-"+BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-","");
    }
    public static CollectionLock Lock(string root) {
        root=RootPath(root);
        Mutex mutex=new Mutex(false,LockName(root));bool acquired=false;
        FileStream guard=null;
        try {
            try {acquired=mutex.WaitOne(0);} catch(AbandonedMutexException) {acquired=true;}
            if(!acquired)throw new IOException("Another snapshot or restore is using this folder. Close it and try again.");
            string state=Full(root,StateFolder);NoLinks(root,state);
            if(Directory.Exists(state))CheckState(root);
            else {
                Directory.CreateDirectory(state);
                string marker=Path.Combine(state,"owner.txt");
                using(FileStream s=new FileStream(marker,FileMode.CreateNew,FileAccess.Write,FileShare.Read)) {
                    byte[] bytes=Encoding.UTF8.GetBytes(StateMarker);s.Write(bytes,0,bytes.Length);s.Flush(true);
                }
            }
            string path=Full(root,StateFolder+"\\operation.lock");NoLinks(root,path);
            // Retain the old file lock during operations so older scripts are still detected.
            try {guard=new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
            catch(IOException) {throw new IOException("Another snapshot or restore is using this folder. Close it and try again.");}
            CleanupFinished(root);return new CollectionLock(root,mutex,guard);
        } catch {
            if(guard!=null)guard.Dispose();
            if(acquired)mutex.ReleaseMutex();
            mutex.Dispose();throw;
        }
    }
    static void RemoveIdleState(string root,FileStream guard) {
        string state=Full(root,StateFolder),marker=Path.Combine(state,"owner.txt");
        bool markerRemoved=false;
        try {
            CheckState(root);if(!Directory.Exists(state))return;
            string lockPath=Path.Combine(state,"operation.lock");
            foreach(string entry in Directory.GetFileSystemEntries(state)) {
                NoLinks(root,entry);
                if(Directory.Exists(entry)||(!CI.Equals(entry,marker)&&!CI.Equals(entry,lockPath)))return;
            }
            if(guard.Length!=0)return;
            // Remove ownership while the legacy file lock is still held: older scripts
            // cannot pass their ownership check and start a restore during folder removal.
            File.Delete(marker);markerRemoved=true;
            guard.Dispose();
            if(File.Exists(lockPath))File.Delete(lockPath);
            Directory.Delete(state,false);
        } catch(IOException) {} catch(UnauthorizedAccessException) {}
        finally {
            // A failed removal must leave recognizable data, including any new obstruction.
            if(markerRemoved) {
                try {
                    NoLinks(root,state);NoLinks(root,marker);
                    if(Directory.Exists(state)&&!File.Exists(marker)) {
                        using(FileStream s=new FileStream(marker,FileMode.CreateNew,FileAccess.Write,FileShare.Read)) {
                            byte[] bytes=Encoding.UTF8.GetBytes(StateMarker);s.Write(bytes,0,bytes.Length);s.Flush(true);
                        }
                    }
                } catch(IOException) {} catch(UnauthorizedAccessException) {}
            }
        }
    }
    public static Journal ReadJournal(string root,string path) {
        NoLinks(root,path);Journal j=Serializer().Deserialize<Journal>(File.ReadAllText(path,Encoding.UTF8));
        if(j==null||j.Version!=1||j.Moves==null||j.OriginalDirectories==null||j.NewDirectories==null) throw new InvalidDataException("Damaged recovery journal. Keep all recovery data: "+path);
        string stagePrefix=Path.GetDirectoryName(path).Substring(RootPath(root).Length)+"\\stage\\";
        HashSet<string> sources=new HashSet<string>(CI),targets=new HashSet<string>(CI),stages=new HashSet<string>(CI);
        foreach(MoveRecord m in j.Moves) {
            ValidateRelative(m.Source,false);ValidateRelative(m.Destination,false);ValidateRelative(m.Stage,true);
            if(!m.Stage.StartsWith(stagePrefix,StringComparison.OrdinalIgnoreCase)||!sources.Add(m.Source)||!targets.Add(m.Destination)||!stages.Add(m.Stage)||
                m.Hash==null||m.Hash.Length!=64||!m.Hash.All(Uri.IsHexDigit)) throw new InvalidDataException("Unsafe recovery record.");
        }
        if(j.FolderRenames==null)j.FolderRenames=new List<FolderRename>();
        foreach(FolderRename d in j.FolderRenames) {
            ValidateRelative(d.Source,false);ValidateRelative(d.Destination,false);ValidateRelative(d.Stage,true);
            if(!CI.Equals(d.Source,d.Destination)||!d.Stage.StartsWith(Path.GetDirectoryName(path).Substring(RootPath(root).Length)+"\\foldercase\\",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Unsafe folder rename in recovery journal.");
        }
        foreach(string d in j.OriginalDirectories.Concat(j.NewDirectories)) ValidateRelative(d,false);
        return j;
    }
    public static List<string> Pending(string root) {
        CheckState(root);
        string runs=Full(root,StateFolder+"\\runs");NoLinks(root,runs);List<string> result=new List<string>();
        if(!Directory.Exists(runs)) return result;
        foreach(string dir in Directory.GetDirectories(runs)) {
            NoLinks(root,dir);string path=Path.Combine(dir,"journal.json");NoLinks(root,path);if(!File.Exists(path)) continue;
            Journal j=ReadJournal(root,path);
            if(j.State!="Completed"&&j.State!="Recovered") result.Add(path);
        }
        return result.OrderBy(x=>x,StringComparer.Ordinal).ToList();
    }
    static void DurableWrite(string path,string text) {
        string temp=path+".tmp";
        using(FileStream s=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)) {
            byte[] bytes=new UTF8Encoding(false).GetBytes(text);s.Write(bytes,0,bytes.Length);s.Flush(true);
        }
        if(File.Exists(path)) File.Replace(temp,path,null);else File.Move(temp,path);
    }
    static void Checkpoint(string path,Journal j,string eventName) {DurableWrite(path,Json(j));if(FaultHook!=null) FaultHook(eventName);}
    // Called only while holding the collection lock. Retain anything uncertain.
    static void CleanupFinished(string root) {
        string runs=Full(root,StateFolder+"\\runs");
        try {
            NoLinks(root,runs);if(!Directory.Exists(runs))return;
            foreach(string run in Directory.GetDirectories(runs)) {
                try {
                    NoLinks(root,run);string path=Path.Combine(run,"journal.json");NoLinks(root,path);
                    if(!File.Exists(path))continue;
                    Journal j=ReadJournal(root,path);
                    if(j.State!="Completed"&&j.State!="Recovered")continue;
                    string[] entries=Directory.GetFileSystemEntries(run);
                    bool safe=true;
                    foreach(string entry in entries) {
                        NoLinks(root,entry);string name=Path.GetFileName(entry);
                        if(Directory.Exists(entry)) {
                            if((name!="stage"&&name!="foldercase")||Directory.GetFileSystemEntries(entry).Length!=0)safe=false;
                        } else if(name!="journal.json"&&name!="report.txt"&&name!="journal.json.tmp"&&name!="report.txt.tmp")safe=false;
                    }
                    if(!safe)continue;
                    // No recursive deletion: leftover staged files or unknown entries preserve the whole run.
                    foreach(string entry in entries.Where(x=>x!=path)) {
                        if(Directory.Exists(entry))Directory.Delete(entry,false);else File.Delete(entry);
                    }
                    File.Delete(path);Directory.Delete(run,false);
                } catch(IOException) {} catch(UnauthorizedAccessException) {} catch(ArgumentException) {} catch(InvalidOperationException) {}
            }
            if(Directory.GetFileSystemEntries(runs).Length==0)Directory.Delete(runs,false);
        } catch(IOException) {} catch(UnauthorizedAccessException) {}
    }
    static void MoveNoOverwrite(string root,string source,string destination) {
        NoLinks(root,source);NoLinks(root,destination);
        for(int attempt=0;;attempt++) {
            try {File.Move(source,destination);return;}
            catch(IOException) {if(attempt==2||File.Exists(destination)||Directory.Exists(destination)) throw;Thread.Sleep(250*(attempt+1));}
        }
    }
    public static RestoreResult Restore(Plan approved,Action<Snapshot,string> writeScript,Action<string> progress) {
        string root=RootPath(approved.Root);
        using(CollectionLock guard=Lock(root)) {
            if(Pending(root).Count>0) throw new IOException("An interrupted restore must be recovered first.");
            Plan plan=Preview(root,approved.Snapshot,progress);
            if(plan.Signature!=approved.Signature) throw new IOException("The folder changed after preview. Refresh the preview before restoring.");
            if(plan.Scan.Errors.Count>0) throw new IOException("Unreadable entries prevent a complete undo snapshot. Resolve the listed errors and refresh.");
            string runId=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N");
            string runRelative=StateFolder+"\\runs\\"+runId,run=Full(root,runRelative);
            NoLinks(root,run);Directory.CreateDirectory(Path.Combine(run,"stage"));
            string undo=Path.Combine(root,"Undo - "+runId+".cmd");writeScript(FromScan(root,"Undo before "+plan.Snapshot.Name,plan.Scan),undo);
            List<PlanItem> moves=plan.Items.Where(x=>x.Status=="Move").ToList();
            Journal j=new Journal {FolderRenames=new List<FolderRename>(),Version=1,State="Prepared",SnapshotName=plan.Snapshot.Name,OriginalDirectories=plan.Scan.Directories.ToList(),
                NewDirectories=plan.Snapshot.Directories.Where(x=>!Directory.Exists(Full(root,x))).ToList(),
                Moves=moves.Select((x,i)=>new MoveRecord {Source=x.Source,Destination=x.Destination,Stage=runRelative+"\\stage\\"+i.ToString("D6"),
                    Hash=x.Current.Hash,Identity=x.Current.Identity,Created=x.Current.Created}).ToList()};
            string journalPath=Path.Combine(run,"journal.json");DurableWrite(journalPath,Json(j));
            List<FileStream> handles=new List<FileStream>();bool completed=false;
            try {
                foreach(MoveRecord m in j.Moves) {
                    string source=Full(root,m.Source);NoLinks(root,source);
                    FileStream h=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);handles.Add(h);
                    FileRecord r=new FileRecord();SetIdentity(r,h);
                    if(Hash(h)!=m.Hash||(!String.IsNullOrEmpty(m.Identity)&&(r.Identity!=m.Identity||r.Created!=m.Created)))
                        throw new IOException("A file changed during preparation: "+m.Source);
                }
                j.State="Staging";Checkpoint(journalPath,j,"before-stage");
                foreach(MoveRecord m in j.Moves) {
                    if(progress!=null) progress("Preparing "+m.Source);
                    MoveNoOverwrite(root,Full(root,m.Source),Full(root,m.Stage));Checkpoint(journalPath,j,"staged");
                }
                j.State="Placing";Checkpoint(journalPath,j,"before-place");
                foreach(string d in plan.Snapshot.Directories.OrderBy(x=>x.Length)) {
                    string folder=Full(root,d);NoLinks(root,folder);
                    if(HasFileAncestor(root,d)) continue;
                    if(Directory.Exists(folder)) {
                        string actual=Directory.GetDirectories(Path.GetDirectoryName(folder)).FirstOrDefault(x=>CI.Equals(Path.GetFileName(x),Path.GetFileName(folder)));
                        if(actual!=null&&!String.Equals(Path.GetFileName(actual),Path.GetFileName(folder),StringComparison.Ordinal)) {
                            string stage=runRelative+"\\foldercase\\"+j.FolderRenames.Count.ToString("D6");
                            Directory.CreateDirectory(Full(root,runRelative+"\\foldercase"));
                            j.FolderRenames.Add(new FolderRename {Source=actual.Substring(root.Length),Destination=d,Stage=stage});
                            Checkpoint(journalPath,j,"before-folder-case");
                            MoveDirectory(root,actual,Full(root,stage));Checkpoint(journalPath,j,"folder-case-staged");
                            MoveDirectory(root,Full(root,stage),folder);Checkpoint(journalPath,j,"folder-case-placed");
                        }
                    } else Directory.CreateDirectory(folder);
                }
                foreach(MoveRecord m in j.Moves) {
                    if(progress!=null) progress("Restoring "+m.Destination);
                    string target=Full(root,m.Destination);NoLinks(root,target);Directory.CreateDirectory(Path.GetDirectoryName(target));
                    MoveNoOverwrite(root,Full(root,m.Stage),target);Checkpoint(journalPath,j,"placed");
                }
                foreach(MoveRecord m in j.Moves)
                    if(Inspect(root,m.Destination).Hash!=m.Hash) throw new IOException("Content verification failed: "+m.Destination);
                HashSet<string> keep=new HashSet<string>(plan.Snapshot.Directories,CI),cleanup=new HashSet<string>(CI);
                foreach(MoveRecord m in j.Moves) {
                    string d=Path.GetDirectoryName(m.Source);
                    while(!String.IsNullOrEmpty(d)) {if(!keep.Contains(d))cleanup.Add(d);d=Path.GetDirectoryName(d);}
                }
                foreach(string d in cleanup.OrderByDescending(x=>x.Length)) DeleteEmpty(root,d);
                j.State="Completed";DurableWrite(journalPath,Json(j));completed=true;
                StringBuilder log=new StringBuilder();
                log.AppendLine("Snapshot: "+plan.Snapshot.Name).AppendLine("Folder: "+root).AppendLine("UTC: "+DateTime.UtcNow.ToString("o"));
                log.AppendLine("Moved and verified: "+moves.Count).AppendLine("Undo script: "+undo).AppendLine();
                foreach(PlanItem item in plan.Items) log.AppendLine(item.Status+" | "+item.Source+" -> "+item.Destination+" | "+item.Detail);
                return new RestoreResult {Moved=moves.Count,Undo=undo,ReportText=log.ToString(),
                    Message="Restoration completed. "+moves.Count+" files moved and content verified.\r\nSkipped and unmatched entries are listed in the report.\r\nAn independent undo script was saved in the collection folder."};
            } catch(Exception original) {
                foreach(FileStream h in handles) h.Dispose();handles.Clear();
                if(completed) throw new IOException("Files were restored and verified, but preparing the report failed: "+original.Message,original);
                try {RecoverOne(root,journalPath,progress);}
                catch(Exception recovery) {
                    throw new IOException("Restore stopped. Automatic recovery could not finish. Keep .restore-organization and use Recover interrupted restore.\r\nCause: "+original.Message+"\r\nRecovery: "+recovery.Message,original);
                }
                throw new IOException("Restore stopped and moved files were returned to their previous locations.\r\n"+original.Message,original);
            } finally {foreach(FileStream h in handles) h.Dispose();CleanupFinished(root);}
        }
    }
    static IEnumerable<string> SafeEntries(string root,string folder) {
        Queue<string> queue=new Queue<string>();queue.Enqueue(folder);
        while(queue.Count>0) {
            string current=queue.Dequeue();NoLinks(root,current);
            foreach(string entry in Directory.GetFileSystemEntries(current)) {
                NoLinks(root,entry);yield return entry;
                if(Directory.Exists(entry))queue.Enqueue(entry);
            }
        }
    }
    static void MoveDirectory(string root,string source,string target) {
        NoLinks(root,source);NoLinks(root,target);
        Directory.Move(source,target);
    }
    static bool HasFileAncestor(string root,string relative) {
        string d=relative;while(!String.IsNullOrEmpty(d)) {if(File.Exists(Full(root,d)))return true;d=Path.GetDirectoryName(d);}return false;
    }
    static void DeleteEmpty(string root,string relative) {
        string path=Full(root,relative);NoLinks(root,path);
        if(Directory.Exists(path)&&Directory.GetFileSystemEntries(path).Length==0) Directory.Delete(path,false);
    }
    public static void Recover(string root,Action<string> progress) {
        root=RootPath(root);using(CollectionLock guard=Lock(root)) {
            try {foreach(string path in Pending(root)) RecoverOne(root,path,progress);}
            finally {CleanupFinished(root);}
        }
    }
    static void RecoverOne(string root,string path,Action<string> progress) {
        Journal j=ReadJournal(root,path);
        // Restore case-only directory renames before locating participating files.
        foreach(FolderRename d in j.FolderRenames.AsEnumerable().Reverse()) {
            string temp=Full(root,d.Stage),original=Full(root,d.Source),target=Full(root,d.Destination);
            if(Directory.Exists(temp)) {
                if(Directory.Exists(original)||File.Exists(original))throw new IOException("A new entry blocks folder recovery: "+d.Source);
                MoveDirectory(root,temp,original);
            } else if(Directory.Exists(target)) {
                MoveDirectory(root,target,temp);MoveDirectory(root,temp,original);
            } else throw new IOException("Cannot locate folder during recovery: "+d.Source);
            DurableWrite(path,Json(j));
        }
        Dictionary<MoveRecord,string> located=new Dictionary<MoveRecord,string>();HashSet<string> used=new HashSet<string>(CI);
        foreach(MoveRecord m in j.Moves) {
            List<FileRecord> candidates=new List<FileRecord>();
            foreach(string relative in new[]{m.Stage,m.Source,m.Destination}.Distinct(CI)) {
                if(used.Contains(relative)||!File.Exists(Full(root,relative)))continue;
                FileRecord f=Inspect(root,relative);if(f.Hash==m.Hash)candidates.Add(f);
            }
            List<FileRecord> byId=candidates.Where(x=>!String.IsNullOrEmpty(m.Identity)&&x.Identity==m.Identity&&x.Created==m.Created).ToList();
            FileRecord found=byId.Count==1?byId[0]:candidates.Count==1?candidates[0]:null;
            if(found==null) throw new IOException("Cannot safely locate a recovery file: "+m.Source+". Recovery data remains at "+path);
            located[m]=found.Path;used.Add(found.Path);
        }
        foreach(MoveRecord m in j.Moves) {
            string target=Full(root,m.Source);NoLinks(root,target);
            if(File.Exists(target)&&!used.Contains(m.Source)) throw new IOException("A new file blocks recovery: "+m.Source);
            if(Directory.Exists(target)) {
                if(!j.NewDirectories.Contains(m.Source,CI))throw new IOException("A folder blocks recovery: "+m.Source);
                foreach(string entry in SafeEntries(root,target)) {
                    NoLinks(root,entry);
                    string relative=entry.Substring(RootPath(root).Length);
                    if(File.Exists(entry)&&!used.Contains(relative))throw new IOException("An unrelated file blocks recovery: "+relative);
                    if(Directory.Exists(entry)&&!j.NewDirectories.Contains(relative,CI))throw new IOException("An unrelated folder blocks recovery: "+relative);
                }
            }
        }
        j.State="Recovering";DurableWrite(path,Json(j));
        foreach(MoveRecord m in j.Moves) {
            if(progress!=null)progress("Recovering "+m.Source);
            if(!CI.Equals(located[m],m.Stage))MoveNoOverwrite(root,Full(root,located[m]),Full(root,m.Stage));
            DurableWrite(path,Json(j));
        }
        foreach(string d in j.NewDirectories.OrderByDescending(x=>x.Length)) DeleteEmpty(root,d);
        foreach(string d in j.OriginalDirectories.OrderBy(x=>x.Length)) {NoLinks(root,Full(root,d));Directory.CreateDirectory(Full(root,d));}
        foreach(MoveRecord m in j.Moves) {
            string target=Full(root,m.Source);NoLinks(root,target);Directory.CreateDirectory(Path.GetDirectoryName(target));
            MoveNoOverwrite(root,Full(root,m.Stage),target);DurableWrite(path,Json(j));
            if(Inspect(root,m.Source).Hash!=m.Hash) throw new IOException("Recovery verification failed: "+m.Source);
        }
        foreach(string d in j.NewDirectories.OrderByDescending(x=>x.Length)) DeleteEmpty(root,d);
        j.State="Recovered";DurableWrite(path,Json(j));
    }
    public static void WriteScript(Snapshot snapshot,string path,string sourceCode) {
        ReadSnapshot(Json(snapshot));
        string source=Convert.ToBase64String(Encoding.UTF8.GetBytes(sourceCode)),data=Convert.ToBase64String(Encoding.UTF8.GetBytes(Json(snapshot)));
        string payload="$ErrorActionPreference='Stop'\r\nAdd-Type -ReferencedAssemblies System.Windows.Forms,System.Drawing,System.Web.Extensions -TypeDefinition ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('"+source+"')))\r\n"+
            "$json=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('"+data+"'))\r\n"+
            "$root=[IO.Path]::GetDirectoryName($env:RO_SELF)\r\n"+
            "if($env:RO_HEADLESS -eq 'preview') { [RestoreOrganization.Engine]::Json([RestoreOrganization.Engine]::Preview($root,[RestoreOrganization.Engine]::ReadSnapshot($json),$null).Items); return }\r\n"+
            "[RestoreOrganization.RestoreWindow]::Run($root,$json,[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('"+source+"')))\r\n";
        string compressed;
        using(MemoryStream buffer=new MemoryStream()) {
            using(System.IO.Compression.GZipStream gzip=new System.IO.Compression.GZipStream(buffer,System.IO.Compression.CompressionMode.Compress,true)) {
                byte[] bytes=Encoding.UTF8.GetBytes(payload);gzip.Write(bytes,0,bytes.Length);
            }
            compressed=Convert.ToBase64String(buffer.ToArray());
        }
        string script="@echo off\r\n"+ScriptHeader+"\r\nsetlocal DisableDelayedExpansion\r\nset \"RO_SELF=%~f0\"\r\n"+
            "\"%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\" -NoLogo -NoProfile -STA -ExecutionPolicy Bypass -Command \"try { $t=[IO.File]::ReadAllText($env:RO_SELF); $b=($t -split '\\r?\\n:RO_PAYLOAD\\r?\\n',2)[1]; $m=New-Object IO.MemoryStream(,[Convert]::FromBase64String($b)); $g=New-Object IO.Compression.GZipStream($m,[IO.Compression.CompressionMode]::Decompress); $r=New-Object IO.StreamReader($g,[Text.Encoding]::UTF8); $code=$r.ReadToEnd(); $r.Dispose(); & ([scriptblock]::Create($code)) } catch { Write-Host $_.Exception.Message; if($env:RO_HEADLESS -ne 'preview') { Read-Host 'Press Enter to close' }; exit 1 }\"\r\n"+
            "exit /b %errorlevel%\r\n:RO_PAYLOAD\r\n"+compressed+"\r\n";
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            using(FileStream s=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                byte[] bytes=Encoding.ASCII.GetBytes(script);s.Write(bytes,0,bytes.Length);s.Flush(true);
            }
            File.Move(temporary,path);
        } finally {
            if(File.Exists(temporary))File.Delete(temporary);
        }
    }
}
}

