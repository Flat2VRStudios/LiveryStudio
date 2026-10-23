using System.Numerics;
using LiveryStudio.Core;

internal static class SharedUvChecks
{
    public static void Run(string repo,string folder,Action<bool,string> check)
    {
        var values=new List<float>();
        void Panel(float x,float u)
        {
            foreach(var p in new[]{new Vector2(0,0),new(1,0),new(1,1),new(0,0),new(1,1),new(0,1)})values.AddRange([x+p.X,p.Y,0,0,0,1,u+p.X,p.Y,1]);
        }
        Panel(0,0);Panel(2,.8f);Panel(4,2);var synthetic=new PaintMesh(values.ToArray());var safe=SimplePlacement.UnsharedExterior(synthetic);
        check(!safe.Contains(0)&&!safe.Contains(2)&&safe.Contains(4)&&safe.Contains(5),"Shared-area protection finds partial UV overlaps away from triangle centers and keeps unique panels paintable");
        values.Clear();Panel(0,0);Panel(1,1);Panel(0,0);var seams=new PaintMesh(values.ToArray());check(SimplePlacement.UnsharedExterior(seams).Count==6,"Shared-area protection permits neighboring UV edges and coincident geometry");
        var mesh=PaintMesh.Read(Path.Combine(repo,"LiveryStudio/artifacts/models/00.flsm"));var hit=mesh.Raycast(new(.8f,-.3f,5),-Vector3.UnitZ)!.Value;
        check(MeshEditing.SharedUvCount(mesh,hit)>=2,"Actual Grosse pointe bumper reproduces shared texture locations on both front corners");
        var projection=SurfaceProjection.At(hit,Vector3.UnitX,.45f,1,0);var layer=SimplePlacement.OnSurface(new PaintLayer{Kind=LayerKind.Color,Color="#21DCC1",Samples=[projection]});var p=layer.Transform(projection);
        var blank=new PaintImage(1024,1024,new byte[1024*1024*4]);var linked=new TexturePainter(blank);var protectedPaint=new TexturePainter(blank);
        linked.Stamp(mesh,p,null,Vector3.One,1,Vector3.Zero,SimplePlacement.Exterior(mesh,p.Normal));
        protectedPaint.Stamp(mesh,p,null,Vector3.One,1,Vector3.Zero,SimplePlacement.Mask(mesh,layer,p.Normal));
        var at=((int)(hit.UV.Y*blank.Height)*blank.Width+(int)(hit.UV.X*blank.Width))*4;
        check(linked.Image.Pixels[at+3]>0&&protectedPaint.Image.Pixels[at+3]==0,"Simple paint skips the actual shared bumper texel instead of changing the opposite corner");
        var door=mesh.Raycast(new(5,-.06f,.68f),-Vector3.UnitX)!.Value;
        check(SimplePlacement.UnsharedExterior(mesh).Contains(door.Triangle),"The earlier outer-door target remains paintable after bumper protection");
        var file=Path.Combine(folder,"shared-base.tga");FinishMaps.WriteTga(blank,file);var project=new LiveryProject{Albedo=file,Structure=file};var doc=new LayerDocument{BaseAlbedo=file,BaseStructure=file};doc.Commit("Protected paint",layers=>layers.Add(layer));var maps=doc.Compose(mesh,project);
        var saved=Path.Combine(folder,"protected.json");doc.Save(saved);var reopened=LayerDocument.Read(saved);
        check(reopened.Version==9&&reopened.Layers[0].ProtectSharedUvs&&maps.Albedo.Pixels[at+3]==0&&reopened.Compose(mesh,project).Albedo.Pixels.SequenceEqual(maps.Albedo.Pixels),"Shared-area protection survives save and reopen and agrees with live painting");
        reopened.Undo();check(reopened.Compose(mesh,project).Albedo.Pixels.All(b=>b==0),"Undoing protected paint restores the complete untouched texture");reopened.Redo();check(reopened.Compose(mesh,project).Albedo.Pixels.SequenceEqual(maps.Albedo.Pixels),"Redo restores the protected stroke without linked bumper paint");
    }
}
