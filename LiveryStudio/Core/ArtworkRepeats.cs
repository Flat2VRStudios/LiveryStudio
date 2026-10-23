using System.Numerics;
using System.Text.Json;

namespace LiveryStudio.Core;

public enum RepeatArrangement { Line, Grid, Circle }
public sealed record ArtworkRepeatOptions
{
    public RepeatArrangement Arrangement {get;init;}
    public int Count {get;init;}=4;
    public int Columns {get;init;}=3;
    public int Rows {get;init;}=2;
    public float Gap {get;init;}=.03f;
    public float RowGap {get;init;}=.03f;
    public float Direction {get;init;}
    public float Radius {get;init;}=.3f;
    public float Scale {get;init;}=1;
    public bool RotateAroundCircle {get;init;}=true;
    public bool KeepPanels {get;init;}=true;
    public bool CenterPattern {get;init;}=true;
    public int Total=>Arrangement==RepeatArrangement.Grid?checked(Columns*Rows):Count;
    public void Validate()
    {
        if(!Enum.IsDefined(Arrangement)||Count is <2 or >64||Columns is <1 or >32||Rows is <1 or >32||Total is <2 or >64||
            !float.IsFinite(Gap)||Gap is <0 or >5||!float.IsFinite(RowGap)||RowGap is <0 or >5||
            !float.IsFinite(Direction)||Direction is <-180 or >180||!float.IsFinite(Radius)||Radius is <.005f or >5||!float.IsFinite(Scale)||Scale is <.05f or >3)
            throw new ArgumentException("Choose 2–64 copies, 5–300% size, gaps up to 500 cm, and a circle radius from 0.5 to 500 cm.");
    }
}

public sealed class ArtworkRepeatPlan
{
    private readonly LayerDocument source;
    private readonly string original,copies,movedOriginals;
    public IReadOnlyList<Guid> CopyIds {get;}
    public IReadOnlyList<Guid> PatternIds {get;}
    public int TotalLayers {get;}
    public string Label {get;}
    internal static string State(LayerDocument document)=>JsonSerializer.Serialize(new{document.Version,document.CarIndex,document.BaseAlbedo,document.BaseStructure,document.SeamPadding,document.Cursor,Checkpoint=document.History[document.Cursor]},LayerDocument.Json);
    internal ArtworkRepeatPlan(LayerDocument document,List<PaintLayer> moved,List<PaintLayer> added,IEnumerable<Guid> selected,RepeatArrangement arrangement)
    {
        source=document;original=State(document);copies=JsonSerializer.Serialize(added,LayerDocument.Json);
        movedOriginals=JsonSerializer.Serialize(moved,LayerDocument.Json);
        CopyIds=Array.AsReadOnly(added.Select(l=>l.Id).ToArray());PatternIds=Array.AsReadOnly(selected.Concat(CopyIds).ToArray());
        TotalLayers=document.Layers.Count+added.Count;Label="Repeated artwork · "+arrangement.ToString().ToLowerInvariant();
    }
    public void Apply(LayerDocument document)
    {
        if(!ReferenceEquals(document,source)||State(document)!=original)throw new InvalidOperationException("The design changed during the repeat preview. Select the artwork and try again.");
        var independent=JsonSerializer.Deserialize<List<PaintLayer>>(copies,LayerDocument.Json)!;
        var moved=JsonSerializer.Deserialize<List<PaintLayer>>(movedOriginals,LayerDocument.Json)!;
        document.Commit(Label,layers=>{foreach(var layer in moved){var at=layers.FindIndex(l=>l.Id==layer.Id);layers[at]=layer;}layers.AddRange(independent);});
    }
}

