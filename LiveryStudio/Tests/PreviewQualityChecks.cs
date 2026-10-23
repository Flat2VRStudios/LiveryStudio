using System.Numerics;
using System.Runtime.InteropServices;
using LiveryStudio.Core;

internal static class PreviewQualityChecks
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate nint Create();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate void Destroy(nint renderer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int Quality(nint renderer,float scaleX,float scaleY,uint filtering);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int Render(nint renderer,float[] scene,uint count,uint width,uint height);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int Mesh(nint renderer,float[] vertices,uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int Texture(nint renderer,byte[] pixels,uint width,uint height,int slot);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int Snapshot(nint renderer,byte[] pixels,uint capacity);
    public static void Run(string repo,string temp,Action<bool,string> check,Action<Action,string> reject)
    {
        var balanced=new PreviewQualitySettings();var low=PreviewQualitySettings.Preset(PreviewQuality.Low);var high=PreviewQualitySettings.Preset(PreviewQuality.High);
        check(balanced.RenderSize(800,450,2.5,2.5)==new PreviewRenderSize(2000,1125),"Balanced preview renders physical pixels at 250% Windows scaling");
        check(low.RenderSize(800,480,2.5,2.5)==new PreviewRenderSize(1500,900)&&high.RenderSize(800,480,2.5,2.5)==new PreviewRenderSize(2500,1500),"Low and High preview presets offer 75% and 125% physical resolution");
        check(balanced.RenderSize(800,450,1,1)==new PreviewRenderSize(800,450),"Unscaled displays retain native Balanced resolution");
        check(balanced.RenderSize(800,450,1.5,2)==new PreviewRenderSize(1200,900),"Independent composition scales are included for transformed XAML panels");
        var large=high.RenderSize(7680,4320,3,3);check(large.Width==3840&&large.Height==2160&&large.Pixels<=PreviewQualitySettings.MaximumPixels,"Preview dimensions stay within a 4K pixel budget on very large displays");
        var square=high.RenderSize(6000,6000,2,2);check(square.Pixels<=PreviewQualitySettings.MaximumPixels&&Math.Abs((double)square.Width/square.Height-1)<.001,"The preview pixel budget preserves aspect ratio for square canvases");
        var portrait=high.RenderSize(1200,3600,2,2);check(portrait.Width==1280&&portrait.Height==3840,"Portrait canvases preserve their shape within the maximum edge");
        check(low.RenderSize(.1,.1,1,1)==new PreviewRenderSize(1,1),"Tiny positive preview panels keep a valid render target");
        reject(()=>balanced.RenderSize(double.NaN,450,1,1),"Reject non-finite preview dimensions");reject(()=>balanced.RenderSize(800,450,0,1),"Reject an invalid composition scale");
        reject(()=>(balanced with {Quality=(PreviewQuality)99}).Validate(),"Reject unsupported preview quality presets");reject(()=>(balanced with {Filtering=(PreviewFiltering)2}).Validate(),"Reject unsupported filtering values");
        var workspace=Path.Combine(temp,"preview-quality");check(PreviewQualitySettings.Read(workspace)==balanced,"Missing preview preferences default to DPI-aware Balanced quality");
        var customized=high with {Filtering=PreviewFiltering.Maximum};customized.Save(workspace);check(PreviewQualitySettings.Read(workspace)==customized,"Resolution and texture filtering preferences survive saving and reopening");
        File.WriteAllText(PreviewQualitySettings.FilePath(workspace),"{\"Version\":20}");reject(()=>PreviewQualitySettings.Read(workspace),"Reject future preview preference formats without changing a project");
        File.WriteAllText(PreviewQualitySettings.FilePath(workspace),new string('x',16*1024+1));reject(()=>PreviewQualitySettings.Read(workspace),"Bound preview preference file size");

        if(!OperatingSystem.IsWindows())return;
        var library=NativeLibrary.Load(Path.Combine(repo,"LiveryStudio/artifacts/native/LiveryPreview.dll"));
        try
        {
            T Get<T>(string name)where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library,name));
            var renderer=Get<Create>("ls_create_offscreen")();check(renderer!=0,"Create renderer for quality control pixel checks");if(renderer==0)return;
            try
            {
                var quality=Get<Quality>("ls_quality");var render=Get<Render>("ls_render_ex");var snapshot=Get<Snapshot>("ls_snapshot");
                check(quality(renderer,.4f,.4f,4)==0&&quality(renderer,.32f,.32f,8)==0,"Native preview accepts DPI compensation and improved filtering");
                check(quality(renderer,float.NaN,1,4)!=0&&quality(renderer,1,1,17)!=0&&quality(renderer,0,1,4)!=0,"Invalid quality values are rejected before changing native state");
                float[] vertices=[-1,-1,0,0,0,1,0,1,1,1,-1,0,0,0,1,1,1,1,1,1,0,0,0,1,1,0,1,-1,-1,0,0,0,1,0,1,1,1,1,0,0,0,1,1,0,1,-1,1,0,0,0,1,0,0,1];
                check(Get<Mesh>("ls_mesh")(renderer,vertices,6)==0,"Upload geometry for quality resolution checks");check(Get<Texture>("ls_texture")(renderer,[255,60,20,255],1,1,0)==0,"Upload unchanged source artwork for quality checks");
                var matrix=Matrix4x4.CreateLookAt(new(0,0,3),Vector3.Zero,Vector3.UnitY)*Matrix4x4.CreatePerspectiveFieldOfView(.8f,1,.03f,100);var scene=new float[76];
                float[] values=[matrix.M11,matrix.M12,matrix.M13,matrix.M14,matrix.M21,matrix.M22,matrix.M23,matrix.M24,matrix.M31,matrix.M32,matrix.M33,matrix.M34,matrix.M41,matrix.M42,matrix.M43,matrix.M44];values.CopyTo(scene,0);scene[18]=3;scene[20]=scene[21]=scene[22]=scene[24]=scene[25]=scene[26]=.3f;scene[23]=.7f;scene[54]=1;scene[55]=2;scene[65]=1;
                foreach(var preset in new[]{low,balanced,high})
                {
                    var size=preset.RenderSize(128,128,2.5,2.5);check(quality(renderer,(float)(128d/size.Width),(float)(128d/size.Height),(uint)preset.Filtering)==0&&render(renderer,scene,76,size.Width,size.Height)==0,"Render quality preset at computed physical dimensions: "+preset.Quality);
                    var pixels=new byte[checked((int)(size.Pixels*4))];check(snapshot(renderer,pixels,(uint)pixels.Length)==0&&pixels.Where((_,i)=>i%4!=3).Distinct().Count()>2,"Read complete shaded frame after quality change: "+preset.Quality);
                }
            }
            finally{Get<Destroy>("ls_destroy")(renderer);}
        }
        finally{NativeLibrary.Free(library);}
    }
}
