#include <windows.h>
#include <d3d11.h>
#include <dxgi1_3.h>
#include <wrl/client.h>
#include <vector>
#include <string>
#include <stdexcept>
#include <memory>
#include <algorithm>
#include <cstring>
#include <fstream>
#include <array>
#include <cmath>
#include "VertexShader.h"
#include "PixelShader.h"
#include "StampVertexShader.h"
#include "StampGeometryShader.h"
#include "StampPixelShader.h"
#include "AtlasVertexShader.h"
#include "InitializeSurfaceShader.h"
using Microsoft::WRL::ComPtr;
// WinUI 3's native SwapChainPanel interface (Microsoft.UI.Xaml, not the UWP IID).
MIDL_INTERFACE("63aad0b8-7c24-40ff-85a8-640d944cc325") PanelNative:public IUnknown {virtual HRESULT STDMETHODCALLTYPE SetSwapChain(IDXGISwapChain*)=0;};
static void check(HRESULT hr){if(FAILED(hr))throw std::runtime_error("Direct3D error "+std::to_string(unsigned(hr)));}
struct StampAtlas {
 ComPtr<ID3D11Texture2D> data,previousData;ComPtr<ID3D11ShaderResourceView> view,previous;
 ComPtr<ID3D11RenderTargetView> target;ComPtr<ID3D11DepthStencilView> depth;unsigned width=0,height=0;
 void ensure(ID3D11Device* device,ID3D11Texture2D* source){
  D3D11_TEXTURE2D_DESC desc;source->GetDesc(&desc);if(data&&width==desc.Width&&height==desc.Height)return;
  StampAtlas replacement;replacement.width=desc.Width;replacement.height=desc.Height;
  check(device->CreateTexture2D(&desc,nullptr,&replacement.data));check(device->CreateShaderResourceView(replacement.data.Get(),nullptr,&replacement.view));check(device->CreateRenderTargetView(replacement.data.Get(),nullptr,&replacement.target));
  desc.MipLevels=1;desc.Format=DXGI_FORMAT_D16_UNORM;desc.BindFlags=D3D11_BIND_DEPTH_STENCIL;desc.MiscFlags=0;
  ComPtr<ID3D11Texture2D> z;check(device->CreateTexture2D(&desc,nullptr,&z));check(device->CreateDepthStencilView(z.Get(),nullptr,&replacement.depth));*this=std::move(replacement);
 }
 void snapshot(ID3D11Device* device,ID3D11DeviceContext* context){
  if(!previousData){D3D11_TEXTURE2D_DESC desc;data->GetDesc(&desc);ComPtr<ID3D11Texture2D> copy;ComPtr<ID3D11ShaderResourceView> srv;check(device->CreateTexture2D(&desc,nullptr,&copy));check(device->CreateShaderResourceView(copy.Get(),nullptr,&srv));previousData=copy;previous=srv;}
  context->CopyResource(previousData.Get(),data.Get());
 }
};
struct Renderer {
 ComPtr<ID3D11Device> device;ComPtr<ID3D11DeviceContext> context;ComPtr<IDXGISwapChain1> swap;
 ComPtr<PanelNative> panel;ComPtr<ID3D11RenderTargetView> target;ComPtr<ID3D11DepthStencilView> depth;
 ComPtr<ID3D11VertexShader> vs;ComPtr<ID3D11PixelShader> ps;ComPtr<ID3D11InputLayout> layout;
 ComPtr<ID3D11VertexShader> stampVS,atlasVS;ComPtr<ID3D11GeometryShader> stampGS;ComPtr<ID3D11PixelShader> stampPS,initializeSurfacePS;StampAtlas colorAtlas,surfaceAtlas;
 ComPtr<ID3D11Buffer> vertices,constants,floor;ComPtr<ID3D11SamplerState> sampler;ComPtr<ID3D11RasterizerState> raster;
 ComPtr<ID3D11ShaderResourceView> texture,sticker,structure,basePaintMask;ComPtr<ID3D11Texture2D> textureData,structureData;unsigned count=0,width=0,height=0;float lastScene[76]={};
 std::array<ComPtr<ID3D11ShaderResourceView>,5> extras;ComPtr<ID3D11ShaderResourceView> environment,irradiance,selection;bool offscreen=false;
 float displayScaleX=1,displayScaleY=1;unsigned anisotropy=1;
 void quality(float scaleX,float scaleY,unsigned filtering){
  if(!std::isfinite(scaleX)||!std::isfinite(scaleY)||scaleX<=0||scaleY<=0||scaleX>65536||scaleY>65536||filtering<1||filtering>16)throw std::runtime_error("Invalid preview quality");
  ComPtr<ID3D11SamplerState> replacement;
  if(filtering!=anisotropy){D3D11_SAMPLER_DESC sd={};sd.Filter=filtering>1?D3D11_FILTER_ANISOTROPIC:D3D11_FILTER_MIN_MAG_MIP_LINEAR;sd.MaxAnisotropy=filtering;sd.AddressU=sd.AddressV=sd.AddressW=D3D11_TEXTURE_ADDRESS_CLAMP;sd.MaxLOD=D3D11_FLOAT32_MAX;check(device->CreateSamplerState(&sd,&replacement));}
  if(panel&&(scaleX!=displayScaleX||scaleY!=displayScaleY)){ComPtr<IDXGISwapChain2> composition;check(swap.As(&composition));DXGI_MATRIX_3X2_F matrix={scaleX,0,0,scaleY,0,0};check(composition->SetMatrixTransform(&matrix));}
  if(replacement)sampler=replacement;anisotropy=filtering;displayScaleX=scaleX;displayScaleY=scaleY;
 }
 void resize(unsigned w,unsigned h){if(!w||!h|| (w==width&&h==height))return;context->OMSetRenderTargets(0,nullptr,nullptr);target.Reset();depth.Reset();check(swap->ResizeBuffers(2,w,h,DXGI_FORMAT_B8G8R8A8_UNORM,0));width=w;height=h;ComPtr<ID3D11Texture2D> buffer;check(swap->GetBuffer(0,IID_PPV_ARGS(&buffer)));check(device->CreateRenderTargetView(buffer.Get(),nullptr,&target));D3D11_TEXTURE2D_DESC d={};d.Width=w;d.Height=h;d.MipLevels=1;d.ArraySize=1;d.Format=DXGI_FORMAT_D24_UNORM_S8_UINT;d.SampleDesc.Count=1;d.BindFlags=D3D11_BIND_DEPTH_STENCIL;check(device->CreateTexture2D(&d,nullptr,&buffer));check(device->CreateDepthStencilView(buffer.Get(),nullptr,&depth));}
 void image(const unsigned char* pixels,unsigned w,unsigned h,int slot){
  if(!w||!h||w>8192||h>8192)throw std::runtime_error("Invalid texture size");
  if(slot==8){
   // Retain only immutable original alpha, independent of displayed pigment.
   std::vector<unsigned char> mask(size_t(w)*h);for(size_t i=0;i<mask.size();i++)mask[i]=pixels[i*4+3];
   D3D11_TEXTURE2D_DESC desc={};desc.Width=w;desc.Height=h;desc.MipLevels=desc.ArraySize=1;desc.Format=DXGI_FORMAT_R8_UNORM;desc.SampleDesc.Count=1;desc.Usage=D3D11_USAGE_IMMUTABLE;desc.BindFlags=D3D11_BIND_SHADER_RESOURCE;D3D11_SUBRESOURCE_DATA input={mask.data(),w,0};ComPtr<ID3D11Texture2D> data;ComPtr<ID3D11ShaderResourceView> srv;check(device->CreateTexture2D(&desc,&input,&data));check(device->CreateShaderResourceView(data.Get(),nullptr,&srv));basePaintMask=srv;return;
  }
  auto existing=slot==0?textureData.Get():slot==2?structureData.Get():nullptr;
  if(existing){D3D11_TEXTURE2D_DESC desc;existing->GetDesc(&desc);if(w==desc.Width&&h==desc.Height){context->UpdateSubresource(existing,0,nullptr,pixels,w*4,0);context->GenerateMips(slot==0?texture.Get():structure.Get());return;}}
  D3D11_TEXTURE2D_DESC d={};d.Width=w;d.Height=h;d.MipLevels=0;d.ArraySize=1;d.Format=DXGI_FORMAT_R8G8B8A8_UNORM;d.SampleDesc.Count=1;d.BindFlags=D3D11_BIND_SHADER_RESOURCE|D3D11_BIND_RENDER_TARGET;d.MiscFlags=D3D11_RESOURCE_MISC_GENERATE_MIPS;
  ComPtr<ID3D11Texture2D> data;check(device->CreateTexture2D(&d,nullptr,&data));context->UpdateSubresource(data.Get(),0,nullptr,pixels,w*4,0);
  ComPtr<ID3D11ShaderResourceView> srv;check(device->CreateShaderResourceView(data.Get(),nullptr,&srv));context->GenerateMips(srv.Get());
  if(slot>=3&&slot<=7)extras[slot-3]=srv;else if(slot==1)sticker=srv;else if(slot==2){structure=srv;structureData=data;surfaceAtlas={};}else{texture=srv;textureData=data;colorAtlas={};}
 }
 void init(IUnknown* object){
  if(object)check(object->QueryInterface(IID_PPV_ARGS(&panel)));else offscreen=true;D3D_FEATURE_LEVEL level;
  HRESULT hr=D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,nullptr,0,D3D11_SDK_VERSION,&device,&level,&context);
  if(FAILED(hr))check(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,nullptr,0,D3D11_SDK_VERSION,&device,&level,&context));
  ComPtr<IDXGIDevice> dxgi;check(device.As(&dxgi));ComPtr<IDXGIAdapter> adapter;check(dxgi->GetAdapter(&adapter));ComPtr<IDXGIFactory2> factory;check(adapter->GetParent(IID_PPV_ARGS(&factory)));
  DXGI_SWAP_CHAIN_DESC1 desc={};desc.Width=1;desc.Height=1;desc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;desc.SampleDesc.Count=1;desc.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;desc.BufferCount=2;desc.Scaling=DXGI_SCALING_STRETCH;desc.SwapEffect=DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL;desc.AlphaMode=DXGI_ALPHA_MODE_IGNORE;
  check(factory->CreateSwapChainForComposition(device.Get(),&desc,nullptr,&swap));if(panel)check(panel->SetSwapChain(swap.Get()));
  check(device->CreateVertexShader(VertexShaderCode,sizeof(VertexShaderCode),nullptr,&vs));check(device->CreatePixelShader(PixelShaderCode,sizeof(PixelShaderCode),nullptr,&ps));
  check(device->CreateVertexShader(StampVertexShaderCode,sizeof(StampVertexShaderCode),nullptr,&stampVS));check(device->CreatePixelShader(StampPixelShaderCode,sizeof(StampPixelShaderCode),nullptr,&stampPS));
  check(device->CreateGeometryShader(StampGeometryShaderCode,sizeof(StampGeometryShaderCode),nullptr,&stampGS));
  check(device->CreateVertexShader(AtlasVertexShaderCode,sizeof(AtlasVertexShaderCode),nullptr,&atlasVS));check(device->CreatePixelShader(InitializeSurfaceShaderCode,sizeof(InitializeSurfaceShaderCode),nullptr,&initializeSurfacePS));
  D3D11_INPUT_ELEMENT_DESC elements[]={{"POSITION",0,DXGI_FORMAT_R32G32B32_FLOAT,0,0,D3D11_INPUT_PER_VERTEX_DATA,0},{"NORMAL",0,DXGI_FORMAT_R32G32B32_FLOAT,0,12,D3D11_INPUT_PER_VERTEX_DATA,0},{"TEXCOORD",0,DXGI_FORMAT_R32G32_FLOAT,0,24,D3D11_INPUT_PER_VERTEX_DATA,0},{"TEXCOORD",1,DXGI_FORMAT_R32_FLOAT,0,32,D3D11_INPUT_PER_VERTEX_DATA,0}};
  check(device->CreateInputLayout(elements,4,VertexShaderCode,sizeof(VertexShaderCode),&layout));D3D11_BUFFER_DESC bd={};bd.ByteWidth=sizeof(lastScene);bd.Usage=D3D11_USAGE_DEFAULT;bd.BindFlags=D3D11_BIND_CONSTANT_BUFFER;check(device->CreateBuffer(&bd,nullptr,&constants));
  D3D11_SAMPLER_DESC sd={};sd.Filter=D3D11_FILTER_MIN_MAG_MIP_LINEAR;sd.AddressU=sd.AddressV=sd.AddressW=D3D11_TEXTURE_ADDRESS_CLAMP;sd.MaxLOD=D3D11_FLOAT32_MAX;check(device->CreateSamplerState(&sd,&sampler));
  D3D11_RASTERIZER_DESC rd={};rd.FillMode=D3D11_FILL_SOLID;rd.CullMode=D3D11_CULL_NONE;rd.DepthClipEnable=TRUE;check(device->CreateRasterizerState(&rd,&raster));unsigned char pixel[]={110,118,130,0};image(pixel,1,1,0);image(pixel,1,1,1);unsigned char surface[]={128,0,0,255};image(surface,1,1,2);unsigned char normal[]={128,128,255,255},dirt[]={100,75,50,0},damage[]={80,80,80,0},dustSurface[]={25,0,0,255};image(normal,1,1,3);image(dirt,1,1,4);image(dustSurface,1,1,5);image(damage,1,1,6);image(normal,1,1,7);
 }
 void mesh(const float* values,unsigned n){if(!n||n%3||n>6000000)throw std::runtime_error("Invalid mesh count");D3D11_BUFFER_DESC bd={};bd.ByteWidth=n*36;bd.Usage=D3D11_USAGE_IMMUTABLE;bd.BindFlags=D3D11_BIND_VERTEX_BUFFER;D3D11_SUBRESOURCE_DATA data={values,0,0};vertices.Reset();check(device->CreateBuffer(&bd,&data,&vertices));count=n;float y=1e9;for(unsigned i=0;i<n;i++)y=std::min(y,values[i*9+1]);y-=.025f;
  float ground[]={-10,y,-10,0,1,0,0,0,6,10,y,-10,0,1,0,0,0,6,10,y,10,0,1,0,0,0,6,-10,y,-10,0,1,0,0,0,6,10,y,10,0,1,0,0,0,6,-10,y,10,0,1,0,0,0,6};bd.ByteWidth=sizeof(ground);data.pSysMem=ground;floor.Reset();check(device->CreateBuffer(&bd,&data,&floor));}
 void stamp(StampAtlas& atlas,const float* scene){
  context->UpdateSubresource(constants.Get(),0,nullptr,scene,0,0);context->ClearDepthStencilView(atlas.depth.Get(),D3D11_CLEAR_DEPTH,1,0);context->OMSetRenderTargets(1,atlas.target.GetAddressOf(),atlas.depth.Get());
  D3D11_VIEWPORT viewport={0,0,float(atlas.width),float(atlas.height),0,1};context->RSSetViewports(1,&viewport);context->IASetInputLayout(layout.Get());context->VSSetShader(stampVS.Get(),nullptr,0);context->GSSetConstantBuffers(0,1,constants.GetAddressOf());context->GSSetShaderResources(0,1,texture.GetAddressOf());context->GSSetShaderResources(3,1,structure.GetAddressOf());context->GSSetShaderResources(11,1,selection.GetAddressOf());context->GSSetShader(stampGS.Get(),nullptr,0);context->PSSetShader(stampPS.Get(),nullptr,0);unsigned stride=36,offset=0;context->IASetVertexBuffers(0,1,vertices.GetAddressOf(),&stride,&offset);context->Draw(count,0);context->OMSetRenderTargets(0,nullptr,nullptr);
 }
 void render(const float* scene,unsigned w,unsigned h,bool present=true){
  if(scene!=lastScene)std::memcpy(lastScene,scene,sizeof(lastScene));
  // A failed allocation can interrupt a stamp before its normal cleanup.
  // Reestablish the complete draw state before retrying or rendering mode zero.
  context->OMSetRenderTargets(0,nullptr,nullptr);context->GSSetShader(nullptr,nullptr,0);ID3D11ShaderResourceView* noResource=nullptr;context->GSSetShaderResources(0,1,&noResource);context->GSSetShaderResources(3,1,&noResource);context->GSSetShaderResources(11,1,&noResource);
  resize(w,h);if(!target)return;
  float primary[76];std::memcpy(primary,scene,sizeof(primary));primary[19]=0;if(basePaintMask)primary[27]+=2;
  context->UpdateSubresource(constants.Get(),0,nullptr,primary,0,0);context->VSSetConstantBuffers(0,1,constants.GetAddressOf());context->PSSetConstantBuffers(0,1,constants.GetAddressOf());context->IASetInputLayout(layout.Get());context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);context->RSSetState(raster.Get());context->PSSetSamplers(0,1,sampler.GetAddressOf());context->OMSetDepthStencilState(nullptr,0);
  ID3D11ShaderResourceView* srvs[]={texture.Get(),sticker.Get(),nullptr,structure.Get(),extras[0].Get(),extras[1].Get(),extras[2].Get(),extras[3].Get(),extras[4].Get(),environment.Get(),irradiance.Get(),selection.Get(),nullptr,nullptr,nullptr,basePaintMask?basePaintMask.Get():texture.Get()};context->PSSetShaderResources(0,16,srvs);unsigned stride=36,offset=0;
  if(scene[43]>0 && count){
   // Compose complete color and finish atlases before their mip chains. Keep
   // separate dimensions/depth for native maps with different resolutions.
   colorAtlas.ensure(device.Get(),textureData.Get());context->CopyResource(colorAtlas.data.Get(),textureData.Get());stamp(colorAtlas,primary);
   float mirrored[76];std::memcpy(mirrored,primary,sizeof(mirrored));mirrored[19]=1;mirrored[28]=-mirrored[28];mirrored[40]=-mirrored[40];mirrored[32]=-mirrored[32];for(unsigned j=32;j<35;j++)mirrored[j]=-mirrored[j];mirrored[36]=-mirrored[36];
   if(scene[71]>.5f){
    colorAtlas.snapshot(device.Get(),context.Get());context->PSSetShaderResources(12,1,colorAtlas.previous.GetAddressOf());stamp(colorAtlas,mirrored);context->PSSetShaderResources(12,1,&noResource);
   }
   if(scene[43]>3.5f){
    surfaceAtlas.ensure(device.Get(),structureData.Get());
    if(scene[27]>.5f)context->CopyResource(surfaceAtlas.data.Get(),structureData.Get());
    else{
     context->UpdateSubresource(constants.Get(),0,nullptr,primary,0,0);context->GSSetShader(nullptr,nullptr,0);context->IASetInputLayout(nullptr);context->OMSetRenderTargets(1,surfaceAtlas.target.GetAddressOf(),nullptr);D3D11_VIEWPORT viewport={0,0,float(surfaceAtlas.width),float(surfaceAtlas.height),0,1};context->RSSetViewports(1,&viewport);context->VSSetShader(atlasVS.Get(),nullptr,0);context->PSSetShader(initializeSurfacePS.Get(),nullptr,0);context->Draw(3,0);context->OMSetRenderTargets(0,nullptr,nullptr);
    }
    surfaceAtlas.snapshot(device.Get(),context.Get());context->PSSetShaderResources(14,1,surfaceAtlas.previous.GetAddressOf());primary[19]=2;stamp(surfaceAtlas,primary);context->PSSetShaderResources(14,1,&noResource);
    if(scene[71]>.5f){surfaceAtlas.snapshot(device.Get(),context.Get());context->PSSetShaderResources(14,1,surfaceAtlas.previous.GetAddressOf());mirrored[19]=3;stamp(surfaceAtlas,mirrored);context->PSSetShaderResources(14,1,&noResource);}
    context->GenerateMips(surfaceAtlas.view.Get());primary[19]=0;
   }
   context->GSSetShader(nullptr,nullptr,0);context->GSSetShaderResources(0,1,&noResource);context->GSSetShaderResources(3,1,&noResource);context->GSSetShaderResources(11,1,&noResource);context->GenerateMips(colorAtlas.view.Get());context->UpdateSubresource(constants.Get(),0,nullptr,primary,0,0);
  }
  float clear[]={.047f,.072f,.105f,1};context->ClearRenderTargetView(target.Get(),clear);context->ClearDepthStencilView(depth.Get(),D3D11_CLEAR_DEPTH,1,0);context->OMSetRenderTargets(1,target.GetAddressOf(),depth.Get());
  D3D11_VIEWPORT viewport={0,0,float(width),float(height),0,1};context->RSSetViewports(1,&viewport);context->IASetInputLayout(layout.Get());context->VSSetShader(vs.Get(),nullptr,0);context->PSSetShader(ps.Get(),nullptr,0);srvs[2]=colorAtlas.view.Get();srvs[13]=surfaceAtlas.view.Get();context->PSSetShaderResources(0,16,srvs);
  if(count){context->IASetVertexBuffers(0,1,floor.GetAddressOf(),&stride,&offset);context->Draw(6,0);context->IASetVertexBuffers(0,1,vertices.GetAddressOf(),&stride,&offset);context->Draw(count,0);}if(present&&!offscreen)check(swap->Present(0,0));
 }

 ComPtr<ID3D11ShaderResourceView> cube(const wchar_t* path){
  std::ifstream file(path,std::ios::binary|std::ios::ate);if(!file)throw std::runtime_error("Reflection probe file is missing");auto size=file.tellg();if(size<128||size>256*1024*1024)throw std::runtime_error("Invalid reflection probe size");std::vector<unsigned char> bytes(static_cast<size_t>(size));file.seekg(0);file.read(reinterpret_cast<char*>(bytes.data()),size);
  auto word=[&](size_t offset){if(offset+4>bytes.size())throw std::runtime_error("Truncated reflection probe");unsigned value;std::memcpy(&value,bytes.data()+offset,4);return value;};
  if(word(0)!=0x20534444||word(4)!=124)throw std::runtime_error("Invalid DDS reflection probe");unsigned h=word(12),w=word(16),mips=std::max(1u,word(28)),formatCode=word(84),offset=128,bpp=0,block=0;DXGI_FORMAT format=DXGI_FORMAT_UNKNOWN;
  bool rgbm=false;
  if(formatCode==0&&word(88)==32&&word(92)==255&&word(96)==65280&&word(100)==16711680&&word(104)==4278190080u){format=DXGI_FORMAT_R32G32B32A32_FLOAT;bpp=4;rgbm=true;}else if(formatCode==113){format=DXGI_FORMAT_R16G16B16A16_FLOAT;bpp=8;}else if(formatCode==116){format=DXGI_FORMAT_R32G32B32A32_FLOAT;bpp=16;}else if(formatCode==0x30315844){format=static_cast<DXGI_FORMAT>(word(128));if(word(132)!=3||(word(136)&4)==0||word(140)!=1)throw std::runtime_error("Only a single 2D cube is supported");offset=148;if(format==DXGI_FORMAT_R16G16B16A16_FLOAT)bpp=8;else if(format==DXGI_FORMAT_R32G32B32A32_FLOAT)bpp=16;else if(format==DXGI_FORMAT_BC6H_UF16||format==DXGI_FORMAT_BC6H_SF16)block=16;}else throw std::runtime_error("Unsupported DDS reflection probe format");
  if(!w||w!=h||w>4096||mips>13||(!bpp&&!block)||(word(112)&0x200)==0)throw std::runtime_error("Invalid HDR cube layout");
  std::vector<D3D11_SUBRESOURCE_DATA> data;std::vector<float> linear;if(rgbm)linear.reserve(bytes.size());for(unsigned face=0;face<6;face++){unsigned width=w,height=h;for(unsigned mip=0;mip<mips;mip++){unsigned row=block?std::max(1u,(width+3)/4)*block:width*bpp,rows=block?std::max(1u,(height+3)/4):height;size_t length=static_cast<size_t>(row)*rows;if(static_cast<size_t>(offset)+length>bytes.size())throw std::runtime_error("Truncated HDR cube pixels");if(rgbm){auto start=linear.size();for(size_t pixel=offset;pixel<offset+length;pixel+=4){float multiplier=bytes[pixel+3]/255.f*16;for(unsigned component=0;component<3;component++){float value=bytes[pixel+component]/255.f;linear.push_back((value<=.04045f?value/12.92f:std::pow((value+.055f)/1.055f,2.4f))*multiplier);}linear.push_back(1);}data.push_back({linear.data()+start,width*16,width*height*16});}else data.push_back({bytes.data()+offset,row,static_cast<unsigned>(length)});offset+=static_cast<unsigned>(length);width=std::max(1u,width/2);height=std::max(1u,height/2);}}
  D3D11_TEXTURE2D_DESC d={};d.Width=w;d.Height=h;d.MipLevels=mips;d.ArraySize=6;d.Format=format;d.SampleDesc.Count=1;d.Usage=D3D11_USAGE_IMMUTABLE;d.BindFlags=D3D11_BIND_SHADER_RESOURCE;d.MiscFlags=D3D11_RESOURCE_MISC_TEXTURECUBE;ComPtr<ID3D11Texture2D> texture;check(device->CreateTexture2D(&d,data.data(),&texture));ComPtr<ID3D11ShaderResourceView> result;check(device->CreateShaderResourceView(texture.Get(),nullptr,&result));return result;
 }
 void select(const unsigned* mask,unsigned triangles){
  selection.Reset();if(!triangles)return;if(triangles!=count/3)throw std::runtime_error("Panel mask does not match this car");D3D11_BUFFER_DESC d={};d.ByteWidth=triangles*4;d.Usage=D3D11_USAGE_IMMUTABLE;d.BindFlags=D3D11_BIND_SHADER_RESOURCE;d.MiscFlags=D3D11_RESOURCE_MISC_BUFFER_STRUCTURED;d.StructureByteStride=4;D3D11_SUBRESOURCE_DATA values={mask,0,0};ComPtr<ID3D11Buffer> buffer;check(device->CreateBuffer(&d,&values,&buffer));D3D11_SHADER_RESOURCE_VIEW_DESC view={};view.Format=DXGI_FORMAT_UNKNOWN;view.ViewDimension=D3D11_SRV_DIMENSION_BUFFER;view.Buffer.NumElements=triangles;check(device->CreateShaderResourceView(buffer.Get(),&view,&selection));
 }
 void snapshot(unsigned char* pixels,unsigned capacity){
  render(lastScene,width,height,false);
  ComPtr<ID3D11Texture2D> source,copy;check(swap->GetBuffer(0,IID_PPV_ARGS(&source)));D3D11_TEXTURE2D_DESC desc={};source->GetDesc(&desc);
  if(capacity<static_cast<unsigned long long>(desc.Width)*desc.Height*4)throw std::runtime_error("Preview image buffer is too small");
  desc.Usage=D3D11_USAGE_STAGING;desc.BindFlags=0;desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;desc.MiscFlags=0;check(device->CreateTexture2D(&desc,nullptr,&copy));context->CopyResource(copy.Get(),source.Get());
  D3D11_MAPPED_SUBRESOURCE mapped={};check(context->Map(copy.Get(),0,D3D11_MAP_READ,0,&mapped));
  for(unsigned y=0;y<desc.Height;y++){const auto* row=static_cast<const unsigned char*>(mapped.pData)+y*mapped.RowPitch;auto* out=pixels+y*desc.Width*4;for(unsigned x=0;x<desc.Width;x++){out[x*4]=row[x*4+2];out[x*4+1]=row[x*4+1];out[x*4+2]=row[x*4];out[x*4+3]=255;}}
  context->Unmap(copy.Get(),0);
 }

 ~Renderer(){if(panel)panel->SetSwapChain(nullptr);}
};
static thread_local std::string lastError;
#define API extern "C" __declspec(dllexport)
API const char* ls_error(){return lastError.c_str();}
API void* ls_create(IUnknown* panel){try{auto r=std::make_unique<Renderer>();r->init(panel);return r.release();}catch(const std::exception&e){lastError=e.what();return nullptr;}}
API void ls_destroy(Renderer*r){delete r;}
API int ls_mesh(Renderer*r,const float*p,unsigned n){try{r->mesh(p,n);return 0;}catch(const std::exception&e){lastError=e.what();return -1;}}
API int ls_texture(Renderer*r,const unsigned char*p,unsigned w,unsigned h,int slot){try{if(slot<0||slot>8)throw std::runtime_error("Invalid texture slot");r->image(p,w,h,slot);return 0;}catch(const std::exception&e){lastError=e.what();return -1;}}
// Preserve the original 72-float API for existing callers; extended buffers have
// an explicit length so a mismatched app cannot make the renderer read past them.
API int ls_render(Renderer*r,const float*p,unsigned w,unsigned h){try{float scene[76]={};std::memcpy(scene,p,72*sizeof(float));scene[72]=.25f;scene[73]=.35f;r->render(scene,w,h);return 0;}catch(const std::exception&e){lastError=e.what();return -1;}}
API int ls_render_ex(Renderer*r,const float*p,unsigned floats,unsigned w,unsigned h){try{if(!p||floats!=76)throw std::runtime_error("Incompatible preview scene buffer");for(unsigned i=0;i<floats;i++)if(!std::isfinite(p[i]))throw std::runtime_error("Invalid preview scene value");r->render(p,w,h);return 0;}catch(const std::exception&e){lastError=e.what();return -1;}}
API int ls_snapshot(Renderer*r,unsigned char*p,unsigned capacity){try{r->snapshot(p,capacity);return 0;}catch(const std::exception&e){lastError=e.what();return -1;}}

API void* ls_create_offscreen(){try{auto r=std::make_unique<Renderer>();r->init(nullptr);return r.release();}catch(const std::exception&e){lastError=e.what();return nullptr;}}
API int ls_environment(Renderer*r,const wchar_t* specular,const wchar_t* diffuse){try{if(!specular||!*specular){r->environment.Reset();r->irradiance.Reset();return 0;}auto spec=r->cube(specular);auto irr=r->cube(diffuse);r->environment=spec;r->irradiance=irr;return 0;}catch(const std::exception&e){lastError=e.what();return -1;}}
API int ls_selection(Renderer*r,const unsigned* mask,unsigned triangles){try{r->select(mask,triangles);return 0;}catch(const std::exception&e){lastError=e.what();return -1;}}
API int ls_quality(Renderer*r,float scaleX,float scaleY,unsigned anisotropy){try{if(!r)throw std::runtime_error("Missing preview renderer");r->quality(scaleX,scaleY,anisotropy);return 0;}catch(const std::exception&e){lastError=e.what();return -1;}}
