#include <windows.h>
#include <d3d11.h>
#include <d2d1_3.h>
#include <d2d1svg.h>
#include <shlwapi.h>
#include <wrl/client.h>
#include <cstring>
#include <algorithm>
using Microsoft::WRL::ComPtr;

// Render validated, self-contained vector artwork independently of the live viewport.
// WARP makes the import deterministic and keeps it usable without a discrete GPU.
extern "C" __declspec(dllexport) HRESULT ls_svg(const unsigned char* xml, unsigned length,
    unsigned width, unsigned height, unsigned char* rgba, unsigned capacity) noexcept
{
    if (!xml || !length || length > 2 * 1024 * 1024 || !rgba || !width || !height ||
        width > 4096 || height > 4096 || capacity != width * height * 4) return E_INVALIDARG;
    const HRESULT apartment = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    struct Apartment { bool owned; ~Apartment() { if (owned) CoUninitialize(); } } cleanup{SUCCEEDED(apartment)};
    if (FAILED(apartment) && apartment != RPC_E_CHANGED_MODE) return apartment;
    HRESULT hr;
#define SVG_CHECK(call) if (FAILED(hr = (call))) return hr
    ComPtr<ID3D11Device> d3d;
    SVG_CHECK(D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_WARP, nullptr,
        D3D11_CREATE_DEVICE_BGRA_SUPPORT, nullptr, 0, D3D11_SDK_VERSION, &d3d, nullptr, nullptr));
    ComPtr<IDXGIDevice> dxgi; SVG_CHECK(d3d.As(&dxgi));
    ComPtr<ID2D1Factory1> factory;
    SVG_CHECK(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, factory.GetAddressOf()));
    ComPtr<ID2D1Device> device; SVG_CHECK(factory->CreateDevice(dxgi.Get(), &device));
    ComPtr<ID2D1DeviceContext> base; SVG_CHECK(device->CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS_NONE, &base));
    ComPtr<ID2D1DeviceContext5> context; SVG_CHECK(base.As(&context));
    ComPtr<IStream> stream; stream.Attach(SHCreateMemStream(xml, length)); if (!stream) return E_OUTOFMEMORY;
    ComPtr<ID2D1SvgDocument> document;
    SVG_CHECK(context->CreateSvgDocument(stream.Get(), D2D1::SizeF(float(width), float(height)), &document));
    const auto format = D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_PREMULTIPLIED);
    ComPtr<ID2D1Bitmap1> target;
    SVG_CHECK(context->CreateBitmap(D2D1::SizeU(width, height), nullptr, 0,
        D2D1::BitmapProperties1(D2D1_BITMAP_OPTIONS_TARGET, format), &target));
    context->SetTarget(target.Get()); context->BeginDraw(); context->Clear(D2D1::ColorF(0, 0));
    context->DrawSvgDocument(document.Get()); SVG_CHECK(context->EndDraw()); context->SetTarget(nullptr);
    ComPtr<ID2D1Bitmap1> readable;
    SVG_CHECK(context->CreateBitmap(D2D1::SizeU(width, height), nullptr, 0,
        D2D1::BitmapProperties1(D2D1_BITMAP_OPTIONS_CPU_READ | D2D1_BITMAP_OPTIONS_CANNOT_DRAW, format), &readable));
    SVG_CHECK(readable->CopyFromBitmap(nullptr, target.Get(), nullptr));
    D2D1_MAPPED_RECT map{}; SVG_CHECK(readable->Map(D2D1_MAP_OPTIONS_READ, &map));
    for (unsigned y = 0; y < height; ++y) for (unsigned x = 0; x < width; ++x) {
        const auto* p = map.bits + y * map.pitch + x * 4; auto* o = rgba + (y * width + x) * 4;
        const unsigned alpha = p[3]; o[3] = static_cast<unsigned char>(alpha);
        for (unsigned c = 0; c < 3; ++c) o[c] = alpha ? static_cast<unsigned char>(
            std::min(255u, (unsigned(p[2 - c]) * 255 + alpha / 2) / alpha)) : 0;
    }
    return readable->Unmap();
#undef SVG_CHECK
}
