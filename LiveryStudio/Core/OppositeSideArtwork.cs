using System.Numerics;
using System.Text.Json;

namespace LiveryStudio.Core;

public enum OppositeSideMode { Readable, Mirrored, SeparatePaired }
public sealed class OppositeSidePlan
{
    private readonly LayerDocument source;
    private readonly int cursor;
    private readonly EditCheckpoint checkpoint;
    public List<PaintLayer> Layers {get;}
    public Guid[] CopyIds {get;}
    public string Label {get;}
    internal OppositeSidePlan(LayerDocument document,List<PaintLayer> layers,Guid[] ids,string label)
    {source=document;cursor=document.Cursor;checkpoint=document.History[cursor];Layers=JsonSerializer.Deserialize<List<PaintLayer>>(JsonSerializer.Serialize(layers,LayerDocument.Json),LayerDocument.Json)!;CopyIds=ids;Label=label;}
    public void Apply(LayerDocument document)
    {
        if(!ReferenceEquals(source,document)||document.Cursor!=cursor||!ReferenceEquals(document.History[cursor],checkpoint))throw new InvalidOperationException("The design changed during the other-side preview. Try again.");
        var independent=JsonSerializer.Deserialize<List<PaintLayer>>(JsonSerializer.Serialize(Layers,LayerDocument.Json),LayerDocument.Json)!;
        document.Commit(Label,layers=>{layers.Clear();layers.AddRange(independent);});
    }
}

public static class OppositeSideArtwork
{
    private static Vector3 Reflect(Vector3 p)=>new(-p.X,p.Y,p.Z);
    private static PaintLayer Clone(PaintLayer layer)=>JsonSerializer.Deserialize<PaintLayer>(JsonSerializer.Serialize(layer,LayerDocument.Json),LayerDocument.Json)!;

    public static OppositeSidePlan Create(LayerDocument document,PaintMesh mesh,IEnumerable<Guid> selection,OppositeSideMode mode,bool keepPanels=true,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();
        if(!Enum.IsDefined(mode))throw new ArgumentOutOfRangeException(nameof(mode));
        var ids=selection.ToHashSet();var selected=document.Layers.Where(l=>ids.Contains(l.Id)).ToArray();
        if(ids.Count==0||selected.Length!=ids.Count)throw new InvalidOperationException("Select artwork that is still in this design.");
        foreach(var layer in selected)layer.Validate();
        var outputCount=document.Layers.Count+(mode==OppositeSideMode.SeparatePaired?selected.Where(l=>l.Mirror).Sum(l=>2L*l.Samples.Count-1):selected.Length);
        if(outputCount>512)throw new InvalidOperationException($"This operation would create {outputCount} layers. The project limit is 512; select fewer layers or shorter strokes.");
        var result=document.Layers.ToList();var copies=new List<Guid>();
        var groups=new Dictionary<Guid,Guid>();
        Guid Group(Guid old){if(!groups.TryGetValue(old,out var id))groups[old]=id=Guid.NewGuid();return id;}
        PaintLayer Copy(PaintLayer source)
        {
            var copy=Clone(source);copy.Id=Guid.NewGuid();copy.Locked=false;copy.Mirror=false;
            copy.Name=source.Name[..Math.Min(87,source.Name.Length)]+" · other side";
            if(source.GroupId is {} group)copy.GroupId=Group(group);
            copies.Add(copy.Id);return copy;
        }
        if(mode==OppositeSideMode.SeparatePaired)
        {
            var paired=selected.Where(l=>l.Mirror).ToArray();
            if(paired.Length==0)throw new InvalidOperationException("This selection has no paired layers to separate.");
            LayerSets.RequireEditable(paired);
            // Keep the exact original/reflected stamp order. A translucent stroke
            // can overlap itself near the centerline; moving every reflected stamp
            // to one later layer would change its color and material blending.
            result=[];
            foreach(var layer in document.Layers)
            {
                cancellation.ThrowIfCancellationRequested();
                if(!ids.Contains(layer.Id)||!layer.Mirror){result.Add(layer);continue;}
                var canonical=LayerSets.Canonical(Clone(layer));
                var template=canonical with {Samples=[]};
                var originalGroup=layer.GroupId??(layer.Samples.Count>1?Guid.NewGuid():(Guid?)null);
                var copyGroup=layer.GroupId is {} existing?Group(existing):layer.Samples.Count>1?Guid.NewGuid():(Guid?)null;
                for(var sample=0;sample<canonical.Samples.Count;sample++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var original=Clone(template);original.Id=sample==0?layer.Id:Guid.NewGuid();original.Mirror=false;
                    original.Samples=[canonical.Samples[sample]];original.GroupId=originalGroup;
                    var groupName=string.IsNullOrWhiteSpace(layer.Name)?"Artwork":layer.Name;
                    if(layer.GroupId is null&&originalGroup.HasValue)original.GroupName=groupName[..Math.Min(80,groupName.Length)];
                    var copy=Copy(original);copy.Samples=[PaintLayer.Reflected(original.Samples[0])];copy.GroupId=copyGroup;
                    if(layer.GroupId is null&&copyGroup.HasValue)copy.GroupName=(groupName[..Math.Min(67,groupName.Length)]+" · other side");
                    result.Add(original);result.Add(copy);
                }
            }
        }
        else
        {
            if(selected.Any(l=>l.Mirror))throw new InvalidOperationException("This selection already has paired sides. Separate them to edit each side independently.");
            var source=LayerSets.Frame(selected);var target=PaintLayer.Reflected(source);
            Vector3 ToSourceAxis(Vector3 v)=>source.Right*Vector3.Dot(v,target.Right)+source.Up*Vector3.Dot(v,target.Up)+source.Normal*Vector3.Dot(v,target.Normal);
            // A readable group uses one proper frame transform, so separate text
            // characters and their background keep their reading order together.
            var mapped=mode==OppositeSideMode.Readable?LayerSets.Transform(selected.Select(l=>l with {Locked=false}).ToArray(),source,target):
                selected.Select(l=>
                {
                    var copy=LayerSets.Canonical(l);copy.Samples=copy.Samples.Select(PaintLayer.Reflected).ToList();
                    // Express image reflection through the existing flip control,
                    // retaining a proper outward frame for later rotation/dragging.
                    copy.FlipHorizontal=!copy.FlipHorizontal;return copy;
                }).ToList();
            var masks=new List<(int[] Source,int[] Target)>();
            foreach(var layer in mapped)
            {
                cancellation.ThrowIfCancellationRequested();
                var copy=Copy(layer);
                if(layer.Triangles.Length>0)
                {
                    if(!keepPanels)copy.Triangles=[];
                    else
                    {
                        var existing=masks.FirstOrDefault(item=>item.Source.SequenceEqual(layer.Triangles));
                        if(existing.Target is null)
                        {
                            // Readable groups can move components front-to-back
                            // inside their frame. Their panel masks must undergo
                            // that same transform, rather than just X reflection.
                            var mask=mode==OppositeSideMode.Readable?MapPanels(mesh,layer.Triangles,p=>source.Center+ToSourceAxis(p-target.Center),ToSourceAxis,cancellation):ReflectPanels(mesh,layer.Triangles,cancellation);
                            existing=(layer.Triangles,mask);masks.Add(existing);
                        }
                        copy.Triangles=existing.Target.ToArray();
                    }
                }
                result.Add(copy);
            }
        }
        if(result.Count>512)throw new InvalidOperationException($"This operation would create {result.Count} layers. The project limit is 512; select fewer layers or shorter strokes.");
        foreach(var layer in result)layer.Validate();
        return new(document,result,copies.ToArray(),mode==OppositeSideMode.SeparatePaired?"Separated paired sides":mode==OppositeSideMode.Readable?"Copied readable artwork to other side":"Copied mirrored design to other side");
    }

