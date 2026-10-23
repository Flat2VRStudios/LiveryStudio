using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.Versioning;

namespace LiveryStudio.Core;

public sealed record SharedDesignColor(Guid Id,string Name,string Hex)
{
    public void Validate(){if(Id==Guid.Empty||string.IsNullOrWhiteSpace(Name)||Name.Length>48||Name.Any(char.IsControl)||Hex!=ColorLibrary.Normalize(Hex))throw new InvalidDataException("Shared colors need a name, ID and uppercase hex color.");}
}
public sealed record PaintColorState(string OuterColor,string InnerColor,bool UseSrgbPaintColors)
{
    public SharedDesignColor? EdgeLink {get;set;}
    public SharedDesignColor? FaceLink {get;set;}
    public static PaintColorState From(LiveryProject p)=>new(p.OuterColor,p.InnerColor,p.UseSrgbPaintColors);
    public LiveryProject Apply(LiveryProject p)=>p with {OuterColor=OuterColor,InnerColor=InnerColor,UseSrgbPaintColors=UseSrgbPaintColors};
    public void Validate(){_=PaintFinish.Color(OuterColor);_=PaintFinish.Color(InnerColor);EdgeLink?.Validate();FaceLink?.Validate();}
}
public sealed record DesignColorUse(Guid? LayerId,string Field,string Hex,SharedDesignColor? Link,string Label,bool Locked);
public sealed record DesignColorEdit(LayerDocument Document,LiveryProject Project,int ChangedLayers,int LinkedFields);

