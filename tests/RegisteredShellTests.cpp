#include <windows.h>
#include <shobjidl.h>
#include <iostream>
#include <string>
static const CLSID id={0x5812b34a,0x5af8,0x4c61,{0x85,0x4e,0x72,0xe3,0x3a,0x04,0xe1,0xe4}};
int main() {
    HRESULT hr=CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED);if(FAILED(hr))return 1;
    IExplorerCommand* command=nullptr;
    hr=CoCreateInstance(id,nullptr,CLSCTX_LOCAL_SERVER,IID_IExplorerCommand,(void**)&command);
    if(FAILED(hr)){std::cerr<<"Registered COM activation failed: "<<std::hex<<hr<<std::endl;return 2;}
    PWSTR title=nullptr;hr=command->GetTitle(nullptr,&title);
    if(FAILED(hr)||!title||wcscmp(title,L"Create snapshot"))return 3;CoTaskMemFree(title);
    wchar_t location[32768];DWORD bytes=sizeof(location);
    if(RegGetValueW(HKEY_CURRENT_USER,L"Software\\RestoreOrganization\\ContextMenu",L"Location",RRF_RT_REG_SZ,nullptr,location,&bytes)!=ERROR_SUCCESS)return 4;
    std::wstring expected=std::wstring(location)+L"\\Restore Organization.exe,0";
    PWSTR icon=nullptr;hr=command->GetIcon(nullptr,&icon);
    if(FAILED(hr)||!icon||expected!=icon){std::cerr<<"Registered menu did not resolve per-user portable application"<<std::endl;return 5;}CoTaskMemFree(icon);
    wchar_t current[32768];GetCurrentDirectoryW(32768,current);
    IShellItem* item=nullptr;IShellItemArray* items=nullptr;
    if(FAILED(SHCreateItemFromParsingName(current,nullptr,IID_IShellItem,(void**)&item)))return 6;
    if(FAILED(SHCreateShellItemArrayFromShellItem(item,IID_IShellItemArray,(void**)&items)))return 7;
    EXPCMDSTATE state;if(FAILED(command->GetState(items,TRUE,&state))||state!=ECS_ENABLED)return 8;
    items->Release();item->Release();command->Release();CoUninitialize();
    std::cout<<"PASS: Windows activated the installed packaged COM server, menu title, per-user launcher path, and selected-folder state"<<std::endl;return 0;
}