    public static int[] ReflectPanels(PaintMesh mesh,int[] mask,CancellationToken cancellation=default)
        =>MapPanels(mesh,mask,Reflect,Reflect,cancellation);

    private static int[] MapPanels(PaintMesh mesh,int[] mask,Func<Vector3,Vector3> pointToSource,Func<Vector3,Vector3> normalToSource,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if(mask.Length==0)return [];
        var vertices=mesh.Vertices;var count=vertices.Length/3;
        if(mask.Any(t=>t<0||t>=count))throw new InvalidDataException("The panel selection belongs to different car geometry.");
        var source=mask.ToHashSet();var found=new List<int>();var tolerance=Math.Clamp(mesh.Radius*.02f,.005f,.1f);
        // Ray-test destination centroids against the source panel. This also
        // handles opposite sides triangulated differently and slight asymmetry.
        // Windows, wheels and other non-paintable occluders remain occluders.
        for(var t=0;t<count;t++)
        {
            cancellation.ThrowIfCancellationRequested();
            var a=vertices[t*3];if(a.Kind!=1)continue;var b=vertices[t*3+1];var c=vertices[t*3+2];
            var point=pointToSource((a.Position+b.Position+c.Position)/3);var normal=Vector3.Normalize(normalToSource(Vector3.Normalize(a.Normal+b.Normal+c.Normal)));
            var hit=mesh.Raycast(point+normal*(tolerance+.001f),-normal);
            if(hit is {} p&&source.Contains(p.Triangle)&&Vector3.DistanceSquared(p.Position,point)<=tolerance*tolerance&&Vector3.Dot(p.Normal,normal)>.6f)found.Add(t);
        }
        if(found.Count==0)throw new InvalidOperationException("No matching opposite body panel was found. Turn off ‘Keep panel restrictions’ to preview nearby body panels.");
        return found.ToArray();
    }
}
