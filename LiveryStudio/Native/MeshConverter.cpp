#include "third_party/ufbx/ufbx.h"
#include <fstream>
#include <iostream>
#include <string>
#include <vector>
#include <map>
#include <cmath>
#include <algorithm>
struct Vertex { float x,y,z,nx,ny,nz,u,v,kind; };
int main(int argc,char**argv) {
 if(argc<2)return 2;
 ufbx_load_opts opts={};opts.generate_missing_normals=true;opts.evaluate_skinning=true;opts.target_axes=ufbx_axes_right_handed_y_up;
 ufbx_error error={}; auto scene=ufbx_load_file(argv[1],&opts,&error);
 if(!scene){char text[1024];ufbx_format_error(text,sizeof(text),&error);std::cerr<<text;return 1;}
 if(argc==2){for(auto node:scene->nodes){auto p=node->node_to_world.cols[3];std::cout<<node->name.data<<"\t"<<(node->mesh?node->mesh->num_triangles:0)<<"\t"<<p.x<<","<<p.y<<","<<p.z<<"\n";}ufbx_free_scene(scene);return 0;}
 std::map<std::pair<std::string,int>,int> bindings;
 std::ifstream map(argv[2]);std::string name;int material,kind;
 while(std::getline(map,name,'\t') && map>>material>>kind){bindings[{name,material}]=kind;map.ignore(10000,'\n');}
 std::vector<Vertex> output;
 for(auto node:scene->nodes) {
  auto mesh=node->mesh;if(!mesh)continue;
  std::string name(node->name.data,node->name.length);
  auto normalMatrix=ufbx_matrix_for_normals(&node->geometry_to_world);
  std::vector<uint32_t> indices(mesh->max_face_triangles*3);
  for(size_t fi=0;fi<mesh->faces.count;fi++) {
   int mi=mesh->face_material.count?mesh->face_material.data[fi]:0;
   // FBX SDK/PlayAll uses material parts in first-use order for PSI geometry bindings.
   auto order=std::find(mesh->material_part_usage_order.begin(),mesh->material_part_usage_order.end(),mi);
   int geometry=order!=mesh->material_part_usage_order.end()?int(order-mesh->material_part_usage_order.begin()):mi;
   auto binding=bindings.find({name,geometry});if(binding==bindings.end())continue;
   auto face=mesh->faces.data[fi];auto triangles=ufbx_triangulate_face(indices.data(),indices.size(),mesh,face);
   for(size_t vi=0;vi<triangles*3;vi++){
    auto ix=indices[vi];auto p=ufbx_get_vertex_vec3(&mesh->skinned_position,ix);auto n=ufbx_get_vertex_vec3(&mesh->skinned_normal,ix);
    if(mesh->skinned_is_local){p=ufbx_transform_position(&node->geometry_to_world,p);n=ufbx_transform_direction(&normalMatrix,n);}
    double length=std::sqrt(n.x*n.x+n.y*n.y+n.z*n.z);if(length>0){n.x/=length;n.y/=length;n.z/=length;}
    auto uv=mesh->vertex_uv.exists?ufbx_get_vertex_vec2(&mesh->vertex_uv,ix):ufbx_vec2{};
    output.push_back({float(p.x),float(p.y),float(p.z),float(n.x),float(n.y),float(n.z),float(uv.x),float(1-uv.y),float(binding->second)});
   }
  }
 }
 if(output.empty()){std::cerr<<"No bound triangles";ufbx_free_scene(scene);return 3;}
 std::ofstream file(argv[3],std::ios::binary);uint32_t magic=0x4d534c46,count=uint32_t(output.size());file.write((char*)&magic,4);file.write((char*)&count,4);file.write((char*)output.data(),output.size()*sizeof(Vertex));
 std::cout<<output.size()/3<<" triangles\n";ufbx_free_scene(scene);return file?0:4;
}