public static class DesignPalette
{
    public static LiveryProject ResolvePaint(LayerDocument document,LiveryProject project){var state=document.History[document.Cursor];return state.PaintColors?.Apply(state.Finish?.Apply(project)??project)??state.Finish?.Apply(project)??project;}
    public static void EnsureProjectHistory(LayerDocument document,LiveryProject project)
    {
        for(var i=0;i<document.History.Count;i++){var state=document.History[i];document.History[i]=state with {PaintColors=state.PaintColors??PaintColorState.From(project),Finish=state.Finish??PaintFinishState.From(project)};}document.Version=Math.Max(document.Version,9);
    }
    public static IEnumerable<SharedDesignColor> LinkedColors(IEnumerable<PaintLayer> layers)=>layers.SelectMany(l=>l.ColorLinks.Values).GroupBy(c=>c.Id).Select(g=>g.First());
    public static bool FieldAllowed(PaintLayer layer,string key)=>key switch
    {
        "paint"=>layer.Kind==LayerKind.Color,
        "text.fill" or "text.outline"=>layer.TextArtwork is not null,
        "pattern.a" or "pattern.b"=>layer.PatternArtwork is not null,
        _=>key.StartsWith("svg:",StringComparison.Ordinal)?layer.VectorArtwork is not null&&Regex.IsMatch(key[4..],@"^#[0-9A-F]{6}$"):
            key.StartsWith("image:",StringComparison.Ordinal)&&layer.RasterArtwork is not null&&Regex.IsMatch(key[6..],@"^#[0-9A-F]{6}$")
    };
    public static void ValidateLayers(IReadOnlyList<PaintLayer> layers,IReadOnlyList<SharedDesignColor> palette,PaintColorState? paint)
    {
        if(palette.Count>32||palette.Any(c=>c is null)||palette.Select(c=>c.Id).Distinct().Count()!=palette.Count)throw new InvalidDataException("A design supports 32 unique shared colors.");
        var roles=palette.ToDictionary(c=>c.Id);foreach(var role in palette)role.Validate();paint?.Validate();
        foreach(var role in layers.SelectMany(l=>l.ColorLinks.Values).Concat(new[]{paint?.EdgeLink,paint?.FaceLink}.OfType<SharedDesignColor>()))
            if(!roles.TryGetValue(role.Id,out var definition)||definition!=role)throw new InvalidDataException("Shared color definitions must agree with every linked field.");
    }
    private static IEnumerable<DesignColorUse> LayerFields(PaintLayer layer,Dictionary<string,string[]> cache)
    {
        DesignColorUse Field(string key,string value,string label)=>new(layer.Id,key,ColorLibrary.Normalize(value),layer.ColorLinks.GetValueOrDefault(key),layer.Name+" · "+label,layer.Locked);
        if(layer.Kind==LayerKind.Color)yield return Field("paint",layer.Color,"paint");
        if(layer.TextArtwork is {} text){yield return Field("text.fill",text.Color,"text");yield return Field("text.outline",text.OutlineColor,text.OutlineWidth>0?"outline":"outline (off)");}
        if(layer.PatternArtwork is {} pattern){yield return Field("pattern.a",pattern.ColorA,"first color");yield return Field("pattern.b",pattern.ColorB,"second color");}
        if(layer.VectorArtwork is {} vector)
        {
            var key="svg:"+vector.SourceFile;if(!cache.TryGetValue(key,out var colors))cache[key]=colors=ArtworkColors.SvgPalette(vector.SourceFile);
            foreach(var color in colors)yield return Field("svg:"+color,vector.Recolors.GetValueOrDefault(color,color),"SVG");
        }
        else if(layer.Kind==LayerKind.Sticker&&layer.TextArtwork is null&&layer.PatternArtwork is null)
        {
            var source=layer.RasterArtwork?.SourceFile??layer.Asset;var key="image:"+source+"|"+JsonSerializer.Serialize(layer.RasterArtwork?.Preparation,LayerDocument.Json);if(!cache.TryGetValue(key,out var colors))cache[key]=colors=layer.RasterArtwork is {} raster?ArtworkColors.RasterPalette(raster):ArtworkColors.RasterPalette(source);
            foreach(var color in colors.Concat(layer.ColorLinks.Keys.Where(k=>k.StartsWith("image:")).Select(k=>k[6..])).Distinct())
                yield return Field("image:"+color,layer.RasterArtwork?.Recolors.GetValueOrDefault(color,color)??color,"image pixels");
        }
    }
    public static List<DesignColorUse> Uses(LayerDocument document,LiveryProject project)
    {
        project=ResolvePaint(document,project);var paint=document.History[document.Cursor].PaintColors;var cache=new Dictionary<string,string[]>(StringComparer.OrdinalIgnoreCase);
        var result=new List<DesignColorUse>{new(null,"edge",DesignColor.PaintPickerHex(project,project.OuterColor),paint?.EdgeLink,"Edge paint",false),new(null,"face",DesignColor.PaintPickerHex(project,project.InnerColor),paint?.FaceLink,"Face paint",false)};
        foreach(var layer in document.Layers)result.AddRange(LayerFields(layer,cache));return result;
    }
    private static LayerDocument Copy(LayerDocument source)=>JsonSerializer.Deserialize<LayerDocument>(JsonSerializer.Serialize(source,LayerDocument.Json),LayerDocument.Json)!;
    private static void CapturePaint(LayerDocument document,LiveryProject project)
    {
        var original=PaintColorState.From(project);document.History=document.History.Select(h=>h.PaintColors is null?h with {PaintColors=original with {}}:h).ToList();
    }
    private static void SetField(PaintLayer layer,string key,string hex)
    {
        switch(key)
        {
            case "paint":layer.Color=hex;break;
            case "text.fill":layer.TextArtwork=layer.TextArtwork! with {Color=hex};break;
            case "text.outline":layer.TextArtwork=layer.TextArtwork! with {OutlineColor=hex};break;
            case "pattern.a":layer.PatternArtwork=layer.PatternArtwork! with {ColorA=hex};break;
            case "pattern.b":layer.PatternArtwork=layer.PatternArtwork! with {ColorB=hex};break;
            default:
                if(key.StartsWith("svg:")){layer.VectorArtwork!.Recolors[key[4..]]=hex;break;}
                if(key.StartsWith("image:")){layer.RasterArtwork??=new(layer.Asset);layer.RasterArtwork.Recolors[key[6..]]=hex;break;}
                throw new InvalidDataException("Unknown shared color field.");
        }
    }
    private static string? FieldValue(PaintLayer layer,string key)=>key switch
    {
        "paint"=>layer.Color,"text.fill"=>layer.TextArtwork?.Color,"text.outline"=>layer.TextArtwork?.OutlineColor,
        "pattern.a"=>layer.PatternArtwork?.ColorA,"pattern.b"=>layer.PatternArtwork?.ColorB,
        _=>key.StartsWith("svg:")?layer.VectorArtwork?.Recolors.GetValueOrDefault(key[4..],key[4..]):key.StartsWith("image:")?layer.RasterArtwork?.Recolors.GetValueOrDefault(key[6..],key[6..]):null
    };
    public static void DetachEditedLinks(PaintLayer layer,PaintLayer previous)
    {
        foreach(var (key,role) in layer.ColorLinks.ToArray())
            if(!FieldAllowed(layer,key)||previous.ColorLinks.TryGetValue(key,out var before)&&before==role&&!string.Equals(FieldValue(layer,key),FieldValue(previous,key),StringComparison.OrdinalIgnoreCase))layer.ColorLinks.Remove(key);
    }
    [SupportedOSPlatform("windows")]
    private static void Regenerate(PaintLayer layer,string workspace,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if(layer.TextArtwork is {} text)layer.Asset=TextArtwork.Generate(text,workspace);
        else if(layer.PatternArtwork is {} pattern)layer.Asset=PatternArtwork.Generate(pattern,workspace,cancellation);
        else if(layer.VectorArtwork is {} vector)layer.Asset=VectorArtwork.Rebuild(vector,workspace);
        else if(layer.RasterArtwork is {} image)layer.Asset=ArtworkColors.Generate(image,workspace,cancellation);
        cancellation.ThrowIfCancellationRequested();
    }
    public static DesignColorEdit Link(LayerDocument source,LiveryProject project,string color,string name,IReadOnlySet<Guid>? selected=null)
    {
        color=ColorLibrary.Normalize(color);var role=new SharedDesignColor(Guid.NewGuid(),name.Trim(),color);role.Validate();var document=Copy(source);project=ResolvePaint(document,project);CapturePaint(document,project);
        if(document.History[document.Cursor].Palette.Count>=32)throw new InvalidOperationException("This design already has 32 shared colors.");
        var matches=Uses(document,project).Where(u=>u.Hex==color&&!u.Locked&&(selected is null||u.LayerId is {} id&&selected.Contains(id))).ToArray();
        if(matches.Length==0)throw new InvalidOperationException("No unlocked matching colors are in this selection.");
        document.Commit("Linked color · "+role.Name,layers=>
        {
            foreach(var use in matches.Where(u=>u.LayerId.HasValue))
            {
                var layer=layers.Single(l=>l.Id==use.LayerId);if(use.Field.StartsWith("image:"))layer.RasterArtwork??=new(layer.Asset);layer.ColorLinks[use.Field]=role;
            }
        });
        var active=document.History[document.Cursor];var paint=active.PaintColors!;
        active=active with {Palette=active.Palette.Where(c=>c.Id!=role.Id).Append(role).ToList(),PaintColors=paint with {EdgeLink=matches.Any(u=>u.Field=="edge"&&u.LayerId is null)?role:paint.EdgeLink,FaceLink=matches.Any(u=>u.Field=="face"&&u.LayerId is null)?role:paint.FaceLink}};
        document.History[document.Cursor]=active;document.Version=Math.Max(document.Version,6);document.Validate();return new(document,project,matches.Where(u=>u.LayerId.HasValue).Select(u=>u.LayerId).Distinct().Count(),matches.Length);
    }
    [SupportedOSPlatform("windows")]
    public static DesignColorEdit Change(LayerDocument source,LiveryProject project,Guid roleId,string hex,string workspace,CancellationToken cancellation=default,string? name=null)
    {
        hex=ColorLibrary.Normalize(hex);var document=Copy(source);project=ResolvePaint(document,project);CapturePaint(document,project);var active=document.History[document.Cursor];var original=active.Palette.SingleOrDefault(c=>c.Id==roleId)??throw new InvalidOperationException("Choose a shared design color.");
        name=name?.Trim()??original.Name;if(original.Hex==hex&&original.Name==name)return new(document,project,0,0);
        var changed=original with {Hex=hex,Name=name};changed.Validate();var pigmentChanged=original.Hex!=hex;var affected=document.Layers.Where(l=>l.ColorLinks.Values.Any(c=>c.Id==roleId)).ToArray();
        if(affected.Any(l=>l.Locked))throw new InvalidOperationException("Unlock the layers linked to '"+original.Name+"' before changing it.");
        var replacements=new Dictionary<Guid,PaintLayer>();foreach(var old in affected)
        {
            cancellation.ThrowIfCancellationRequested();var layer=JsonSerializer.Deserialize<PaintLayer>(JsonSerializer.Serialize(old,LayerDocument.Json),LayerDocument.Json)!;
            foreach(var key in layer.ColorLinks.Where(p=>p.Value.Id==roleId).Select(p=>p.Key).ToArray()){if(pigmentChanged)SetField(layer,key,hex);layer.ColorLinks[key]=changed;}
            if(pigmentChanged)Regenerate(layer,workspace,cancellation);replacements[layer.Id]=layer;
        }
        cancellation.ThrowIfCancellationRequested();document.Commit("Changed shared color · "+original.Name,layers=>{for(var i=0;i<layers.Count;i++)if(replacements.TryGetValue(layers[i].Id,out var replacement))layers[i]=replacement;});
        active=document.History[document.Cursor];var paint=active.PaintColors!;
        if(paint.EdgeLink?.Id==roleId)paint=paint with {OuterColor=pigmentChanged?DesignColor.PaintFieldHex(project,hex):paint.OuterColor,EdgeLink=changed};
        if(paint.FaceLink?.Id==roleId)paint=paint with {InnerColor=pigmentChanged?DesignColor.PaintFieldHex(project,hex):paint.InnerColor,FaceLink=changed};
        document.History[document.Cursor]=active with {Palette=active.Palette.Select(c=>c.Id==roleId?changed:c).ToList(),PaintColors=paint};document.Version=Math.Max(document.Version,6);document.Validate();
        return new(document,paint.Apply(project),affected.Length,affected.Sum(l=>l.ColorLinks.Values.Count(c=>c.Id==roleId))+(paint.EdgeLink?.Id==roleId?1:0)+(paint.FaceLink?.Id==roleId?1:0));
    }
    public static void AttachUniqueLinks(PaintLayer layer,IReadOnlyList<SharedDesignColor> palette)
    {
        if(palette.Count==0||layer.Kind==LayerKind.Sticker&&layer.TextArtwork is null&&layer.PatternArtwork is null&&layer.VectorArtwork is null)return;
        foreach(var use in LayerFields(layer,new())){var matches=palette.Where(c=>c.Hex==use.Hex).ToArray();if(matches.Length!=1||use.Field.StartsWith("image:"))continue;layer.ColorLinks[use.Field]=matches[0];}
    }
    public static void SyncManualPaint(LayerDocument document,LiveryProject project)
    {
        if(document.History[document.Cursor].PaintColors is not {} before)return;
        _=PaintFinish.Color(project.OuterColor);_=PaintFinish.Color(project.InnerColor);
        var finish=PaintFinishState.From(project);finish.Validate();var colorsChanged=before.OuterColor!=project.OuterColor||before.InnerColor!=project.InnerColor||before.UseSrgbPaintColors!=project.UseSrgbPaintColors;
        if(!colorsChanged&&(document.History[document.Cursor].Finish is null||document.History[document.Cursor].Finish==finish))return;
        var edge=before.OuterColor==project.OuterColor&&before.UseSrgbPaintColors==project.UseSrgbPaintColors?before.EdgeLink:null;
        var face=before.InnerColor==project.InnerColor&&before.UseSrgbPaintColors==project.UseSrgbPaintColors?before.FaceLink:null;
        document.Commit(colorsChanged?"Changed paint color":"Changed paint finish",_=>{});var state=document.History[document.Cursor];document.History[document.Cursor]=state with {PaintColors=PaintColorState.From(project) with {EdgeLink=edge,FaceLink=face},Finish=state.Finish is null?null:finish};
    }
    public static DesignColorEdit Unlink(LayerDocument source,LiveryProject project,Guid roleId)
    {
        var document=Copy(source);project=ResolvePaint(document,project);var affected=document.Layers.Where(l=>l.ColorLinks.Values.Any(c=>c.Id==roleId)).ToArray();
        if(affected.Any(l=>l.Locked))throw new InvalidOperationException("Unlock the linked layers before unlinking this color.");
        document.Commit("Unlinked shared color",layers=>{foreach(var layer in layers)foreach(var key in layer.ColorLinks.Where(p=>p.Value.Id==roleId).Select(p=>p.Key).ToArray())layer.ColorLinks.Remove(key);});
        var state=document.History[document.Cursor];var paint=state.PaintColors;
        document.History[document.Cursor]=state with {Palette=state.Palette.Where(c=>c.Id!=roleId).ToList(),PaintColors=paint is null?null:paint with {EdgeLink=paint.EdgeLink?.Id==roleId?null:paint.EdgeLink,FaceLink=paint.FaceLink?.Id==roleId?null:paint.FaceLink}};
        document.Validate();return new(document,project,affected.Length,0);
    }
}
