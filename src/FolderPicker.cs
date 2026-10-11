using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RestoreOrganization {
public static class FolderPicker {
    const uint PickFolders=0x20, ForceFileSystem=0x40, NoChangeDirectory=0x8, PathMustExist=0x800;
    const uint FileSystemPath=0x80058000;
    const int Cancelled=unchecked((int)0x800704C7);

    public static string Choose(IWin32Window owner,string currentFolder) {
        IFileDialog dialog=null;IShellItem initial=null,result=null;IntPtr path=IntPtr.Zero;
        try {
            dialog=(IFileDialog)new FileOpenDialog();
            uint options;dialog.GetOptions(out options);
            dialog.SetOptions(options|PickFolders|ForceFileSystem|NoChangeDirectory|PathMustExist);
            dialog.SetTitle("Choose the root of your collection");
            dialog.SetOkButtonLabel("Select folder");
            // An explicitly selected collection takes precedence over the dialog's saved location.
            string existing=ExistingFolder(currentFolder);
            if(existing!=null) {
                Guid itemId=typeof(IShellItem).GUID;
                int created=SHCreateItemFromParsingName(existing,IntPtr.Zero,ref itemId,out initial);
                if(created>=0)dialog.SetFolder(initial);
            }
            int shown=dialog.Show(owner==null?IntPtr.Zero:owner.Handle);
            if(shown==Cancelled)return null;
            Marshal.ThrowExceptionForHR(shown);
            dialog.GetResult(out result);
            result.GetDisplayName(FileSystemPath,out path);
            string selected=Marshal.PtrToStringUni(path);
            if(String.IsNullOrEmpty(selected)||!Directory.Exists(selected))
                throw new IOException("The selected folder is no longer available. Choose an existing folder on an accessible drive.");
            return selected;
        } finally {
            if(path!=IntPtr.Zero)Marshal.FreeCoTaskMem(path);
            Release(result);Release(initial);Release(dialog);
        }
    }
    static string ExistingFolder(string path) {
        if(String.IsNullOrWhiteSpace(path))return null;
        try {string full=Path.GetFullPath(path);return Directory.Exists(full)?full:null;}
        catch(ArgumentException){return null;}
        catch(NotSupportedException){return null;}
        catch(PathTooLongException){return null;}
        catch(System.Security.SecurityException){return null;}
    }
    static void Release(object value) {
        if(value!=null&&Marshal.IsComObject(value))Marshal.FinalReleaseComObject(value);
    }

    [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=true)]
    static extern int SHCreateItemFromParsingName(string path,IntPtr bindingContext,ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    [ComImport,Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
    internal class FileOpenDialog {}

    // Complete native vtable order, including inherited IModalWindow.Show.
    [ComImport,Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFileDialog {
        [PreserveSig] int Show(IntPtr owner);
        void SetFileTypes(uint count,IntPtr filters);
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(IntPtr events,out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(uint options);
        void GetOptions(out uint options);
        void SetDefaultFolder(IShellItem folder);
        void SetFolder(IShellItem folder);
        void GetFolder(out IShellItem folder);
        void GetCurrentSelection(out IShellItem item);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName(out IntPtr name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetResult(out IShellItem item);
        void AddPlace(IShellItem folder,uint alignment);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
        void Close(int result);
        void SetClientGuid(ref Guid id);
        void ClearClientData();
        void SetFilter(IntPtr filter);
    }
    [ComImport,Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem {
        void BindToHandler(IntPtr bindingContext,ref Guid handler,ref Guid interfaceId,out IntPtr value);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint format,out IntPtr name);
        void GetAttributes(uint mask,out uint attributes);
        void Compare(IShellItem other,uint hint,out int order);
    }
}
}
