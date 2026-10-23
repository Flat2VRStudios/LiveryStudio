// Uses the game's own P3D reader with CPU graphics buffers. This does not
// establish rendered material, physics simulation, or in-game compatibility.
#include "PlayAll/Common/Kernel/Kernel.h"
#include "PlayAll/Common/Core/Entry.h"
#include "PlayAll/Common/Core/CoreModule.h"
#include "PlayAll/Common/Core/VirtualFileSystem.h"
#include "PlayAll/Drivers/Core/PhysicalFileSystem.h"
#include "PlayAll/Drivers/Core/Main.h"
#include "PlayAll/Common/Graphics/GraphicsSys.h"
#include "PlayAll/Common/GraphicsNULL/GraphicsNULLInit.h"
#include "PlayAll/Common/Graphics/VideoDevice.h"
#include "PlayAll/Common/Graphics/P3DResourceManager.h"
#include "PlayAll/Common/Graphics/MeshRscData.h"
#include "PlayAll/Common/Graphics/VertexDeclaration.h"
#include "PlayAll/Common/Graphics/GraphicsResourceManagerSet.h"
#include <iostream>
#include <string>
#include <direct.h>

using namespace PlayAll;
APPLICATION_EXPORT

static void WriteString(const std::string& value)
{
    static const char hex[]="0123456789abcdef";
    std::cout<<'"';
    for(unsigned char c:value)
    {
        if(c=='"'||c=='\\')std::cout<<'\\'<<c;
        else if(c<32)std::cout<<"\\u00"<<hex[c>>4]<<hex[c&15];
        else std::cout<<c;
    }
    std::cout<<'"';
}

static void WriteNode(Graphics::GraphNodeRscData* node, bool& first, int& nodes, int& meshes, uint64& faces)
{
    if(!first)std::cout<<",";first=false;nodes++;
    std::cout<<"{\"name\":";WriteString(node->GetName().c_str());
    std::cout<<",\"mesh\":"<<(node->IsMesh()?"true":"false");
    if(node->IsMesh())
    {
        auto mesh=static_cast<Graphics::MeshRscData*>(node);meshes++;faces+=mesh->GetFaceCount();
        std::cout<<",\"vertices\":"<<mesh->GetVertexCount()<<",\"faces\":"<<mesh->GetFaceCount()<<",\"lods\":"<<mesh->GetLODCount()<<",\"sections\":"<<mesh->GetSubsetCount();
        std::cout<<",\"uvChannels\":[";
        bool firstSubset=true;
        for(auto subset:mesh->GetSubsets())
        {
            if(!firstSubset)std::cout<<",";firstSubset=false;
            auto declaration=subset->GetVertexDeclaration();
            std::cout<<(declaration?declaration->GetNbUvs():0);
        }
        std::cout<<"]";
    }
    std::cout<<"}";
    if(auto children=node->GetChildList())for(auto child:*children)WriteNode(child.Get(),first,nodes,meshes,faces);
}

static int ReadP3D(const char* relativePath)
{
    const auto path=Core::VirtualPath((std::string("PROBE:/")+relativePath).c_str());
    auto manager=Graphics::GraphicsResourceManagerSet::GetInstance()->GetP3DManager();
    Graphics::P3DResourceResPtrStrongPtr holder;
    if(manager->Load(holder,Core::ResourceID(path.c_str()))!=PA_OK||!holder.Get()){std::cerr<<"Native P3D load failed: "<<path.c_str();return 7;}
    auto resource=holder->GetData();
    if(!resource||!resource->GetGraphNodeRscDataHierarchy())return 8;
    bool first=true;int nodes=0,meshes=0;uint64 faces=0;
    std::cout<<"{\"scope\":\"Native P3D reader and CPU hierarchy only\",\"nodes\":[";
    WriteNode(resource->GetGraphNodeRscDataHierarchy(),first,nodes,meshes,faces);
    std::cout<<"],\"nodeCount\":"<<nodes<<",\"meshCount\":"<<meshes<<",\"baseLodFaces\":"<<faces<<",\"resourceLoaded\":true}"<<std::endl;
    return meshes==0||faces==0?10:0;
}

int PlayAllMain(int argc, char* argv[], GenericStartupParameters*)
{
    if(argc!=3&&argc!=4){std::cerr<<"Expected runtime root, relative P3D path and optional package";return 2;}
    PaErrorMessageBox(false);
    Core::CoreModule::Desc coreDesc;
    coreDesc.sDefaultLogFileName="";
    Core::CoreModule core;
    if(core.Initialize(&coreDesc)!=PA_OK)return 4;
    if(Init()!=PA_OK)return 3;
    // Package keys are relative to the storage root. Keep module loading in
    // the private runtime directory, then scope this worker to the read root.
    if(argc==4&&_chdir(argv[1])!=0){End();return 5;}
    if(Core::Mount(Core::PhysicalPath(argv[1]),Core::VirtualPath("PROBE:/"))!=PA_OK){End();return 5;}
    if(argc==4){Core::PhysicalPath package(argv[3]);if(Core::Mount(package.GetParent(),Core::VirtualPath("PKGSRC:/"))!=PA_OK){End();return 11;}auto relative=Core::VirtualPath((std::string("PKGSRC:/")+package.GetName().c_str()).c_str());if(Core::Mount(relative)!=PA_OK){std::cerr<<"Package mount failed"<<std::endl;End();return 11;}}
    Graphics::GraphicsDesc graphics;
    if(Graphics::CreateGraphicsNULL(graphics)!=PA_OK){End();return 6;}
    // The holder is released before tearing down its managers, including on
    // failed resource reads. Each native attempt still runs in its own process.
    const int result=ReadP3D(argv[2]);
    Graphics::DestroyGraphicsNULL();
    End();
    return result;
}
