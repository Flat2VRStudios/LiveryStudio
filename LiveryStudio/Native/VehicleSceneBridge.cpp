// A narrow bundled scene composer. Donor hierarchy/pivots and retained mesh
// sections survive; custom static geometry is fitted and exported for the
// existing native compiler. No game installation is changed by this process.
#include <fbxsdk.h>
#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <limits>
#include <map>
#include <set>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>

struct Binding { std::string role, resource; int original=-1; };
struct Corner { FbxVector4 p,n; FbxVector2 uv,uv1; FbxColor color{1,1,1,1}; bool hasUv1=false,hasColor=false; int cp=-1; };
struct Face { std::array<Corner,3> v; int material=0; };
struct Bounds {
    FbxVector4 lo{1e30,1e30,1e30},hi{-1e30,-1e30,-1e30};
    void Add(const FbxVector4& p){for(int i=0;i<3;i++){if(!std::isfinite(p[i]))throw std::runtime_error("Mesh contains invalid coordinates");lo[i]=std::min(lo[i],p[i]);hi[i]=std::max(hi[i],p[i]);}}
};
using BindingMap=std::map<std::pair<std::string,int>,Binding>;
static void Json(std::ostream& out,const std::string& s){static const char h[]="0123456789abcdef";out<<'"';for(unsigned char c:s){if(c=='"'||c=='\\')out<<'\\'<<c;else if(c<32)out<<"\\u00"<<h[c>>4]<<h[c&15];else out<<c;}out<<'"';}
static std::string Upper(std::string s){for(auto&c:s)c=(char)toupper((unsigned char)c);return s;}
static bool Lod(const std::string& s){return Upper(s).find("_LOD")!=std::string::npos;}
static bool Hidden(const std::string& s){auto n=Upper(s);return Lod(n)||n.find("BREAK")!=n.npos||n.find("$PHYS")!=n.npos||n.find("ARROW")!=n.npos||n.find("RTT")!=n.npos||n.find("MIRROR_SPACE")!=n.npos;}
static FbxAMatrix Geometry(FbxNode* n){FbxAMatrix m;m.SetT(n->GetGeometricTranslation(FbxNode::eSourcePivot));m.SetR(n->GetGeometricRotation(FbxNode::eSourcePivot));m.SetS(n->GetGeometricScaling(FbxNode::eSourcePivot));return n->EvaluateGlobalTransform()*m;}
static FbxVector4 Direction(const FbxAMatrix& m,const FbxVector4& n){auto v=m.MultT(n)-m.MultT(FbxVector4(0,0,0));v[3]=0;auto l=v.Length();if(l>1e-15)v/=l;return v;}
static void Nodes(FbxNode* n,std::vector<FbxNode*>& list){if(!n)return;list.push_back(n);for(int i=0;i<n->GetChildCount();i++)Nodes(n->GetChild(i),list);}
static int Mat(FbxMesh* m,int p){FbxLayerElementMaterial* e=nullptr;for(int i=0;i<m->GetLayerCount()&&!e;i++)e=m->GetLayer(i)->GetMaterials();if(!e)return 0;auto index=e->GetMappingMode()==FbxLayerElement::eAllSame?0:p;return index<e->GetIndexArray().GetCount()?e->GetIndexArray().GetAt(index):0;}
static std::vector<int> Sections(FbxMesh* m){std::vector<int> result;for(int p=0;p<m->GetPolygonCount();p++){int id=Mat(m,p);if(std::find(result.begin(),result.end(),id)==result.end())result.push_back(id);}std::sort(result.begin(),result.end());return result;}
static Face ReadFace(FbxMesh* m,int polygon){
    if(m->GetPolygonSize(polygon)!=3)throw std::runtime_error("Mesh triangulation failed");
    Face f;f.material=Mat(m,polygon);FbxStringList names;m->GetUVSetNames(names);
    for(int j=0;j<3;j++){auto&v=f.v[j];v.cp=m->GetPolygonVertex(polygon,j);v.p=m->GetControlPointAt(v.cp);if(!m->GetPolygonVertexNormal(polygon,j,v.n))v.n=FbxVector4();bool unmapped=false;if(names.GetCount()>0)m->GetPolygonVertexUV(polygon,j,names[0],v.uv,unmapped);else v.uv=FbxVector2(-1e30,-1e30);
        if(names.GetCount()>1){v.hasUv1=m->GetPolygonVertexUV(polygon,j,names[1],v.uv1,unmapped)&&!unmapped;}
        if(auto colors=m->GetElementVertexColor()){int id=-1;switch(colors->GetMappingMode()){case FbxLayerElement::eByControlPoint:id=v.cp;break;case FbxLayerElement::eByPolygonVertex:id=m->GetPolygonVertexIndex(polygon)+j;break;case FbxLayerElement::eByPolygon:id=polygon;break;case FbxLayerElement::eAllSame:id=0;break;default:break;}if(id>=0){if(colors->GetReferenceMode()!=FbxLayerElement::eDirect){id=id<colors->GetIndexArray().GetCount()?colors->GetIndexArray().GetAt(id):-1;}if(id>=0&&id<colors->GetDirectArray().GetCount()){v.color=colors->GetDirectArray().GetAt(id);v.hasColor=true;}}}
    }
    auto normal=(f.v[1].p-f.v[0].p).CrossProduct(f.v[2].p-f.v[0].p);normal.Normalize();
    for(auto&v:f.v)if(v.n.Length()<1e-12)v.n=normal;
    return f;
}
static FbxScene* Load(FbxManager* manager,const char* path){
    if(std::filesystem::file_size(std::filesystem::u8path(path))>256ull*1024*1024)throw std::runtime_error("Choose a mesh smaller than 256 MB");
    auto scene=FbxScene::Create(manager,"");auto importer=FbxImporter::Create(manager,"");
    if(!importer->Initialize(path,-1,manager->GetIOSettings())||!importer->Import(scene)){std::string error=importer->GetStatus().GetErrorString();importer->Destroy();throw std::runtime_error("Could not read mesh: "+error);}
    importer->Destroy();if(Upper(std::filesystem::u8path(path).extension().u8string())==".OBJ")scene->GetGlobalSettings().SetSystemUnit(FbxSystemUnit::m);FbxAxisSystem::Max.ConvertScene(scene);FbxGeometryConverter converter(manager);if(!converter.Triangulate(scene,true))throw std::runtime_error("Could not triangulate the scene");return scene;
}
static BindingMap ReadBindings(const char* path){BindingMap result;std::ifstream file(std::filesystem::u8path(path));std::string row;while(std::getline(file,row)){std::istringstream line(row);std::string name,section,role,resource;std::getline(line,name,'\t');std::getline(line,section,'\t');std::getline(line,role,'\t');std::getline(line,resource,'\t');if(name.empty()||section.empty())continue;result[{name,std::stoi(section)}]={role,resource,std::stoi(section)};}if(result.empty())throw std::runtime_error("Donor material sections are missing");return result;}
static Binding Lookup(const BindingMap& bindings,FbxNode* node,const std::vector<int>& sections,int material){auto it=std::find(sections.begin(),sections.end(),material);auto index=(int)(it-sections.begin());auto match=bindings.find({node->GetName(),index});return match==bindings.end()?Binding{"other","",index}:match->second;}
static FbxMesh* MakeMesh(FbxScene* scene,FbxNode* node,const std::vector<Face>& faces){
    auto mesh=FbxMesh::Create(scene,(std::string(node->GetName())+"_Assembly").c_str());
    auto normals=mesh->CreateElementNormal();normals->SetMappingMode(FbxLayerElement::eByPolygonVertex);normals->SetReferenceMode(FbxLayerElement::eDirect);
    // The native importer resolves channels by these exact names, not FBX
    // element order. Other names silently produce geometry without UVs.
    auto uv=mesh->CreateElementUV("UVChannel_1");uv->SetMappingMode(FbxLayerElement::eByPolygonVertex);uv->SetReferenceMode(FbxLayerElement::eDirect);
    bool hasUv1=false,hasColor=false;for(auto&f:faces)for(auto&v:f.v){hasUv1|=v.hasUv1;hasColor|=v.hasColor;}
    auto uv1=hasUv1?mesh->CreateElementUV("UVChannel_2"):nullptr;if(uv1){uv1->SetMappingMode(FbxLayerElement::eByPolygonVertex);uv1->SetReferenceMode(FbxLayerElement::eDirect);}
    auto colors=hasColor?mesh->CreateElementVertexColor():nullptr;if(colors){colors->SetMappingMode(FbxLayerElement::eByPolygonVertex);colors->SetReferenceMode(FbxLayerElement::eDirect);}
    auto material=mesh->CreateElementMaterial();material->SetMappingMode(FbxLayerElement::eByPolygon);material->SetReferenceMode(FbxLayerElement::eIndexToDirect);
    std::map<std::array<double,3>,int> positions;std::vector<FbxVector4> points;std::vector<std::array<int,3>> indices;
    for(const auto&face:faces){std::array<int,3> tri;for(int i=0;i<3;i++){auto p=face.v[i].p;std::array<double,3> key{p[0],p[1],p[2]};auto inserted=positions.emplace(key,(int)points.size());if(inserted.second)points.push_back(p);tri[i]=inserted.first->second;}indices.push_back(tri);}
    mesh->InitControlPoints((int)points.size());for(size_t i=0;i<points.size();i++)mesh->SetControlPointAt(points[i],(int)i);
    for(size_t i=0;i<faces.size();i++){mesh->BeginPolygon(faces[i].material);for(int j=0;j<3;j++){mesh->AddPolygon(indices[i][j]);normals->GetDirectArray().Add(faces[i].v[j].n);uv->GetDirectArray().Add(faces[i].v[j].uv);if(uv1)uv1->GetDirectArray().Add(faces[i].v[j].hasUv1?faces[i].v[j].uv1:faces[i].v[j].uv);if(colors)colors->GetDirectArray().Add(faces[i].v[j].color);}mesh->EndPolygon();}
    // The compiler takes the first layer material element. Creating a second
    // UV set can leave an empty element ahead of the polygon assignments.
    material->GetIndexArray().SetCount((int)faces.size());for(size_t i=0;i<faces.size();i++)material->GetIndexArray().SetAt((int)i,faces[i].material);
    mesh->GetLayer(0)->SetMaterials(material);for(int i=1;i<mesh->GetLayerCount();i++)mesh->GetLayer(i)->SetMaterials(nullptr);
    if(colors){mesh->GetLayer(0)->SetVertexColors(colors);for(int i=1;i<mesh->GetLayerCount();i++)mesh->GetLayer(i)->SetVertexColors(nullptr);}
    node->SetNodeAttribute(mesh);return mesh;
}
static void WriteBounds(std::ostream& out,const Bounds& bounds){out<<"[";for(int row=0;row<2;row++){if(row)out<<",";out<<"[";for(int i=0;i<3;i++){if(i)out<<",";out<<(row?bounds.hi[i]:bounds.lo[i]);}out<<"]";}out<<"]";}
static int Run(int argc,char**argv){
    auto manager=FbxManager::Create();if(!manager)throw std::runtime_error("FBX scene service could not start");
    struct Guard{FbxManager*p;~Guard(){p->Destroy();}}guard{manager};auto settings=FbxIOSettings::Create(manager,IOSROOT);manager->SetIOSettings(settings);settings->SetBoolProp(IMP_FBX_TEXTURE,false);settings->SetBoolProp(IMP_FBX_MATERIAL,true);settings->SetBoolProp(EXP_FBX_EMBEDDED,false);
    if(argc==4&&std::string(argv[1])=="inspect"){
        auto scene=Load(manager,argv[2]);std::vector<FbxNode*> nodes;Nodes(scene->GetRootNode(),nodes);Bounds bounds;uint64_t faces=0;
        for(auto node:nodes)if(auto mesh=node->GetMesh()){auto world=Geometry(node);faces+=mesh->GetPolygonCount();for(int i=0;i<mesh->GetControlPointsCount();i++)bounds.Add(world.MultT(mesh->GetControlPointAt(i)));}
        if(faces==0||faces>2000000)throw std::runtime_error("Choose a static exterior with 1 to 2,000,000 triangles");
        std::ofstream out(std::filesystem::u8path(argv[3]));out<<std::setprecision(12)<<"{\"triangles\":"<<faces<<",\"unitCentimeters\":"<<scene->GetGlobalSettings().GetSystemUnit().GetScaleFactor()<<",\"gameSpaceBounds\":";WriteBounds(out,bounds);out<<"}";return out?0:4;
    }
    if(argc==5&&std::string(argv[1])=="exterior"){
        auto scene=Load(manager,argv[2]);auto bindings=ReadBindings(argv[3]);std::vector<FbxNode*> nodes;Nodes(scene->GetRootNode(),nodes);std::vector<float> values;
        for(auto node:nodes)if(auto mesh=node->GetMesh()){if(Hidden(node->GetName()))continue;auto sections=Sections(mesh);auto world=Geometry(node);auto normalMatrix=world.Inverse().Transpose();for(int p=0;p<mesh->GetPolygonCount();p++)if(Lookup(bindings,node,sections,Mat(mesh,p)).role=="paint")for(auto&v:ReadFace(mesh,p).v){auto point=world.MultT(v.p);auto normal=Direction(normalMatrix,v.n);for(float f:{(float)point[0],(float)point[2],(float)-point[1],(float)normal[0],(float)normal[2],(float)-normal[1],(float)v.uv[0],(float)(1-v.uv[1]),1.f})values.push_back(f);}}
        std::ofstream out(std::filesystem::u8path(argv[4]),std::ios::binary);uint32_t magic=0x4d534c46,count=(uint32_t)(values.size()/9);out.write((char*)&magic,4);out.write((char*)&count,4);out.write((char*)values.data(),values.size()*sizeof(float));return out&&count>0?0:4;
    }
    if(argc==8&&std::string(argv[1])=="paint-sides"){
        // UV0 remains the factory mapping for stock skins, tangent space,
        // normal maps and weathering. Only the dedicated paint UV1 changes.
        auto scene=Load(manager,argv[2]);auto bindings=ReadBindings(argv[3]);std::vector<FbxNode*> nodes;Nodes(scene->GetRootNode(),nodes);std::vector<float> vertices;std::vector<std::string> meshNames;Bounds bounds;uint64_t changed=0,paint=0;
        std::ofstream mapping(std::filesystem::u8path(argv[5]));
        for(auto node:nodes)if(auto original=node->GetMesh()){
            auto sections=Sections(original);auto world=Geometry(node);auto normalMatrix=world.Inverse().Transpose();std::vector<Face> faces;std::map<int,Binding> maps;
            for(int p=0;p<original->GetPolygonCount();p++){
                auto face=ReadFace(original,p);auto bind=Lookup(bindings,node,sections,face.material);maps[face.material]=bind;
                if(bind.role=="paint"||bind.role=="interiorpaint"){
                    double x=0;for(auto&v:face.v)x+=world.MultT(v.p)[0];int side=x>=0?1:0;
                    for(auto&v:face.v){if(v.uv[0]<-.001||v.uv[0]>1.001||v.uv[1]<-.001||v.uv[1]>1.001)throw std::runtime_error("This car has repeating paint UVs outside the supported atlas");v.uv1=FbxVector2((std::clamp(v.uv[0],0.,1.)+side)*.5,std::clamp(v.uv[1],0.,1.));v.hasUv1=true;}
                    if(!Hidden(node->GetName()))paint++;changed++;
                }
                faces.push_back(face);
            }
            node->RemoveNodeAttribute(original);auto mesh=MakeMesh(scene,node,faces);if(Upper(node->GetName()).find("$PHYS")==std::string::npos)meshNames.push_back(node->GetName());auto newSections=Sections(mesh);
            for(size_t s=0;s<newSections.size();s++){auto bind=maps.at(newSections[s]);if(bind.resource.empty()||Upper(node->GetName()).find("$PHYS")!=std::string::npos)continue;mapping<<node->GetName()<<'\t'<<s<<'\t'<<bind.original<<'\t'<<bind.role<<'\t'<<((bind.role=="paint"||bind.role=="interiorpaint")?"__EXTERIOR__":bind.resource)<<'\n';}
            if(Hidden(node->GetName()))continue;
            for(auto&f:faces){auto bind=maps.at(f.material);float kind=bind.role=="paint"||bind.role=="interiorpaint"?1.f:bind.role=="glass"?2.f:bind.role=="optics"?4.f:0.f;for(auto&v:f.v){auto p=world.MultT(v.p);auto n=Direction(normalMatrix,v.n);bounds.Add(p);auto uv=kind==1?v.uv1:v.uv;for(float value:{(float)p[0],(float)p[2],(float)-p[1],(float)n[0],(float)n[2],(float)-n[1],(float)uv[0],(float)(1-uv[1]),kind})vertices.push_back(value);}}
        }
        auto exporter=FbxExporter::Create(manager,"");if(!exporter->Initialize(argv[4],-1,manager->GetIOSettings())||!exporter->Export(scene))throw std::runtime_error(std::string("Could not export independent paint mapping: ")+exporter->GetStatus().GetErrorString());exporter->Destroy();
        std::ofstream preview(std::filesystem::u8path(argv[6]),std::ios::binary);uint32_t magic=0x4d534c46,count=(uint32_t)(vertices.size()/9);preview.write((char*)&magic,4);preview.write((char*)&count,4);preview.write((char*)vertices.data(),vertices.size()*sizeof(float));
        std::ofstream report(std::filesystem::u8path(argv[7]));report<<std::setprecision(12)<<"{\"importedTriangles\":"<<paint<<",\"removedExteriorTriangles\":0,\"retainedInteriorTriangles\":0,\"generatedBoxUv\":false,\"unitCentimeters\":"<<scene->GetGlobalSettings().GetSystemUnit().GetScaleFactor()<<",\"gameSpaceBounds\":";WriteBounds(report,bounds);report<<",\"meshNodes\":[";for(size_t i=0;i<meshNames.size();i++){if(i)report<<',';Json(report,meshNames[i]);}report<<"],\"emptyNodes\":[],\"independentPaintTriangles\":"<<changed<<"}";
        if(!mapping||!preview||!report||!paint)throw std::runtime_error("Independent paint mapping is incomplete");return 0;
    }
    if(argc!=18||std::string(argv[1])!="assemble")throw std::runtime_error("Expected inspect or assemble with a complete scene recipe");
    auto donor=Load(manager,argv[2]);auto custom=Load(manager,argv[3]);auto bindings=ReadBindings(argv[4]);
    donor->GetGlobalSettings().GetSystemUnit().ConvertScene(custom);
    std::vector<FbxNode*> nodes,imports;Nodes(donor->GetRootNode(),nodes);Nodes(custom->GetRootNode(),imports);std::map<std::string,FbxNode*> byName;Bounds target,source;std::set<std::string> paintBases;
    for(auto node:nodes){byName[node->GetName()]=node;if(auto mesh=node->GetMesh()){auto sections=Sections(mesh);auto world=Geometry(node);for(int p=0;p<mesh->GetPolygonCount();p++)if(Lookup(bindings,node,sections,Mat(mesh,p)).role=="paint"){paintBases.insert(node->GetName());if(!Lod(node->GetName()))for(auto&v:ReadFace(mesh,p).v)target.Add(world.MultT(v.p));}}}
    if(!byName.count("Body")||paintBases.empty())throw std::runtime_error("This donor does not expose a supported Body paint mesh");
    uint64_t inputFaces=0;for(auto node:imports)if(auto mesh=node->GetMesh()){if(mesh->GetDeformerCount(FbxDeformer::eSkin)>0)throw std::runtime_error("Export a static rest-pose exterior before importing");auto world=Geometry(node);inputFaces+=mesh->GetPolygonCount();for(int p=0;p<mesh->GetPolygonCount();p++)for(auto&v:ReadFace(mesh,p).v)source.Add(world.MultT(v.p));}
    if(inputFaces==0||inputFaces>2000000)throw std::runtime_error("Choose an exterior with 1 to 2,000,000 triangles");
    double fit=std::stoi(argv[10])?(target.hi[1]-target.lo[1])/std::max(1e-8,source.hi[1]-source.lo[1]):1.;
    FbxVector4 scale(fit*std::stod(argv[11]),fit*std::stod(argv[12]),fit*std::stod(argv[13]));double yaw=std::stod(argv[14])*3.141592653589793/180.;FbxAMatrix fitting;fitting.SetS(scale);fitting.SetR(FbxVector4(0,0,yaw*180./3.141592653589793));
    auto sourceCenter=(source.lo+source.hi)*.5;sourceCenter[2]=source.lo[2];auto targetCenter=(target.lo+target.hi)*.5;targetCenter[2]=target.lo[2];auto shift=std::stoi(argv[10])?targetCenter-fitting.MultT(sourceCenter):FbxVector4();for(int i=0;i<3;i++)shift[i]+=std::stod(argv[15+i]);fitting.SetT(shift);
    std::map<FbxNode*,std::vector<Face>> output;std::map<FbxNode*,std::map<int,Binding>> maps;uint64_t keptInterior=0,removed=0,added=0;int generatedUv=0;
    for(auto node:nodes)if(auto mesh=node->GetMesh()){
        if(Upper(node->GetName()).find("$PHYS")!=std::string::npos)continue;
        auto sections=Sections(mesh);bool changedLod=false;auto name=std::string(node->GetName());auto suffix=Upper(name).find("_LOD");if(suffix!=name.npos)changedLod=paintBases.count(name.substr(0,suffix))>0;
        for(int p=0;p<mesh->GetPolygonCount();p++){auto f=ReadFace(mesh,p);auto bind=Lookup(bindings,node,sections,f.material);bool interior=bind.role=="interior"||bind.role=="interiorpaint";bool drop=bind.role=="paint"||(changedLod&&bind.role=="other")||(std::stoi(argv[9])==0&&interior);if(drop){removed++;continue;}output[node].push_back(f);maps[node][f.material]=bind;if(interior)keptInterior++;}
        // Record empty former exterior meshes as well, so their original
        // geometry is replaced by a transform node rather than left visible.
        output[node];
    }
    for(auto node:imports)if(auto mesh=node->GetMesh()){
        if(Lod(node->GetName()))continue;
        auto match=byName.find(node->GetName());auto destination=match!=byName.end()&&paintBases.count(match->first)&&!Lod(match->first)?match->second:byName.at("Body");
        auto inputWorld=Geometry(node);auto destinationInverse=Geometry(destination).Inverse();auto transform=destinationInverse*fitting*inputWorld;auto normalMatrix=transform.Inverse().Transpose();
        int customMaterial=destination->GetMaterialCount();auto material=FbxSurfacePhong::Create(donor,"LiveryStudioExterior");destination->AddMaterial(material);maps[destination][customMaterial]={"paint","__EXTERIOR__",-1};
        for(int p=0;p<mesh->GetPolygonCount();p++){auto face=ReadFace(mesh,p);face.material=customMaterial;auto wa=inputWorld.MultT(face.v[0].p),wb=inputWorld.MultT(face.v[1].p),wc=inputWorld.MultT(face.v[2].p);auto worldNormal=(wb-wa).CrossProduct(wc-wa);int axis=0;if(std::abs(worldNormal[1])>std::abs(worldNormal[axis]))axis=1;if(std::abs(worldNormal[2])>std::abs(worldNormal[axis]))axis=2;int tile=axis*2+(worldNormal[axis]<0?1:0);for(auto&v:face.v){auto world=inputWorld.MultT(v.p);v.p=transform.MultT(v.p);v.n=Direction(normalMatrix,v.n);if(v.uv[0]<-1e20){int a=(axis+1)%3,b=(axis+2)%3;double u=(world[a]-source.lo[a])/std::max(1e-8,source.hi[a]-source.lo[a]);double w=(world[b]-source.lo[b])/std::max(1e-8,source.hi[b]-source.lo[b]);v.uv=FbxVector2(((tile%3)+.03+.94*u)/3.,((tile/3)+.03+.94*w)/2.);generatedUv=1;}}if(transform.Determinant()<0)std::swap(face.v[1],face.v[2]);output[destination].push_back(face);added++;}
    }
    std::ofstream mapping(std::filesystem::u8path(argv[6]));std::ofstream preview(std::filesystem::u8path(argv[7]),std::ios::binary);std::vector<float> vertices;std::vector<std::string> meshNames,emptyNames;Bounds assembled;
    for(auto node:nodes){auto it=output.find(node);if(it==output.end())continue;auto&faces=it->second;if(faces.empty()){node->SetNodeAttribute(FbxNull::Create(donor,""));emptyNames.push_back(node->GetName());continue;}auto mesh=MakeMesh(donor,node,faces);meshNames.push_back(node->GetName());auto sections=Sections(mesh);for(size_t s=0;s<sections.size();s++){auto bind=maps[node].at(sections[s]);mapping<<node->GetName()<<'\t'<<s<<'\t'<<bind.original<<'\t'<<bind.role<<'\t'<<bind.resource<<'\n';}
        if(Hidden(node->GetName()))continue;auto world=Geometry(node);auto normalMatrix=world.Inverse().Transpose();for(auto&f:faces){auto bind=maps[node].at(f.material);float kind=bind.role=="paint"||bind.role=="interiorpaint"?1.f:bind.role=="glass"?2.f:bind.role=="optics"?4.f:0.f;for(auto&v:f.v){auto p=world.MultT(v.p);auto n=Direction(normalMatrix,v.n);assembled.Add(p);for(float value:{(float)p[0],(float)p[2],(float)-p[1],(float)n[0],(float)n[2],(float)-n[1],(float)v.uv[0],(float)(1-v.uv[1]),kind})vertices.push_back(value);}}
    }
    uint32_t magic=0x4d534c46,count=(uint32_t)(vertices.size()/9);preview.write((char*)&magic,4);preview.write((char*)&count,4);preview.write((char*)vertices.data(),vertices.size()*sizeof(float));preview.close();mapping.close();
    auto exporter=FbxExporter::Create(manager,"");if(!exporter->Initialize(argv[5],-1,manager->GetIOSettings())||!exporter->Export(donor))throw std::runtime_error(std::string("Could not export assembly: ")+exporter->GetStatus().GetErrorString());exporter->Destroy();
    std::ofstream report(std::filesystem::u8path(argv[8]));report<<std::setprecision(12)<<"{\"importedTriangles\":"<<added<<",\"removedExteriorTriangles\":"<<removed<<",\"retainedInteriorTriangles\":"<<keptInterior<<",\"generatedBoxUv\":"<<(generatedUv?"true":"false")<<",\"unitCentimeters\":"<<donor->GetGlobalSettings().GetSystemUnit().GetScaleFactor()<<",\"gameSpaceBounds\":";WriteBounds(report,assembled);report<<",\"meshNodes\":[";for(size_t i=0;i<meshNames.size();i++){if(i)report<<',';Json(report,meshNames[i]);}report<<"],\"emptyNodes\":[";for(size_t i=0;i<emptyNames.size();i++){if(i)report<<',';Json(report,emptyNames[i]);}report<<"]}";
    if(!report||!preview||!mapping||added==0)throw std::runtime_error("Assembly output is incomplete");return 0;
}
int main(int argc,char**argv){try{return Run(argc,argv);}catch(const std::exception& error){std::cerr<<error.what()<<std::endl;return 1;}}