public static class ArtworkRepeats
{
    public static ArtworkRepeatPlan Create(LayerDocument document,IEnumerable<Guid> selection,ArtworkRepeatOptions options,bool mirrored=false,CancellationToken cancellation=default,PaintMesh? mesh=null)
    {
        cancellation.ThrowIfCancellationRequested();options.Validate();
        var ids=selection.ToHashSet();var selected=document.Layers.Where(l=>ids.Contains(l.Id)).ToArray();
        if(ids.Count==0||ids.Count!=selected.Length)throw new InvalidOperationException("Select artwork that is still in this design.");
        LayerSets.RequireEditable(selected);foreach(var layer in selected)layer.Validate();
        if(selected.Any(l=>!l.Visible))throw new InvalidOperationException("Show the selected artwork before repeating it.");
        var newCount=(options.Total-1L)*selected.Length;
        if(document.Layers.Count+newCount>512)throw new InvalidOperationException("This pattern would exceed the project's 512-layer limit. Choose fewer copies.");
        if((options.Total-1L)*selected.Sum(l=>(long)l.Samples.Count)>200_000)throw new InvalidOperationException("This selection contains too many brush samples to repeat at that count. Choose fewer copies or a smaller selection.");
        // Clone recipes, masks, group identities and brush lists before any
        // transform. A preview cannot mutate its caller or share mutable copies.
        var frozen=JsonSerializer.Deserialize<List<PaintLayer>>(JsonSerializer.Serialize(selected,LayerDocument.Json),LayerDocument.Json)!;
        var shown=LayerSets.Display(frozen,mirrored);var frame=LayerSets.Frame(shown);var added=new List<PaintLayer>();var movedOriginals=new List<PaintLayer>();
        for(var i=0;i<options.Total;i++)
        {
            if(i==0&&!options.CenterPattern&&options.Scale==1)continue;
            cancellation.ThrowIfCancellationRequested();float right,up,rotation=0;
            if(options.Arrangement==RepeatArrangement.Grid)
            {right=(i%options.Columns-(options.CenterPattern?(options.Columns-1)*.5f:0))*(frame.Width*options.Scale+options.Gap);up=-(i/options.Columns-(options.CenterPattern?(options.Rows-1)*.5f:0))*(frame.Height*options.Scale+options.RowGap);}
            else if(options.Arrangement==RepeatArrangement.Circle)
            {var angle=MathF.Tau*i/options.Total;right=options.Radius*(MathF.Cos(angle)-(options.CenterPattern?0:1));up=options.Radius*MathF.Sin(angle);rotation=options.RotateAroundCircle?angle*180/MathF.PI:0;}
            else
            {var angle=options.Direction*MathF.PI/180;var step=((Math.Abs(MathF.Cos(angle))*frame.Width+Math.Abs(MathF.Sin(angle))*frame.Height)*options.Scale+options.Gap)*(i-(options.CenterPattern?(options.Total-1)*.5f:0));right=step*MathF.Cos(angle);up=step*MathF.Sin(angle);}
            var moved=LayerSets.Edit(shown,right,up,scale:options.Scale,rotation:rotation);
            var groups=new Dictionary<Guid,Guid>();
            foreach(var layer in moved)
            {
                cancellation.ThrowIfCancellationRequested();
                var copy=JsonSerializer.Deserialize<PaintLayer>(JsonSerializer.Serialize(layer,LayerDocument.Json),LayerDocument.Json)!;
                if(i>0){copy.Id=Guid.NewGuid();copy.Name=layer.Name[..Math.Min(85,layer.Name.Length)]+" · repeat "+(i+1);copy.Locked=false;
                    if(copy.GroupId is {} group){if(!groups.TryGetValue(group,out var next))groups[group]=next=Guid.NewGuid();copy.GroupId=next;}}
                if(!options.KeepPanels)copy.Triangles=[];
                if(mesh is not null)
                {
                    var panels=copy.Triangles.Length==0?null:copy.Triangles.ToHashSet();var sourceDepth=copy.WrapDepth;
                    copy.Samples=copy.Samples.Select(p=>
                    {
                        cancellation.ThrowIfCancellationRequested();
                        // Moving along the projection normal preserves its image
                        // coordinates while restoring contact after resizing an
                        // older decal whose plane was offset from the body.
                        var edge=MathF.Max(p.Width,p.Height);var reach=edge/options.Scale*sourceDepth;
                        var hit=Contact(mesh,p,reach,panels);if(hit is not {} body)return p;
                        var displacement=Vector3.Dot(body.Position-p.Center,p.Normal);
                        if(copy.Mirror&&Contact(mesh,PaintLayer.Reflected(p),reach,panels) is {} other)
                        {
                            var reflected=PaintLayer.Reflected(p);var opposite=Vector3.Dot(other.Position-reflected.Center,reflected.Normal);
                            var depth=(Math.Abs(displacement-opposite)*.5f+.001f)/edge;
                            if(depth>2)throw new InvalidOperationException("These paired body sides need more reach at this size. Increase copy size or separate paired sides first.");
                            copy.WrapDepth=Math.Max(copy.WrapDepth,depth);displacement=(displacement+opposite)*.5f;
                        }
                        return p with {Center=p.Center+p.Normal*displacement};
                    }).ToList();
                }
                if(mirrored)copy.Samples=copy.Samples.Select(PaintLayer.Reflected).ToList();
                copy.Validate();if(i==0)movedOriginals.Add(copy);else added.Add(copy);
            }
        }
        cancellation.ThrowIfCancellationRequested();return new(document,movedOriginals,added,selected.Select(l=>l.Id),options.Arrangement);
    }
    private static SurfaceHit? Contact(PaintMesh mesh,SurfaceProjection p,float reach,HashSet<int>? panels)
    {
        var hit=mesh.Raycast(p.Center+p.Normal*(reach+.01f),-p.Normal);
        if(hit is not {} body||Math.Abs(Vector3.Dot(body.Position-p.Center,p.Normal))>reach+.001f||(panels is not null&&!panels.Contains(body.Triangle)))return null;
        // Raycast orients its hit normal toward the ray. The compositor uses
        // authored vertex normals, so interpolate those for the facing test.
        var a=mesh.Vertices[body.Triangle*3];var b=mesh.Vertices[body.Triangle*3+1];var c=mesh.Vertices[body.Triangle*3+2];
        var e0=b.Position-a.Position;var e1=c.Position-a.Position;var at=body.Position-a.Position;
        var aa=Vector3.Dot(e0,e0);var ab=Vector3.Dot(e0,e1);var bb=Vector3.Dot(e1,e1);var denominator=aa*bb-ab*ab;
        if(denominator<=1e-12f)return null;
        var u=(bb*Vector3.Dot(at,e0)-ab*Vector3.Dot(at,e1))/denominator;var v=(aa*Vector3.Dot(at,e1)-ab*Vector3.Dot(at,e0))/denominator;
        var normal=a.Normal*(1-u-v)+b.Normal*u+c.Normal*v;
        return normal.LengthSquared()>1e-12f&&Vector3.Dot(Vector3.Normalize(normal),p.Normal)>p.MinimumFacing?body:null;
    }
}
