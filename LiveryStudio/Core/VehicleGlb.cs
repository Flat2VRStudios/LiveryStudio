using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace LiveryStudio.Core;

// A bounded GLB 2 static-geometry reader. It never resolves external buffers,
// textures or URLs. Materials are assigned through the car's paint document.
public static class VehicleGlb
{
    public static bool WriteObj(string input,string output,CancellationToken cancellation=default)
    {
        var bytes=File.ReadAllBytes(input);if(bytes.Length<20||bytes.Length>256*1024*1024||BinaryPrimitives.ReadUInt32LittleEndian(bytes)!=0x46546c67||BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4))!=2||BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8))!=bytes.Length)throw new InvalidDataException("Choose a valid GLB 2 model smaller than 256 MB.");
        byte[]? json=null,bin=null;var at=12;while(at<bytes.Length){if(at+8>bytes.Length)throw new InvalidDataException("Truncated GLB chunk.");var length=checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at)));var type=BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at+4));at+=8;if(length<0||length%4!=0||length>bytes.Length-at)throw new InvalidDataException("Invalid GLB chunk length.");if(type==0x4e4f534a){if(json is not null)throw new InvalidDataException("Duplicate GLB JSON chunk.");json=bytes.AsSpan(at,length).ToArray();}else if(type==0x004e4942){if(bin is not null)throw new InvalidDataException("Duplicate GLB buffer.");bin=bytes.AsSpan(at,length).ToArray();}at+=length;}
        if(json is null||bin is null)throw new InvalidDataException("The GLB must contain its geometry buffer.");using var document=JsonDocument.Parse(json);var root=document.RootElement;
        if(root.GetProperty("asset").GetProperty("version").GetString()!="2.0")throw new InvalidDataException("Only GLB 2 models are supported.");
        if(root.TryGetProperty("extensionsRequired",out var required)&&required.EnumerateArray().Any(x=>x.GetString() is "KHR_draco_mesh_compression" or "EXT_meshopt_compression"))throw new InvalidDataException("Export the exterior without Draco or meshopt compression.");
        var buffers=root.GetProperty("buffers");if(buffers.GetArrayLength()!=1||buffers[0].TryGetProperty("uri",out _)||buffers[0].GetProperty("byteLength").GetInt32()>bin.Length)throw new InvalidDataException("Use a self-contained GLB with one embedded buffer.");
        var accessors=root.GetProperty("accessors");var views=root.GetProperty("bufferViews");var nodes=root.GetProperty("nodes");var meshes=root.GetProperty("meshes");if(nodes.GetArrayLength()>50000)throw new InvalidDataException("The model has too many scene objects.");
        JsonElement Element(JsonElement array,int index){if(index<0||index>=array.GetArrayLength())throw new InvalidDataException("Invalid GLB object reference.");return array[index];}
        int Num(JsonElement e,string name,int fallback=0)=>e.TryGetProperty(name,out var value)?value.GetInt32():fallback;
        (int Offset,int Stride,int Count,int Component,bool Normalized) Accessor(int index,int components,string type)
        {
            var a=Element(accessors,index);if(a.TryGetProperty("sparse",out _))throw new InvalidDataException("Export the exterior with regular, non-sparse vertex buffers.");if(a.GetProperty("type").GetString()!=type)throw new InvalidDataException("Invalid GLB vertex layout.");var v=Element(views,a.GetProperty("bufferView").GetInt32());if(Num(v,"buffer")!=0||v.TryGetProperty("extensions",out var extensions)&&extensions.TryGetProperty("EXT_meshopt_compression",out _))throw new InvalidDataException("Unsupported compressed/external GLB buffer.");var component=a.GetProperty("componentType").GetInt32();var size=component is 5120 or 5121?1:component is 5122 or 5123?2:component is 5125 or 5126?4:throw new InvalidDataException("Invalid GLB component type.");var width=checked(size*components);var stride=Num(v,"byteStride",width);var count=a.GetProperty("count").GetInt32();var local=Num(a,"byteOffset");var viewOffset=Num(v,"byteOffset");var viewLength=v.GetProperty("byteLength").GetInt32();
            if(count<0||count>6000000||stride<width||stride>252||stride%size!=0||local<0||viewOffset<0||viewLength<0||viewOffset>(long)bin.Length-viewLength||(count>0&&(long)local+(long)(count-1)*stride+width>viewLength)||local%size!=0)throw new InvalidDataException("GLB vertex data exceeds its buffer.");return(checked(viewOffset+local),stride,count,component,a.TryGetProperty("normalized",out var normalized)&&normalized.GetBoolean());
        }
        float Value(int offset,int component,bool normalized)
        {
            var value=component switch{5126=>BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bin.AsSpan(offset))),5121=>(float)bin[offset],5123=>BinaryPrimitives.ReadUInt16LittleEndian(bin.AsSpan(offset)),5120=>(float)(sbyte)bin[offset],5122=>BinaryPrimitives.ReadInt16LittleEndian(bin.AsSpan(offset)),_=>throw new InvalidDataException("Unsupported GLB vertex component.")};if(normalized)value=component switch{5121=>value/255,5123=>value/65535,5120=>Math.Max(-1,value/127),5122=>Math.Max(-1,value/32767),_=>value};if(!float.IsFinite(value))throw new InvalidDataException("The GLB contains invalid vertex values.");return value;
        }
        Vector3[] Vectors3(int index)
        {
            var a=Accessor(index,3,"VEC3");if(a.Component!=5126)throw new InvalidDataException("Use floating-point GLB positions and normals.");var list=new Vector3[a.Count];for(var i=0;i<list.Length;i++){var offset=a.Offset+i*a.Stride;list[i]=new(Value(offset,a.Component,a.Normalized),Value(offset+4,a.Component,a.Normalized),Value(offset+8,a.Component,a.Normalized));}return list;
        }
        Vector2[] Vectors2(int index)
        {
            var a=Accessor(index,2,"VEC2");var size=a.Component is 5121 or 5120?1:a.Component is 5122 or 5123?2:4;var list=new Vector2[a.Count];for(var i=0;i<list.Length;i++){var offset=a.Offset+i*a.Stride;list[i]=new(Value(offset,a.Component,a.Normalized),Value(offset+size,a.Component,a.Normalized));}return list;
        }
        int[] Indices(int index)
        {
            var a=Accessor(index,1,"SCALAR");if(a.Component is not (5121 or 5123 or 5125)||a.Normalized)throw new InvalidDataException("Invalid GLB index type.");var list=new int[a.Count];for(var i=0;i<list.Length;i++){var offset=a.Offset+i*a.Stride;list[i]=a.Component==5121?bin[offset]:a.Component==5123?BinaryPrimitives.ReadUInt16LittleEndian(bin.AsSpan(offset)):checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bin.AsSpan(offset)));}return list;
        }
        float[] Array(JsonElement node,string field,int length,float[] fallback){if(!node.TryGetProperty(field,out var data))return fallback;var result=data.EnumerateArray().Select(x=>x.GetSingle()).ToArray();if(result.Length!=length||result.Any(x=>!float.IsFinite(x)))throw new InvalidDataException("Invalid GLB transform.");return result;}
        Matrix4x4 Local(JsonElement node)
        {
            if(node.TryGetProperty("matrix",out _)){var m=Array(node,"matrix",16,[]);return new(m[0],m[1],m[2],m[3],m[4],m[5],m[6],m[7],m[8],m[9],m[10],m[11],m[12],m[13],m[14],m[15]);}
            var t=Array(node,"translation",3,[0,0,0]);var s=Array(node,"scale",3,[1,1,1]);var r=Array(node,"rotation",4,[0,0,0,1]);var q=new Quaternion(r[0],r[1],r[2],r[3]);if(q.LengthSquared()<1e-12)throw new InvalidDataException("Invalid GLB rotation.");return Matrix4x4.CreateScale(s[0],s[1],s[2])*Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(q))*Matrix4x4.CreateTranslation(t[0],t[1],t[2]);
        }
        var active=new HashSet<int>();var visited=new HashSet<int>();var vertexIndex=0;var triangles=0;var generated=false;using var writer=new StreamWriter(output,false,new UTF8Encoding(false));writer.WriteLine("# Livery Studio static GLB conversion. Y-up, metres.");string F(float f)=>f.ToString("R",CultureInfo.InvariantCulture);
        void Visit(int index,Matrix4x4 parent,int depth)
        {
            cancellation.ThrowIfCancellationRequested();if(depth>128||!active.Add(index)||!visited.Add(index))throw new InvalidDataException("Invalid, repeated or cyclic GLB hierarchy.");var node=Element(nodes,index);if(node.TryGetProperty("skin",out _))throw new InvalidDataException("Export a static rest-pose exterior without skinning.");var world=Local(node)*parent;if(!Matrix4x4.Invert(world,out var inverse))throw new InvalidDataException("The GLB has a zero-scale object.");var normal=Matrix4x4.Transpose(inverse);var mirrored=world.GetDeterminant()<0;
            if(node.TryGetProperty("mesh",out var meshIndex))foreach(var primitive in Element(meshes,meshIndex.GetInt32()).GetProperty("primitives").EnumerateArray())
            {
                if(Num(primitive,"mode",4)!=4)throw new InvalidDataException("The exterior must contain triangle meshes.");if(primitive.TryGetProperty("targets",out _))throw new InvalidDataException("Export the exterior without morph targets.");var attrs=primitive.GetProperty("attributes");var positions=Vectors3(attrs.GetProperty("POSITION").GetInt32());var normals=attrs.TryGetProperty("NORMAL",out var ni)?Vectors3(ni.GetInt32()):null;var uv=attrs.TryGetProperty("TEXCOORD_0",out var ui)?Vectors2(ui.GetInt32()):null;if(normals is not null&&normals.Length!=positions.Length||uv is not null&&uv.Length!=positions.Length)throw new InvalidDataException("GLB attributes have different vertex counts.");var indices=primitive.TryGetProperty("indices",out var ii)?Indices(ii.GetInt32()):Enumerable.Range(0,positions.Length).ToArray();if(indices.Length%3!=0||indices.Any(i=>i<0||i>=positions.Length))throw new InvalidDataException("Invalid GLB triangles.");triangles+=indices.Length/3;if(triangles>2000000)throw new InvalidDataException("The exterior exceeds 2,000,000 triangles.");var transformed=positions.Select(p=>Vector3.Transform(p,world)).ToArray();var lo=new Vector3(float.MaxValue);var hi=new Vector3(float.MinValue);foreach(var p in transformed){if(!float.IsFinite(p.X)||!float.IsFinite(p.Y)||!float.IsFinite(p.Z))throw new InvalidDataException("Invalid GLB transformed coordinates.");lo=Vector3.Min(lo,p);hi=Vector3.Max(hi,p);}var name=node.TryGetProperty("name",out var label)?label.GetString()??"Body":"Body";name=new string(name.Select(c=>char.IsAsciiLetterOrDigit(c)||c=='_'?c:'_').ToArray());writer.WriteLine("o "+(name.Length==0?"Body":name));
                for(var i=0;i<indices.Length;i+=3)
                {
                    if((i&4095)==0)cancellation.ThrowIfCancellationRequested();
                    int[] tri=mirrored?[indices[i],indices[i+2],indices[i+1]]:[indices[i],indices[i+1],indices[i+2]];var face=Vector3.Cross(transformed[tri[1]]-transformed[tri[0]],transformed[tri[2]]-transformed[tri[0]]);if(face.LengthSquared()<1e-20)continue;face=Vector3.Normalize(face);var axis=Math.Abs(face.Y)>Math.Abs(face.X)?1:0;if(Math.Abs(face.Z)>(axis==0?Math.Abs(face.X):Math.Abs(face.Y)))axis=2;var tile=axis*2+((axis==0?face.X:axis==1?face.Y:face.Z)<0?1:0);
                    foreach(var id in tri){var p=transformed[id];var n=normals is null?face:Vector3.TransformNormal(normals[id],normal);if(n.LengthSquared()<1e-20)n=face;else n=Vector3.Normalize(n);Vector2 tex;if(uv is not null)tex=uv[id];else{float A(Vector3 v,int a)=>a==0?v.X:a==1?v.Y:v.Z;var a=(axis+1)%3;var b=(axis+2)%3;tex=new(((tile%3)+.03f+.94f*(A(p,a)-A(lo,a))/Math.Max(1e-8f,A(hi,a)-A(lo,a)))/3,((tile/3)+.03f+.94f*(A(p,b)-A(lo,b))/Math.Max(1e-8f,A(hi,b)-A(lo,b)))/2);generated=true;}writer.WriteLine($"v {F(p.X)} {F(p.Y)} {F(p.Z)}");writer.WriteLine($"vt {F(tex.X)} {F(1-tex.Y)}");writer.WriteLine($"vn {F(n.X)} {F(n.Y)} {F(n.Z)}");}writer.WriteLine($"f {vertexIndex+1}/{vertexIndex+1}/{vertexIndex+1} {vertexIndex+2}/{vertexIndex+2}/{vertexIndex+2} {vertexIndex+3}/{vertexIndex+3}/{vertexIndex+3}");vertexIndex+=3;
                }
            }
            if(node.TryGetProperty("children",out var children))foreach(var child in children.EnumerateArray())Visit(child.GetInt32(),world,depth+1);active.Remove(index);visited.Add(index);
        }
        if(root.TryGetProperty("scenes",out var scenes)&&scenes.GetArrayLength()>0){var scene=Element(scenes,Num(root,"scene"));foreach(var n in scene.GetProperty("nodes").EnumerateArray())Visit(n.GetInt32(),Matrix4x4.Identity,0);}
        else{var children=new HashSet<int>();foreach(var node in nodes.EnumerateArray())if(node.TryGetProperty("children",out var list))foreach(var child in list.EnumerateArray())children.Add(child.GetInt32());for(var i=0;i<nodes.GetArrayLength();i++)if(!children.Contains(i))Visit(i,Matrix4x4.Identity,0);}
        if(vertexIndex==0)throw new InvalidDataException("The GLB contains no usable exterior triangles.");return generated;
    }
}
