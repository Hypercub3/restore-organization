#include <windows.h>
#include <shobjidl.h>
#include <shlwapi.h>
#include <shellapi.h>
#include <new>
#include <string>

static HMODULE moduleHandle;
static long objectCount=0;
static const CLSID commandId={0x5812b34a,0x5af8,0x4c61,{0x85,0x4e,0x72,0xe3,0x3a,0x04,0xe1,0xe4}};
static std::wstring FolderFromItems(IShellItemArray* items) {
    if(!items)return L"";
    DWORD count=0; if(FAILED(items->GetCount(&count))||count!=1)return L"";
    IShellItem* item=nullptr; if(FAILED(items->GetItemAt(0,&item)))return L"";
    SFGAOF attrs=0;std::wstring path;
    if(SUCCEEDED(item->GetAttributes(SFGAO_FOLDER|SFGAO_FILESYSTEM,&attrs))&&(attrs&(SFGAO_FOLDER|SFGAO_FILESYSTEM))==(SFGAO_FOLDER|SFGAO_FILESYSTEM)) {
        PWSTR text=nullptr;if(SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH,&text))) {path=text;CoTaskMemFree(text);}
    }
    item->Release();return path;
}
static std::wstring ModuleFolder() {
    wchar_t path[32768];DWORD n=GetModuleFileNameW(moduleHandle,path,32768);
    if(!n||n>=32768)return L"";
    std::wstring p(path,n);return p.substr(0,p.find_last_of(L"\\"));
}
// Launch the complete portable copy maintained by the per-user installer.
// The signed package contains the native COM component and a fallback executable.
static std::wstring CreatorFolder() {
    wchar_t path[32768];DWORD bytes=sizeof(path);
    if(RegGetValueW(HKEY_CURRENT_USER,L"Software\\RestoreOrganization\\ContextMenu",L"Location",
        RRF_RT_REG_SZ,nullptr,path,&bytes)==ERROR_SUCCESS) {
        std::wstring folder(path);
        if(GetFileAttributesW((folder+L"\\Restore Organization.exe").c_str())!=INVALID_FILE_ATTRIBUTES)return folder;
    }
    return ModuleFolder();
}
static std::wstring Quote(const std::wstring& value) {
    std::wstring r=L"\"";size_t slashes=0;
    for(wchar_t c:value) {
        if(c==L'\\'){++slashes;continue;}
        if(c==L'"'){r.append(slashes*2+1,L'\\');r+=c;}
        else{r.append(slashes,L'\\');r+=c;}slashes=0;
    }
    r.append(slashes*2,L'\\');return r+L"\"";
}
class Command final:public IExplorerCommand,public IObjectWithSite {
    long refs=1;IUnknown* site=nullptr;
    std::wstring Folder(IShellItemArray* items) {
        std::wstring folder=FolderFromItems(items);if(!folder.empty()||!site)return folder;
        IServiceProvider* provider=nullptr;IFolderView* view=nullptr;IShellItem* item=nullptr;
        if(SUCCEEDED(site->QueryInterface(IID_IServiceProvider,(void**)&provider))) {
            if(SUCCEEDED(provider->QueryService(SID_SFolderView,IID_IFolderView,(void**)&view))) {
                if(SUCCEEDED(view->GetFolder(IID_IShellItem,(void**)&item))) {
                    PWSTR path=nullptr;if(SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH,&path))){folder=path;CoTaskMemFree(path);}item->Release();
                }
                view->Release();
            }
            provider->Release();
        }
        return folder;
    }
