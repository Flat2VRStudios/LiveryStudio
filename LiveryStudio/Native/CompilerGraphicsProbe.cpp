#include <windows.h>
#include <d3d9.h>
#include <d3d9on12.h>
#include <d3d12.h>
#include <dxgi1_4.h>
#include <wrl/client.h>
#include <cstdio>

int wmain(int argc,wchar_t** argv)
{
	wchar_t system[MAX_PATH]{};GetSystemDirectoryW(system,MAX_PATH);wcscat_s(system,L"\\d3d9.dll");
	const auto module=LoadLibraryW(argc>1?argv[1]:system);if(!module){std::printf("Cannot load graphics runtime: %lu\n",GetLastError());return 2;}
	const auto create=reinterpret_cast<IDirect3D9*(WINAPI*)(UINT)>(GetProcAddress(module,"Direct3DCreate9"));
	if(!create)return 3;IDirect3D9* d3d=nullptr;
	Microsoft::WRL::ComPtr<ID3D12Device> software;
	if(argc>2&&(wcscmp(argv[2],L"on12")==0||wcscmp(argv[2],L"warp")==0)){const auto createOn12=reinterpret_cast<PFN_Direct3DCreate9On12>(GetProcAddress(module,"Direct3DCreate9On12"));if(!createOn12){std::printf("D3D9On12 is unavailable\n");return 5;}D3D9ON12_ARGS override{};override.Enable9On12=TRUE;
		if(wcscmp(argv[2],L"warp")==0){Microsoft::WRL::ComPtr<IDXGIFactory4> factory;Microsoft::WRL::ComPtr<IDXGIAdapter> adapter;auto hr=CreateDXGIFactory1(IID_PPV_ARGS(&factory));if(SUCCEEDED(hr))hr=factory->EnumWarpAdapter(IID_PPV_ARGS(&adapter));if(SUCCEEDED(hr))hr=D3D12CreateDevice(adapter.Get(),D3D_FEATURE_LEVEL_11_0,IID_PPV_ARGS(&software));std::printf("D3D12 software device=%08lX\n",static_cast<unsigned long>(hr));if(FAILED(hr))return 6;override.pD3D12Device=software.Get();}
		d3d=createOn12(D3D_SDK_VERSION,&override,1);}
	else d3d=create(D3D_SDK_VERSION);if(!d3d)return 4;
	WNDCLASSW type{};type.lpfnWndProc=DefWindowProcW;type.hInstance=GetModuleHandleW(nullptr);type.lpszClassName=L"LiveryCompilerGraphicsProbe";RegisterClassW(&type);
	const auto window=CreateWindowW(type.lpszClassName,L"Graphics test",WS_OVERLAPPEDWINDOW,100,100,300,300,nullptr,nullptr,type.hInstance,nullptr);
	std::printf("Graphics host=%p\n",window);
	bool succeeded=false;const auto count=argc>2?d3d->GetAdapterCount():1u;
	for(UINT adapter=0;adapter<count;++adapter)
	{
		if(argc>3){MONITORINFO monitor{};monitor.cbSize=sizeof(monitor);if(GetMonitorInfoW(d3d->GetAdapterMonitor(adapter),&monitor)){SetWindowPos(window,nullptr,monitor.rcMonitor.left+100,monitor.rcMonitor.top+100,300,300,SWP_NOACTIVATE|SWP_NOZORDER);std::printf("Graphics test host at %ld,%ld\n",monitor.rcMonitor.left+100,monitor.rcMonitor.top+100);}}
		D3DADAPTER_IDENTIFIER9 identity{};d3d->GetAdapterIdentifier(adapter,0,&identity);D3DPRESENT_PARAMETERS present{};present.Windowed=TRUE;present.SwapEffect=D3DSWAPEFFECT_DISCARD;
		D3DDISPLAYMODE mode{};const auto modeResult=d3d->GetAdapterDisplayMode(adapter,&mode);std::printf("Display mode HRESULT=%08lX format=%u size=%ux%u\n",static_cast<unsigned long>(modeResult),mode.Format,mode.Width,mode.Height);
		if(argc>2&&wcscmp(argv[2],L"format")==0){present.BackBufferFormat=D3DFMT_X8R8G8B8;present.BackBufferWidth=64;present.BackBufferHeight=64;present.hDeviceWindow=window;present.PresentationInterval=D3DPRESENT_INTERVAL_IMMEDIATE;}
		IDirect3DDevice9* device=nullptr;const auto vertex=argc>4?D3DCREATE_SOFTWARE_VERTEXPROCESSING:D3DCREATE_HARDWARE_VERTEXPROCESSING;const auto kind=argc>2&&wcscmp(argv[2],L"nullref")==0?D3DDEVTYPE_NULLREF:argc>2&&wcscmp(argv[2],L"ref")==0?D3DDEVTYPE_REF:D3DDEVTYPE_HAL;const auto hr=d3d->CreateDevice(adapter,kind,window,D3DCREATE_FPU_PRESERVE|D3DCREATE_MULTITHREADED|D3DCREATE_NOWINDOWCHANGES|vertex,&present,&device);
		std::printf("Adapter %u: %s HRESULT=%08lX\n",adapter,identity.Description,static_cast<unsigned long>(hr));
		if(SUCCEEDED(hr)&&device){IDirect3DTexture9* texture=nullptr;const auto allocated=device->CreateTexture(64,64,1,0,D3DFMT_A8R8G8B8,D3DPOOL_MANAGED,&texture,nullptr);std::printf("Compiler texture allocation=%08lX\n",static_cast<unsigned long>(allocated));succeeded|=SUCCEEDED(allocated);if(texture)texture->Release();device->Release();}
	}
	d3d->Release();DestroyWindow(window);FreeLibrary(module);return succeeded?0:1;
}
