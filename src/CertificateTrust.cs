using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using Microsoft.Win32;

// This elevated helper can trust only the public certificate embedded at build time.
// No certificate, file, registry path, or command supplied by the caller is accepted.
static class CertificateTrust {
    static X509Certificate2 Certificate() {
        using(Stream s=Assembly.GetExecutingAssembly().GetManifestResourceStream("MenuCertificate"))
        using(MemoryStream m=new MemoryStream()) { s.CopyTo(m); return new X509Certificate2(m.ToArray()); }
    }
    static string NativePowerShell() {
        string win=Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return Path.Combine(win, Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess ? "Sysnative" : "System32",
            "WindowsPowerShell","v1.0","powershell.exe");
    }
    public static int Main(string[] args) {
        if(args.Length!=1)return 2;
        using(X509Certificate2 cert=Certificate()) {
            if(args[0]=="--thumbprint"){Console.WriteLine(cert.Thumbprint);return 0;}
            if(args[0]!="--install" && args[0]!="--remove")return 2;
            if(!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))return 12;
            if(args[0]=="--install" && (DateTime.Now<cert.NotBefore || DateTime.Now>cert.NotAfter))return 11;
            string key=@"SOFTWARE\RestoreOrganization\ContextMenuCertificates\"+cert.Thumbprint;
            try {
                using(var machine=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64))
                using(var store=new X509Store(StoreName.TrustedPeople,StoreLocation.LocalMachine)) {
                    store.Open(OpenFlags.ReadWrite);
                    if(args[0]=="--install") {
                        if(store.Certificates.Find(X509FindType.FindByThumbprint,cert.Thumbprint,false).Count>0)return 0;
                        store.Add(cert);
                        try { using(var marker=machine.CreateSubKey(key))marker.SetValue("AddedByRestoreOrganization",1,RegistryValueKind.DWord); }
                        catch {store.Remove(cert);return 14;}
                        return 0;
                    }
                    using(var marker=machine.OpenSubKey(key)) {
                        if(marker==null || !Object.Equals(marker.GetValue("AddedByRestoreOrganization"),1))return 0;
                    }
                    // Trust is shared by Windows accounts. Retain it while another account uses the menu.
                    var info=new ProcessStartInfo(NativePowerShell(),
                        "-NoLogo -NoProfile -NonInteractive -Command \"try { if (@(Get-AppxPackage -AllUsers -Name 'Phill.RestoreOrganization.ContextMenu' -ErrorAction Stop).Count) { exit 3 }; exit 0 } catch { exit 13 }\"");
                    info.UseShellExecute=false;info.CreateNoWindow=true;
                    using(var p=Process.Start(info)){p.WaitForExit();if(p.ExitCode!=0)return p.ExitCode;}
                    foreach(X509Certificate2 c in store.Certificates.Find(X509FindType.FindByThumbprint,cert.Thumbprint,false))store.Remove(c);
                    machine.DeleteSubKey(key,false);
                    return 0;
                }
            } catch {return 10;}
        }
    }
}
