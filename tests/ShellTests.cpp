#include <windows.h>
#include <shobjidl.h>
#include <iostream>
static const CLSID id={0x5812b34a,0x5af8,0x4c61,{0x85,0x4e,0x72,0xe3,0x3a,0x04,0xe1,0xe4}};
int main() {
    CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED);
    HMODULE dll=LoadLibraryW(L"dist\\SnapshotCommand.dll");if(!dll){std::cerr<<"DLL load failed";return 1;}
    using GetClass=HRESULT(WINAPI*)(REFCLSID,REFIID,void**);
    using CanUnload=HRESULT(WINAPI*)();
    GetClass get=(GetClass)GetProcAddress(dll,"DllGetClassObject");CanUnload unload=(CanUnload)GetProcAddress(dll,"DllCanUnloadNow");
    if(!get||!unload||unload()!=S_OK)return 2;
    IClassFactory* factory=nullptr;IExplorerCommand* command=nullptr;
    if(FAILED(get(id,IID_IClassFactory,(void**)&factory)))return 3;
    if(FAILED(factory->CreateInstance(nullptr,IID_IExplorerCommand,(void**)&command)))return 4;
    if(unload()!=S_FALSE)return 5;
    PWSTR title=nullptr;if(FAILED(command->GetTitle(nullptr,&title))||wcscmp(title,L"Create snapshot"))return 6;CoTaskMemFree(title);
    GUID canonical;if(FAILED(command->GetCanonicalName(&canonical))||!IsEqualGUID(canonical,id))return 7;
    EXPCMDSTATE state;if(FAILED(command->GetState(nullptr,TRUE,&state))||state!=ECS_HIDDEN)return 8;
    wchar_t current[32768];GetCurrentDirectoryW(32768,current);
    IShellItem* item=nullptr;IShellItemArray* items=nullptr;
    if(FAILED(SHCreateItemFromParsingName(current,nullptr,IID_IShellItem,(void**)&item)))return 9;
    if(FAILED(SHCreateShellItemArrayFromShellItem(item,IID_IShellItemArray,(void**)&items)))return 10;
    if(FAILED(command->GetState(items,TRUE,&state))||state!=ECS_ENABLED)return 11;
    IObjectWithSite* withSite=nullptr;if(FAILED(command->QueryInterface(IID_IObjectWithSite,(void**)&withSite)))return 12;withSite->Release();
    items->Release();item->Release();command->Release();factory->Release();
    if(unload()!=S_OK)return 13;
    FreeLibrary(dll);CoUninitialize();std::cout<<"PASS: shell COM activation, title, identity, folder state, site interface, and lifetime"<<std::endl;return 0;
}
