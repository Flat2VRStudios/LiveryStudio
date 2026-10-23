using System.Runtime.Versioning;
using System.Text.Json;

namespace LiveryStudio.Core;

// Prepare changes on an independent document. The studio can preview, keep or
// discard the result without changing the active history or its autosave.
public static class SimpleDesigns
{
    public static LayerDocument Copy(LayerDocument source)=>JsonSerializer.Deserialize<LayerDocument>(JsonSerializer.Serialize(source,LayerDocument.Json),LayerDocument.Json)!;
    private static SharedDesignColor Role(string name,string hex)=>new(Guid.NewGuid(),name,ColorLibrary.Normalize(hex));
    private static string Contrast(string hex)
    {
        var rgb=PaintFinish.ToLinear(PaintFinish.Color(hex));return System.Numerics.Vector3.Dot(rgb,new(.2126f,.7152f,.0722f))>.179f?"#18202B":"#FFF3ED";
    }
    [SupportedOSPlatform("windows")]
    public static DesignColorEdit Look(LayerDocument source,LiveryProject project,PaintMesh mesh,DesignStarterOptions options,string workspace,CancellationToken cancellation=default)
    {
        options.Validate();if(source.Layers.Any(l=>l.Locked))throw new InvalidOperationException("Unlock your artwork before replacing this look. Colors and individual stickers can still be changed.");
        var document=Copy(source);DesignPalette.EnsureProjectHistory(document,project);var paint=DesignStarters.Paint(project,options) with {Name=project.Name,Description=project.Description,Albedo=project.Albedo,Structure=project.Structure,EditorFile=project.EditorFile,Banner=project.Banner,CameraBookmarks=project.CameraBookmarks};
        var layout=DesignStarters.Layout(mesh,options);var roles=new[]{Role("Body paint",options.BaseColor),Role("Accent",options.AccentColor),Role("Details",options.InkColor),Role("Automatic ink",Contrast(options.InkColor))};
        foreach(var layer in layout.Layers)
        {
            cancellation.ThrowIfCancellationRequested();
            layer.ExteriorOnly=true;
            if(layer.TextArtwork is {} text){layer.TextArtwork=text with {Color=roles[3].Hex};layer.ColorLinks["text.fill"]=roles[3];}
            if(layer.PatternArtwork is {} pattern)
            {
                var a=roles.Take(3).FirstOrDefault(r=>r.Hex==pattern.ColorA);var b=roles.Take(3).FirstOrDefault(r=>r.Hex==pattern.ColorB);
                if(a is not null)layer.ColorLinks["pattern.a"]=a;if(b is not null)layer.ColorLinks["pattern.b"]=b;
            }
            Generate(layer,workspace,cancellation);
            if(layer.TextArtwork is not null){var image=FinishMaps.Read(layer.Asset);var aspect=(float)image.Width/image.Height;layer.Samples=layer.Samples.Select(p=>{var w=Math.Min(p.Width,p.Height*aspect);return p with {Width=w,Height=w/aspect};}).ToList();}
        }
        document.Commit("Applied "+DesignStarters.Styles.Single(s=>s.Kind==options.Kind).Name,layers=>{layers.Clear();layers.AddRange(layout.Layers);});
        var state=document.History[document.Cursor];document.History[document.Cursor]=state with {Palette=roles.ToList(),PaintColors=PaintColorState.From(paint) with {EdgeLink=options.Kind==StarterKind.TwoTone?roles[1]:roles[0],FaceLink=roles[0]},Finish=PaintFinishState.From(paint),CleanPaint=true};document.Version=9;document.Validate();
        return new(document,DesignPalette.ResolvePaint(document,paint),layout.Layers.Count,0);
    }
    [SupportedOSPlatform("windows")]
    private static void Generate(PaintLayer layer,string workspace,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if(layer.PatternArtwork is {} pattern)layer.Asset=PatternArtwork.Generate(pattern,workspace,cancellation);
        else if(layer.TextArtwork is {} text)
        {
            layer.Asset=TextArtwork.Generate(text,workspace);
            // Number/name recipes retain their intended box when palette colors change.
        }
        else if(layer.VectorArtwork is {} vector)layer.Asset=VectorArtwork.Rebuild(vector,workspace);
    }
    [SupportedOSPlatform("windows")]
    public static DesignColorEdit Palette(LayerDocument source,LiveryProject project,IReadOnlyList<string> colors,string workspace,CancellationToken cancellation=default)
    {
        if(colors.Count<3)throw new ArgumentException("A palette needs body, accent and detail colors.");foreach(var hex in colors)_=ColorLibrary.Normalize(hex);
        var document=Copy(source);DesignPalette.EnsureProjectHistory(document,project);var current=document.History[document.Cursor];
        var labels=new[]{"Body paint","Accent","Details","Automatic ink"};var desired=new[]{colors[0],colors[1],colors[2],Contrast(colors[2])};
        var changes=current.Palette.Where(r=>labels.Contains(r.Name)).ToDictionary(r=>r.Id,r=>r with {Hex=ColorLibrary.Normalize(desired[Array.IndexOf(labels,r.Name)])});
        var body=current.Palette.FirstOrDefault(r=>r.Name==labels[0])??Role(labels[0],colors[0]);body=body with {Hex=ColorLibrary.Normalize(colors[0])};changes[body.Id]=body;
        var layers=JsonSerializer.Deserialize<List<PaintLayer>>(JsonSerializer.Serialize(document.Layers,LayerDocument.Json),LayerDocument.Json)!;var count=0;
        foreach(var layer in layers)
        {
            var edited=false;
            foreach(var (field,old) in layer.ColorLinks.ToArray())
            {
                if(!changes.TryGetValue(old.Id,out var role)||old.Hex==role.Hex)continue;if(layer.Locked)throw new InvalidOperationException("Unlock '"+layer.Name+"' before changing this palette.");
                switch(field){case "paint":layer.Color=role.Hex;break;case "text.fill":layer.TextArtwork=layer.TextArtwork! with {Color=role.Hex};break;case "text.outline":layer.TextArtwork=layer.TextArtwork! with {OutlineColor=role.Hex};break;case "pattern.a":layer.PatternArtwork=layer.PatternArtwork! with {ColorA=role.Hex};break;case "pattern.b":layer.PatternArtwork=layer.PatternArtwork! with {ColorB=role.Hex};break;default:continue;}
                layer.ColorLinks[field]=role;edited=true;
            }
            if(edited){Generate(layer,workspace,cancellation);count++;}
        }
        var before=current.PaintColors!;var next=before with {OuterColor=body.Hex,InnerColor=body.Hex,UseSrgbPaintColors=true,EdgeLink=body,FaceLink=body};
        if(before.EdgeLink is {} edge&&changes.TryGetValue(edge.Id,out var edgeRole))next=next with {OuterColor=edgeRole.Hex,EdgeLink=edgeRole};
        document.Commit("Changed design palette",target=>{target.Clear();target.AddRange(layers);});var state=document.History[document.Cursor];
        document.History[document.Cursor]=state with {Palette=current.Palette.Where(r=>!changes.ContainsKey(r.Id)).Concat(changes.Values).ToList(),PaintColors=next,CleanPaint=true};document.Version=9;document.Validate();
        return new(document,DesignPalette.ResolvePaint(document,project),count,changes.Count);
    }
    public static DesignColorEdit Body(LayerDocument source,LiveryProject project,string hex,bool clean=true)
    {
        hex=ColorLibrary.Normalize(hex);var document=Copy(source);DesignPalette.EnsureProjectHistory(document,project);document.Commit("Changed body paint",_=>{});var state=document.History[document.Cursor];
        document.History[document.Cursor]=state with {PaintColors=new(hex,hex,true),CleanPaint=clean};document.Version=9;return new(document,DesignPalette.ResolvePaint(document,project),0,0);
    }
    [SupportedOSPlatform("windows")]
    public static DesignColorEdit Number(LayerDocument source,LiveryProject project,PaintMesh mesh,string number,string name,string workspace,CancellationToken cancellation=default)
    {
        var options=DesignStarters.Defaults(StarterKind.Circuit) with {Number=number,Team=name,InkColor="#FFF3ED",IncludeNumber=true};options.Validate();
        var document=Copy(source);DesignPalette.EnsureProjectHistory(document,project);
        var existing=document.Layers.LastOrDefault(l=>l.GroupName=="Number badge"&&l.Name.StartsWith("Race number "));
        if(existing is not null)
        {
            var group=document.Layers.Where(l=>l.GroupId==existing.GroupId).ToList();if(group.Any(l=>l.Locked))throw new InvalidOperationException("Unlock the number badge before changing its text.");
            var replacement=existing with {Name="Race number "+number,TextArtwork=existing.TextArtwork! with {Text=number}};Generate(replacement,workspace,cancellation);
            var team=group.FirstOrDefault(l=>l.TextArtwork is not null&&l.Id!=existing.Id);PaintLayer? changedTeam=null;
            if(!string.IsNullOrWhiteSpace(name))
            {
                var box=existing.Samples[0];changedTeam=team is not null?team with {Name="Team · "+name,TextArtwork=team.TextArtwork! with {Text=name}}:existing with {Id=Guid.NewGuid(),Name="Team · "+name,TextArtwork=existing.TextArtwork! with {Text=name},Samples=[box with {Center=box.Center-box.Up*box.Height*.65f,Width=box.Width*1.3f,Height=box.Height*.3f}]};Generate(changedTeam,workspace,cancellation);
            }
            document.Commit("Changed race number to "+number,target=>{var index=target.FindIndex(l=>l.Id==existing.Id);target[index]=replacement;if(team is not null)target.RemoveAll(l=>l.Id==team.Id);if(changedTeam is not null)target.Add(changedTeam);});
            return new(document,DesignPalette.ResolvePaint(document,project),1+(team is not null||changedTeam is not null?1:0),0);
        }
        var layers=DesignStarters.Layout(mesh,options).Layers.Where(l=>l.GroupId.HasValue).ToList();
        if(layers.Count==0)throw new InvalidOperationException("Choose a visible side panel to place your number on this car.");
        foreach(var layer in layers)
        {
            layer.WrapDepth=SimplePlacement.Depth;layer.WrapAngle=SimplePlacement.Angle;Generate(layer,workspace,cancellation);
            if(layer.TextArtwork is not null){var image=FinishMaps.Read(layer.Asset);var aspect=(float)image.Width/image.Height;layer.Samples=layer.Samples.Select(p=>{var w=Math.Min(p.Width,p.Height*aspect);return p with {Width=w,Height=w/aspect};}).ToList();}
        }
        document.Commit("Added number "+number+" to both doors",target=>target.AddRange(layers));document.Version=9;return new(document,DesignPalette.ResolvePaint(document,project),layers.Count,0);
    }
}
