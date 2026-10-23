using System.Numerics;
using System.Text.Json;
using LiveryStudio.Core;

internal static class CameraChecks
{
    private static Vector3 Project(Vector3 point,StudioCameraPose pose,float aspect){var p=Vector4.Transform(new Vector4(point,1),pose.Matrix(aspect));return new(p.X/p.W,p.Y/p.W,p.Z/p.W);}
    private static bool Fits(Vector3[] points,StudioCameraPose pose,float aspect,Vector4 rect){var matrix=pose.Matrix(aspect);return points.All(p=>{var clip=Vector4.Transform(new Vector4(p,1),matrix);var v=new Vector3(clip.X,clip.Y,clip.Z)/clip.W;return float.IsFinite(v.X)&&v.X>=rect.X-.0001f&&v.X<=rect.Z+.0001f&&v.Y>=rect.Y-.0001f&&v.Y<=rect.W+.0001f&&v.Z>=0&&v.Z<=1;});}
    public static void CheckMesh(PaintMesh mesh,string name,Action<bool,string> check)
    {
        var points=StudioCamera.Geometry(mesh);var good=true;
        foreach(var aspect in new[]{.35f,.8f,1.6f,3.5f})foreach(var angle in new[]{(0f,0f),(MathF.PI,0f),(-MathF.PI/2,0f),(MathF.PI/2,0f),(0f,MathF.PI/2),(.65f,.22f)})foreach(var flat in new[]{false,true})
        {var rect=new Vector4(-.92f,-.7f,.92f,.3f);var pose=StudioCamera.Fit(points,angle.Item1,angle.Item2,aspect,flat,rect);good&=Fits(points,pose,aspect,rect);}
        check(good,"Camera frames real car at six angles, four aspect ratios and both projections: "+name);
        var inspectionRect=new Vector4(-.94f,-.1f,.94f,.24f);var inspectionFits=true;
        foreach(var angle in new[]{(0f,0f),(MathF.PI,0f),(-MathF.PI/2,0f),(MathF.PI/2,0f),(0f,MathF.PI/2),(.65f,.22f)})foreach(var flat in new[]{false,true})
        {var pose=StudioCamera.Fit(points,angle.Item1,angle.Item2,4.02f,flat,inspectionRect);inspectionFits&=Fits(points,pose,4.02f,inspectionRect);}
        check(inspectionFits,"Inspection camera clears the heading and caption in a minimum-height viewport: "+name);
    }
    public static void Run(string temp,Action<bool,string> check,Action<Action,string> reject)
    {
        var points=StudioCamera.Box(new(-2,-1,-3),new(4,2,3));var pose=StudioCamera.Fit(points,.65f,.22f,1.4f);check(Fits(points,pose,1.4f,new(-1,-1,1,1))&&pose.Target==new Vector3(1,.5f,0),"Camera fit uses the actual bounds center rather than the world origin");
        var top=StudioCamera.Fit(points,0,MathF.PI/2,1,true);check(top.Right==Vector3.UnitX&&Math.Abs(Vector3.Dot(top.Right,top.Up))<.0001f&&Fits(points,top,1,new(-1,-1,1,1)),"Exact top working angle has a stable finite camera basis");
        var layer=new PaintLayer{Samples=[new(new(1,2,0),Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,.6f,.2f)],ScaleX=2,Rotation=30};var artwork=StudioCamera.Artwork([layer]);var focused=StudioCamera.Fit(artwork,0,0,1.3f);check(Fits(artwork,focused,1.3f,new(-1,-1,1,1))&&focused.Distance<pose.Distance,"Fit selection uses stretched rotated artwork bounds for a close view");
        var reflected=StudioCamera.Artwork(LayerSets.Display([layer],true));check(Math.Abs(reflected.Average(p=>p.X)+artwork.Average(p=>p.X))<.0001f,"Focus geometry follows the displayed mirrored artwork");
        var anchor=new Vector3(.2f,.1f,0);foreach(var flat in new[]{false,true}){var before=pose with {Orthographic=flat};var after=StudioCamera.Zoom(before,.7f,anchor);var a=Project(anchor,before,1.4f);var b=Project(anchor,after,1.4f);check(Vector2.Distance(new(a.X,a.Y),new(b.X,b.Y))<.0001f,"Pointer-anchored zoom retains the panel's screen position: "+(flat?"flat":"perspective"));}
        var flatPose=pose with {Orthographic=true};var first=Project(new(0,0,0),flatPose,1.4f);var second=Project(flatPose.Direction*2,flatPose,1.4f);check(Vector2.Distance(new(first.X,first.Y),new(second.X,second.Y))<.0001f,"Flat working view removes perspective scaling along the view direction");
        var saved=new CameraBookmark(0,1,focused with {Orthographic=true},1.3f);var json=JsonSerializer.Serialize(saved,Contract.Json);var restored=JsonSerializer.Deserialize<CameraBookmark>(json,Contract.Json)!;check(restored.Pose==saved.Pose&&!json.Contains("Direction")&&!json.Contains("Target\""),"Saved camera JSON retains only independent pose values");
        check(restored.Restore(.65f).Distance==saved.Pose.Distance*2&&restored.Restore(2).Distance==saved.Pose.Distance,"Saved view adapts to a narrower viewport without changing its original pose");
        var image=Path.Combine(temp,"camera-base.tga");FinishMaps.WriteTga(new(2,2,new byte[16]),image);var project=new LiveryProject{Albedo=image,Structure=image,CameraBookmarks=[saved]};var archive=Path.Combine(temp,"camera.flproject");ProjectBundle.Save(project,archive);var reopened=ProjectBundle.Open(archive,Path.Combine(temp,"camera-open"));check(reopened.CameraBookmarks.Single()==saved,"Portable projects retain saved camera angles with the editable design");
        var id="BODY_01234567890123456789012345678901";check(WorkshopService.MaterialXml(project,id)==WorkshopService.MaterialXml(project with {CameraBookmarks=[]},id),"Camera bookmarks leave the native game material unchanged");
        var matrix=focused.Matrix(1.3f);Matrix4x4.Invert(matrix,out var inverse);Vector3 Unproject(float z){var p=Vector4.Transform(new Vector4(0,0,z,1),inverse);return new(p.X/p.W,p.Y/p.W,p.Z/p.W);}var near=Unproject(0);var far=Unproject(1);check(Vector3.Dot(Vector3.Normalize(far-near),-focused.Direction)>.999f,"Camera projection inverts into the expected picking ray");
        float[] Vertex(float x,float z,float u,float v)=>[x,0,z,0,1,0,u,v,1];var panel=new PaintMesh([..Vertex(0,1,0,0),..Vertex(2,1,1,0),..Vertex(2,3,1,1),..Vertex(0,1,0,0),..Vertex(2,3,1,1),..Vertex(0,3,0,1)]);var ink=new PaintImage(2,2,Enumerable.Repeat((byte)255,16).ToArray());var sticker=new PaintLayer{Asset=image,Samples=[new(new(1,0,2),Vector3.UnitY,Vector3.UnitX,-Vector3.UnitZ,1,1)]};
        foreach(var flat in new[]{false,true}){var view=StudioCamera.Fit(StudioCamera.Artwork([sticker]),0,MathF.PI/2,1.3f,flat);Matrix4x4.Invert(view.Matrix(1.3f),out var inv);Vector3 RayPoint(float z){var p=Vector4.Transform(new Vector4(0,0,z,1),inv);return new(p.X/p.W,p.Y/p.W,p.Z/p.W);}var a=RayPoint(0);var hit=panel.Raycast(a,RayPoint(1)-a);check(hit is {} surface&&Vector3.Distance(surface.Position,new(1,0,2))<.001f&&DecalEditing.Pick(panel,[sticker],surface,64,64,_=>ink)?.LayerId==sticker.Id,"Top-view picking reaches the visible decal: "+(flat?"flat":"perspective"));check(DecalEditing.PlanePoint(new(650,500),view.Matrix(1.3f),1300,1000,sticker.Samples[0],out var point)&&Vector3.Distance(point,new(1,0,2))<.001f,"Direct manipulation unprojects onto the artwork plane: "+(flat?"flat":"perspective"));}
        reject(()=>StudioCamera.Fit([],0,0,1),"Camera fit rejects an empty selection");reject(()=>StudioCamera.Fit([new(float.NaN,0,0)],0,0,1),"Camera fit rejects invalid geometry");reject(()=>StudioCamera.Fit(points,0,0,0),"Camera fit rejects a zero-width viewport");reject(()=>StudioCamera.Fit(points,0,0,1,viewport:new(-1,0,1,0)),"Camera fit rejects an empty framing rectangle");reject(()=>StudioCamera.Zoom(pose,float.NaN),"Camera zoom rejects non-finite factors");reject(()=>new CameraBookmark(27,0,pose,1).Validate(),"Saved camera rejects a foreign car index");reject(()=>new CameraBookmark(0,3,pose,1).Validate(),"Saved camera rejects a foreign slot");reject(()=>(project with {CameraBookmarks=[saved,saved]}).Validate(textureSources:false),"Project rejects duplicate camera slots");
    }
}
