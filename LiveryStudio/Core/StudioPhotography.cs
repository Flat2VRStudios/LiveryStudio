using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace LiveryStudio.Core;

public sealed record PhotoLighting
{
    public float Exposure {get;init;}
    public float LightAngle {get;init;}=120;
    public float SunStrength {get;init;}=1;
    public float NormalStrength {get;init;}=1;
    public bool Normals {get;init;}=true;
    public float Dirt {get;init;}
    public float Damage {get;init;}
    public int DirtType {get;init;}
    public int EnvironmentMode {get;init;}
    public string Specular {get;init;}="";
    public string Irradiance {get;init;}="";
    public void Validate()
    {
        if(!float.IsFinite(Exposure)||Exposure is <-2 or >2||!float.IsFinite(LightAngle)||LightAngle is <0 or >360||
            !float.IsFinite(SunStrength)||SunStrength is <0 or >10||!float.IsFinite(NormalStrength)||NormalStrength is <0 or >2||
            !float.IsFinite(Dirt)||Dirt is <0 or >1||!float.IsFinite(Damage)||Damage is <0 or >1||DirtType is <0 or >3||EnvironmentMode is <0 or >2)
            throw new ArgumentException("Invalid photo lighting settings.");
        if((Specular.Length==0)!=(Irradiance.Length==0))throw new ArgumentException("Choose a complete reflection environment.");
    }
}

// A private renderer keeps exported images independent of window size, tool
// overlays, hover previews and the editor's mutable camera/texture state.
public sealed class StudioPhotography : IDisposable
{
    private nint renderer;
    private readonly LiveryProject project;
    private readonly PhotoLighting lighting;
    private readonly bool hasMaterial;
    public StudioPhotography(string appFolder,PaintMesh mesh,LayerDocument document,LiveryProject project,PhotoLighting lighting,CancellationToken cancellation=default,CompositeMaps? preparedMaps=null,PaintImage? originalMask=null)
    {
        lighting.Validate();document.Validate();project.Validate();cancellation.ThrowIfCancellationRequested();
        document=document.Snapshot("Photo",document.Layers);project=JsonSerializer.Deserialize<LiveryProject>(JsonSerializer.Serialize(project,Contract.Json),Contract.Json)!;
        this.project=DesignPalette.ResolvePaint(document,project);this.lighting=lighting;hasMaterial=document.HasMaterial;
        var maps=preparedMaps??document.Compose(mesh,this.project,cancellation);
        try
        {
            renderer=ls_create_offscreen();if(renderer==0)Check(-1);
            Check(ls_mesh(renderer,mesh.NativeVertices,(uint)mesh.Vertices.Length));
            Upload(maps.Albedo,0);Upload(maps.Structure,2);var original=originalMask??FinishMaps.Read(document.BaseAlbedo,cancellation);Upload(document.History[document.Cursor].CleanPaint?new(original.Width,original.Height,new byte[original.Pixels.Length]):original,8);
            PaintImage Weathering(string file){var image=FinishMaps.Read(file,cancellation);return project.IndependentPaintSides?IndependentPaintSides.Atlas(image):image;}
            var car=Path.Combine(appFolder,"Preview",project.CarIndex.ToString("00"));
            foreach(var (name,slot) in new[]{("normal",3),("dirt-surface",5),("damage",6),("damage-normal",7)})Upload(Weathering(Path.Combine(car,name+".png")),slot);
            Upload(Weathering(Path.Combine(car,"dirt-"+new[]{"dust","mud","sand","snow"}[lighting.DirtType]+".png")),4);
            Check(ls_environment(renderer,lighting.Specular,lighting.Irradiance));cancellation.ThrowIfCancellationRequested();
        }
        catch{Dispose();throw;}
    }
    public PaintImage Capture(StudioCameraPose pose,int width,int height,CancellationToken cancellation=default)
    {
        if(renderer==0)throw new ObjectDisposedException(nameof(StudioPhotography));
        if(width<32||height<32||width>4096||height>4096||(long)width*height>16_777_216)throw new ArgumentException("Invalid photo frame size.");
        pose.Validate();cancellation.ThrowIfCancellationRequested();
        var scene=Scene(project,lighting,hasMaterial,pose,(float)width/height);Check(ls_render_ex(renderer,scene,(uint)scene.Length,(uint)width,(uint)height));cancellation.ThrowIfCancellationRequested();
        var pixels=new byte[checked(width*height*4)];Check(ls_snapshot(renderer,pixels,(uint)pixels.Length));cancellation.ThrowIfCancellationRequested();return new(width,height,pixels);
    }
    internal static float[] Scene(LiveryProject project,PhotoLighting lighting,bool hasMaterial,StudioCameraPose pose,float aspect)
    {
        var m=pose.Matrix(aspect);var values=new float[76];float[] matrix=[m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];matrix.CopyTo(values,0);
        void V(int at,Vector3 v,float w){values[at]=v.X;values[at+1]=v.Y;values[at+2]=v.Z;values[at+3]=w;}
        var probe=lighting.Specular.Length>0;
        V(16,pose.Eye,0);V(20,PaintFinish.PaintColor(project,project.OuterColor),(float)project.OuterGloss);V(24,PaintFinish.PaintColor(project,project.InnerColor),hasMaterial?1:0);
        V(48,new((float)project.Metalness,(float)project.ClearCoat,(float)project.InnerGloss),project.OverrideArtworkFinish&&!hasMaterial?1:0);
        V(52,new((float)project.ArtworkGloss,(float)project.ArtworkMetalness,lighting.Exposure),lighting.EnvironmentMode);
        V(56,new(lighting.LightAngle*MathF.PI/180,lighting.NormalStrength,lighting.Dirt),lighting.Damage);
        V(60,new((float)project.DamageGloss,(float)project.DamageMetalness,lighting.Normals?1:0),probe?1:0);
        V(64,new(probe?.6f:1,lighting.SunStrength,0),.85f);return values;
    }
    private void Upload(PaintImage image,int slot)=>Check(ls_texture(renderer,image.Pixels,(uint)image.Width,(uint)image.Height,slot));
    private static void Check(int result){if(result!=0)throw new InvalidOperationException(Marshal.PtrToStringAnsi(ls_error())??"The photo renderer could not create this view.");}
    public void Dispose(){if(renderer!=0){ls_destroy(renderer);renderer=0;}}
    [DllImport("LiveryPreview",CallingConvention=CallingConvention.Cdecl)]private static extern nint ls_create_offscreen();
    [DllImport("LiveryPreview",CallingConvention=CallingConvention.Cdecl)]private static extern void ls_destroy(nint renderer);
    [DllImport("LiveryPreview",CallingConvention=CallingConvention.Cdecl)]private static extern nint ls_error();
    [DllImport("LiveryPreview",CallingConvention=CallingConvention.Cdecl)]private static extern int ls_mesh(nint renderer,float[] values,uint count);
    [DllImport("LiveryPreview",CallingConvention=CallingConvention.Cdecl)]private static extern int ls_texture(nint renderer,byte[] pixels,uint width,uint height,int slot);
    [DllImport("LiveryPreview",CallingConvention=CallingConvention.Cdecl)]private static extern int ls_render_ex(nint renderer,float[] values,uint floats,uint width,uint height);
    [DllImport("LiveryPreview",CallingConvention=CallingConvention.Cdecl)]private static extern int ls_snapshot(nint renderer,[Out]byte[] pixels,uint capacity);
    [DllImport("LiveryPreview",CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Unicode)]private static extern int ls_environment(nint renderer,string specular,string irradiance);
}

