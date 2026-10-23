using System.Numerics;
using System.Text.Json;
using LiveryStudio.Core;

internal static class PhotographyChecks
{
    public static void Run(string repo,string temp,Action<bool,string> check,Action<Action,string> reject)
    {
        var model=PaintMesh.Read(Path.Combine(repo,"LiveryStudio/artifacts/models/00.flsm"));var points=StudioCamera.Geometry(model);
        var camera=StudioCamera.Fit(points,.65f,.22f,2);var square=PhotoLayout.Current(model,camera,2,1024,1024,false);
        check(square.Yaw==camera.Yaw&&square.Pitch==camera.Pitch&&square.Target==camera.Target&&square.Distance==camera.Distance*2,"Changing export aspect preserves current camera angle and safely widens framing");
        var fitted=PhotoLayout.Current(model,camera,2,1920,1080,true);
        bool Fits(StudioCameraPose pose,float aspect)=>points.All(p=>{var v=Vector4.Transform(new Vector4(p,1),pose.Matrix(aspect));return v.W>0&&Math.Abs(v.X/v.W)<=1.001f&&Math.Abs(v.Y/v.W)<=1.001f;});
        check(Fits(fitted,1920f/1080),"Full-car photo framing contains actual car geometry");
        foreach(var (width,height) in new[]{(1280,720),(1920,1080),(3840,2160),(1024,1024),(1080,1350)})
        {
            var tiles=PhotoLayout.Sheet(model,width,height);
            check(tiles.Count==4&&tiles.Select(t=>t.Label).Distinct().Count()==4&&tiles.All(t=>t.X>=0&&t.Y>=0&&t.X+t.Width<=width&&t.Y+t.Height<=height&&Fits(t.Camera,(float)t.Width/t.Height)),"Four-angle sheet frames every view within its output: "+width+"x"+height);
            check(!tiles.SelectMany((a,i)=>tiles.Skip(i+1).Select(b=>a.X<b.X+b.Width&&a.X+a.Width>b.X&&a.Y<b.Y+b.Height&&a.Y+a.Height>b.Y)).Any(x=>x),"Photo sheet views do not overlap: "+width+"x"+height);
        }
        var poses=PhotoLayout.Turntable(model,camera.Yaw,camera.Pitch,320,180);
        check(poses.Count==48&&poses.Select(p=>p.Distance).Distinct().Count()==1&&poses.Select(p=>p.Target).Distinct().Count()==1&&poses.All(p=>Fits(p,320f/180)),"Turntable contains the whole car at every frame without zoom or center pulsing");
        check(poses.Select(p=>MathF.Round(MathF.Sin(p.Yaw),5)+","+MathF.Round(MathF.Cos(p.Yaw),5)).Distinct().Count()==48,"Turntable covers unique evenly spaced views without a duplicate final frame");
        foreach(var bad in new[]{(0,720),(1920,0),(9000,720),(1920,int.MaxValue)})reject(()=>PhotoLayout.ValidateSize(bad.Item1,bad.Item2),"Reject invalid photo size "+bad);
        reject(()=>PhotoLayout.Turntable(model,0,0,320,180,10000),"Turntable frame count is bounded");
        reject(()=>new PhotoLighting{Exposure=float.NaN}.Validate(),"Reject nonfinite photo lighting");
        reject(()=>new PhotoLighting{Specular="x"}.Validate(),"Reject an incomplete photo environment");
        using var canceled=new CancellationTokenSource();canceled.Cancel();
        var project=ProjectBundle.Open(Path.Combine(repo,"examples/OppositeSideStudioDemo.flproject"),Path.Combine(temp,"photography"));var document=LayerDocument.Read(project.EditorFile);
        var before=JsonSerializer.Serialize(document,LayerDocument.Json);var beforeProject=JsonSerializer.Serialize(project,Contract.Json);
        var app=Path.Combine(repo,"LiveryStudio/artifacts/photo-runtime");
        reject(()=>new StudioPhotography(app,model,document,project,new(),canceled.Token),"Canceled photo preparation does not create a renderer");
        if(!Directory.Exists(app))throw new DirectoryNotFoundException("Photography GPU checks need artifacts/photo-runtime with the bundled Preview assets.");
        using(var photography=new StudioPhotography(app,model,document,project,new()))
        {
            var first=photography.Capture(poses[0],320,180);var repeat=photography.Capture(poses[0],320,180);var reverse=photography.Capture(poses[24],320,180);
            check(first.Pixels.SequenceEqual(repeat.Pixels)&&!first.Pixels.SequenceEqual(reverse.Pixels),"Independent photo GPU renders are repeatable and show distinct sides of the real car");
            check(first.Pixels.Where((_,i)=>i%4!=3).Distinct().Count()>100&&first.Pixels.Where((_,i)=>i%4==3).All(a=>a==255),"Photo output is a shaded opaque car render rather than an empty frame");
            reject(()=>photography.Capture(poses[0],320,180,canceled.Token),"Canceled frame capture stops without changing the live document");
            check(first.Pixels.SequenceEqual(photography.Capture(poses[0],320,180).Pixels),"A canceled capture leaves the private renderer usable");
            var old=project.OuterColor;project.OuterColor="#FFFFFF";check(first.Pixels.SequenceEqual(photography.Capture(poses[0],320,180).Pixels),"A prepared photography context freezes project settings across later caller changes");project.OuterColor=old;
            var full=photography.Capture(fitted,3840,2160);check(full.Width==3840&&full.Height==2160&&full.Pixels.Length==3840*2160*4,"Photo output resolution is independent of the application window and supports 4K");
            var tiles=PhotoLayout.Sheet(model,1280,720);var sheet=PhotoSheet.Render(project.Name,project.SelectedCar.Display,1280,720,tiles,t=>photography.Capture(t.Camera,t.Width,t.Height));
            check(sheet.Width==1280&&sheet.Height==720&&sheet.Pixels.Where((_,i)=>i%4!=3).Distinct().Count()>100,"Four-angle sheet combines real GPU views and readable title artwork");
            FinishMaps.WriteTga(sheet,Path.Combine(temp,"photography-sheet.tga"));
            photography.Dispose();reject(()=>photography.Capture(poses[0],320,180),"Disposed photography contexts cannot access released GPU resources");
        }
        check(JsonSerializer.Serialize(document,LayerDocument.Json)==before&&JsonSerializer.Serialize(project,Contract.Json)==beforeProject,"Photo preparation framing rendering and cancellation preserve the entire editor history and project");
        var scene=StudioPhotography.Scene(project,new(),document.HasMaterial,camera,2);
        check(scene[43]==0&&scene[66]==0&&scene[71]==0,"Photography scenes exclude painting hover, panel restrictions and mirrored ghost overlays");
    }
}
