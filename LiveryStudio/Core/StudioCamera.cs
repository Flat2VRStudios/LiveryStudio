using System.Numerics;
using System.Text.Json.Serialization;

namespace LiveryStudio.Core;

public sealed record StudioCameraPose(float Yaw,float Pitch,float Distance,float TargetX,float TargetY,float TargetZ,bool Orthographic=false)
{
    [JsonIgnore] public Vector3 Target=>new(TargetX,TargetY,TargetZ);
    [JsonIgnore] public Vector3 Direction=>new(MathF.Sin(Yaw)*MathF.Cos(Pitch),MathF.Sin(Pitch),MathF.Cos(Yaw)*MathF.Cos(Pitch));
    [JsonIgnore] public Vector3 Right=>new(MathF.Cos(Yaw),0,-MathF.Sin(Yaw));
    [JsonIgnore] public Vector3 Up=>Vector3.Normalize(Vector3.Cross(Direction,Right));
    [JsonIgnore] public Vector3 Eye=>Target+Direction*Distance;
    public void Validate()
    {
        if(new[]{Yaw,Pitch,Distance,TargetX,TargetY,TargetZ}.Any(v=>!float.IsFinite(v))||Math.Abs(Pitch)>MathF.PI/2+.00001f||Distance<.12f||Distance>300||Math.Max(Math.Abs(TargetX),Math.Max(Math.Abs(TargetY),Math.Abs(TargetZ)))>1000)throw new ArgumentException("Invalid saved camera view.");
    }
    public Matrix4x4 Matrix(float aspect)
    {
        StudioCamera.ValidateAspect(aspect);
        var far=Math.Max(100,Distance+100);var height=2*Distance*MathF.Tan(StudioCamera.FieldOfView*.5f);
        return Matrix4x4.CreateLookAt(Eye,Target,Up)*(Orthographic?Matrix4x4.CreateOrthographic(height*aspect,height,.03f,far):Matrix4x4.CreatePerspectiveFieldOfView(StudioCamera.FieldOfView,aspect,.03f,far));
    }
}
public sealed record CameraBookmark(int CarIndex,int Slot,StudioCameraPose Pose,float Aspect)
{
    public void Validate(){if(CarIndex<0||CarIndex>=Contract.Cars.Count||Slot<0||Slot>2||Pose is null)throw new ArgumentException("Invalid saved camera slot.");Pose.Validate();StudioCamera.ValidateAspect(Aspect);}
    public StudioCameraPose Restore(float aspect){Validate();StudioCamera.ValidateAspect(aspect);return Pose with {Distance=Math.Clamp(Pose.Distance*Math.Max(1,Aspect/aspect),.12f,300)};}
}
public static class StudioCamera
{
    public const float FieldOfView=.68f;
    public static Vector3[] Geometry(PaintMesh mesh)=>mesh.Vertices.Select(v=>v.Position).Distinct().ToArray();
    public static void ValidateAspect(float aspect){if(!float.IsFinite(aspect)||aspect<.01f||aspect>100)throw new ArgumentException("Invalid camera viewport size.");}
    public static Vector3[] Box(Vector3 min,Vector3 max)=>new[]{new Vector3(min.X,min.Y,min.Z),new Vector3(max.X,min.Y,min.Z),new Vector3(min.X,max.Y,min.Z),new Vector3(max.X,max.Y,min.Z),new Vector3(min.X,min.Y,max.Z),new Vector3(max.X,min.Y,max.Z),new Vector3(min.X,max.Y,max.Z),new Vector3(max.X,max.Y,max.Z)};
    public static Vector3[] Artwork(IReadOnlyList<PaintLayer> layers)=>layers.SelectMany(l=>l.Samples.SelectMany(s=>new[]{new Vector2(-.5f,-.5f),new Vector2(.5f,-.5f),new Vector2(.5f,.5f),new Vector2(-.5f,.5f)}.Select(c=>LayerSets.Corner(l.Transform(s),c)))).ToArray();
    public static StudioCameraPose Fit(IReadOnlyList<Vector3> points,float yaw,float pitch,float aspect,bool orthographic=false,Vector4? viewport=null)
    {
        ValidateAspect(aspect);if(points.Count<1||points.Any(p=>!float.IsFinite(p.X)||!float.IsFinite(p.Y)||!float.IsFinite(p.Z)))throw new ArgumentException("Choose finite geometry to frame.");
        var min=points.Aggregate(Vector3.Min);var max=points.Aggregate(Vector3.Max);var target=(min+max)*.5f;var pose=new StudioCameraPose(yaw,pitch,1,target.X,target.Y,target.Z,orthographic);pose.Validate();
        var rect=viewport??new Vector4(-1,-1,1,1);if(new[]{rect.X,rect.Y,rect.Z,rect.W}.Any(v=>!float.IsFinite(v)||Math.Abs(v)>1)||rect.Z-rect.X<.1f||rect.W-rect.Y<.1f)throw new ArgumentException("Invalid camera framing area.");
        var center=new Vector2((rect.X+rect.Z)*.5f,(rect.Y+rect.W)*.5f);var tanY=MathF.Tan(FieldOfView*.5f);var tanX=tanY*aspect;var distance=.12f;
        foreach(var point in points)
        {
            var offset=point-target;var x=Vector3.Dot(offset,pose.Right)/tanX*1.22f;var y=Vector3.Dot(offset,pose.Up)/tanY*1.22f;var depth=Vector3.Dot(offset,pose.Direction);var projectionDepth=orthographic?0:depth;
            distance=Math.Max(distance,Math.Max(depth+.04f,Math.Max(Math.Max((x+rect.Z*projectionDepth)/(rect.Z-center.X),(-x-rect.X*projectionDepth)/(center.X-rect.X)),Math.Max((y+rect.W*projectionDepth)/(rect.W-center.Y),(-y-rect.Y*projectionDepth)/(center.Y-rect.Y)))));
        }
        target-=pose.Right*(center.X*distance*tanX)+pose.Up*(center.Y*distance*tanY);pose=pose with {Distance=distance,TargetX=target.X,TargetY=target.Y,TargetZ=target.Z};pose.Validate();return pose;
    }
    public static StudioCameraPose Zoom(StudioCameraPose pose,float factor,Vector3? anchor=null)
    {
        pose.Validate();if(!float.IsFinite(factor)||factor<=0)throw new ArgumentException("Invalid camera zoom.");var distance=Math.Clamp(pose.Distance*factor,.12f,300);var ratio=distance/pose.Distance;var pivot=anchor??pose.Target;var target=pivot+(pose.Target-pivot)*ratio;return pose with {Distance=distance,TargetX=target.X,TargetY=target.Y,TargetZ=target.Z};
    }
}