public:
    Command(){InterlockedIncrement(&objectCount);}
    ~Command(){if(site)site->Release();InterlockedDecrement(&objectCount);}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id,void** out) override {
        if(!out)return E_POINTER;*out=nullptr;
        if(IsEqualIID(id,IID_IUnknown)||IsEqualIID(id,IID_IExplorerCommand))*out=static_cast<IExplorerCommand*>(this);
        else if(IsEqualIID(id,IID_IObjectWithSite))*out=static_cast<IObjectWithSite*>(this);
        else return E_NOINTERFACE;AddRef();return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override{return InterlockedIncrement(&refs);}
    ULONG STDMETHODCALLTYPE Release() override{long n=InterlockedDecrement(&refs);if(!n)delete this;return n;}
    HRESULT STDMETHODCALLTYPE GetTitle(IShellItemArray*,PWSTR* title) override{return SHStrDupW(L"Create snapshot",title);}
    HRESULT STDMETHODCALLTYPE GetIcon(IShellItemArray*,PWSTR* icon) override {
        std::wstring p=CreatorFolder()+L"\\Restore Organization.exe,0";return SHStrDupW(p.c_str(),icon);
    }
    HRESULT STDMETHODCALLTYPE GetToolTip(IShellItemArray*,PWSTR* tip) override{return SHStrDupW(L"Save this folder's file and subfolder arrangement",tip);}
    HRESULT STDMETHODCALLTYPE GetCanonicalName(GUID* name) override{if(!name)return E_POINTER;*name=commandId;return S_OK;}
    HRESULT STDMETHODCALLTYPE GetState(IShellItemArray* items,BOOL,EXPCMDSTATE* state) override {
        if(!state)return E_POINTER;
        DWORD count=0;
        if(items&&SUCCEEDED(items->GetCount(&count))&&count>1){*state=ECS_HIDDEN;return S_OK;}
        *state=Folder(items).empty()?ECS_HIDDEN:ECS_ENABLED;return S_OK;
    }
    HRESULT STDMETHODCALLTYPE Invoke(IShellItemArray* items,IBindCtx*) override {
        std::wstring folder=Folder(items);if(folder.empty())return E_INVALIDARG;
        std::wstring exe=CreatorFolder()+L"\\Restore Organization.exe", args=L"--snapshot "+Quote(folder);
        SHELLEXECUTEINFOW info={sizeof(info)};info.fMask=SEE_MASK_FLAG_NO_UI;info.lpFile=exe.c_str();info.lpParameters=args.c_str();info.nShow=SW_SHOWNORMAL;
        return ShellExecuteExW(&info)?S_OK:HRESULT_FROM_WIN32(GetLastError());
    }
    HRESULT STDMETHODCALLTYPE GetFlags(EXPCMDFLAGS* flags) override{if(!flags)return E_POINTER;*flags=ECF_DEFAULT;return S_OK;}
    HRESULT STDMETHODCALLTYPE EnumSubCommands(IEnumExplorerCommand** commands) override{if(!commands)return E_POINTER;*commands=nullptr;return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE SetSite(IUnknown* value) override{if(value)value->AddRef();if(site)site->Release();site=value;return S_OK;}
    HRESULT STDMETHODCALLTYPE GetSite(REFIID id,void** out) override{if(!out)return E_POINTER;*out=nullptr;return site?site->QueryInterface(id,out):E_FAIL;}
};
class Factory:public IClassFactory {
    long refs=1;
public:
    Factory(){InterlockedIncrement(&objectCount);}
    ~Factory(){InterlockedDecrement(&objectCount);}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id,void** out) override {
        if(!out)return E_POINTER;*out=nullptr;if(!IsEqualIID(id,IID_IUnknown)&&!IsEqualIID(id,IID_IClassFactory))return E_NOINTERFACE;
        *out=this;AddRef();return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override{return InterlockedIncrement(&refs);}
    ULONG STDMETHODCALLTYPE Release() override{long n=InterlockedDecrement(&refs);if(!n)delete this;return n;}
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer,REFIID id,void** out) override {
        if(!out)return E_POINTER;*out=nullptr;if(outer)return CLASS_E_NOAGGREGATION;
        Command* c=new(std::nothrow)Command();if(!c)return E_OUTOFMEMORY;HRESULT r=c->QueryInterface(id,out);c->Release();return r;
    }
    HRESULT STDMETHODCALLTYPE LockServer(BOOL lock) override {if(lock)InterlockedIncrement(&objectCount);else InterlockedDecrement(&objectCount);return S_OK;}
};
extern "C" __declspec(dllexport) HRESULT WINAPI DllGetClassObject(REFCLSID id,REFIID iid,void** out) {
    if(!out)return E_POINTER;*out=nullptr;if(!IsEqualCLSID(id,commandId))return CLASS_E_CLASSNOTAVAILABLE;
    Factory* f=new(std::nothrow)Factory();if(!f)return E_OUTOFMEMORY;HRESULT r=f->QueryInterface(iid,out);f->Release();return r;
}
extern "C" __declspec(dllexport) HRESULT WINAPI DllCanUnloadNow(){return objectCount==0?S_OK:S_FALSE;}
BOOL WINAPI DllMain(HINSTANCE h,DWORD reason,LPVOID){if(reason==DLL_PROCESS_ATTACH){moduleHandle=h;DisableThreadLibraryCalls(h);}return TRUE;}