public sealed record PhotoTile(int X,int Y,int Width,int Height,string Label,StudioCameraPose Camera);
public static class PhotoLayout
{
    public static void ValidateSize(int width,int height)
    {
        if(width<256||height<144||width>4096||height>4096||(long)width*height>16_777_216)throw new ArgumentException("Choose a photo between 256 × 144 and 4,096 × 4,096 pixels.");
    }
    public static StudioCameraPose Current(PaintMesh mesh,StudioCameraPose camera,float oldAspect,int width,int height,bool fit)
    {
        ValidateSize(width,height);camera.Validate();StudioCamera.ValidateAspect(oldAspect);var aspect=(float)width/height;
        return fit?StudioCamera.Fit(StudioCamera.Geometry(mesh),camera.Yaw,camera.Pitch,aspect,camera.Orthographic):camera with {Distance=Math.Clamp(camera.Distance*Math.Max(1,oldAspect/aspect),.12f,300)};
    }
    public static IReadOnlyList<PhotoTile> Sheet(PaintMesh mesh,int width,int height)
    {
        ValidateSize(width,height);var margin=Math.Max(8,width/80);var heading=Math.Max(40,height/12);var label=Math.Max(18,height/40);
        var tileWidth=(width-margin*3)/2;var tileHeight=(height-heading-margin*3)/2-label;
        if(tileWidth<64||tileHeight<48)throw new ArgumentException("Choose a larger four-angle sheet.");
        var points=StudioCamera.Geometry(mesh);var views=new[]{("Front left",-.65f,.22f),("Front right",.65f,.22f),("Rear three-quarter",2.5f,.22f),("Top",0f,MathF.PI/2)};
        return views.Select((view,i)=>new PhotoTile(margin+i%2*(tileWidth+margin),heading+margin+i/2*(tileHeight+label+margin),tileWidth,tileHeight,view.Item1,StudioCamera.Fit(points,view.Item2,view.Item3,(float)tileWidth/tileHeight))).ToArray();
    }
    public static IReadOnlyList<StudioCameraPose> Turntable(PaintMesh mesh,float startYaw,float pitch,int width,int height,int frames=48)
    {
        ValidateSize(width,height);if(!float.IsFinite(startYaw)||!float.IsFinite(pitch)||Math.Abs(pitch)>MathF.PI/2||frames is <8 or >120)throw new ArgumentException("Choose a turntable with 8–120 frames and a valid camera angle.");
        startYaw=MathF.IEEERemainder(startYaw,MathF.Tau);var points=StudioCamera.Geometry(mesh);var poses=Enumerable.Range(0,frames).Select(i=>StudioCamera.Fit(points,startYaw+MathF.Tau*i/frames,pitch,(float)width/height)).ToArray();
        var distance=poses.Max(p=>p.Distance);return poses.Select(p=>p with {Distance=distance}).ToArray();
    }
}
