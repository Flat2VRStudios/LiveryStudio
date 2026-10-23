using System.Numerics;
using System.Runtime.Versioning;

namespace LiveryStudio.Core;

public enum StarterKind { Circuit,Rally,Retro,Carbon,Neon,TwoTone }
public sealed record StarterStyle(StarterKind Kind,string Name,string Description,string Base,string Accent,string Ink,string Finish);
public sealed record DesignStarterOptions
{
    public StarterKind Kind {get;set;}=StarterKind.Circuit;
    public string BaseColor {get;set;}="#153E75";
    public string AccentColor {get;set;}="#E63946";
    public string InkColor {get;set;}="#FFF3D6";
    public string Number {get;set;}="27";
    public string Team {get;set;}="TEAM FURY";
    public string Name {get;set;}="";
    public bool IncludeNumber {get;set;}=true;
    public void Validate()
    {
        if(!Enum.IsDefined(Kind))throw new ArgumentException("Choose an available design starter.");
        _=ColorLibrary.Normalize(BaseColor);_=ColorLibrary.Normalize(AccentColor);_=ColorLibrary.Normalize(InkColor);
        if(IncludeNumber&&(Number.Length is <1 or >3||Number.Any(c=>c<'0'||c>'9')))throw new ArgumentException("Use a racing number of one to three digits.");
        if(Team.Length>24||Team.Any(char.IsControl))throw new ArgumentException("Use a team name of up to 24 visible characters.");
        if(Name.Length>100||Name.Any(char.IsControl))throw new ArgumentException("Use a design name of up to 100 visible characters.");
    }
}
public sealed record StarterLayout(List<PaintLayer> Layers,IReadOnlyList<string> Notes,Vector3 Minimum,Vector3 Maximum);
public sealed record StarterDesign(LiveryProject Project,StarterLayout Layout);

public static class DesignStarters
{
    public static readonly StarterStyle[] Styles=[
        new(StarterKind.Circuit,"Circuit stripes","Long racing stripes and paired number panels.","#153E75","#E63946","#FFF3D6","Gloss"),
        new(StarterKind.Rally,"Rally checker","A checker accent with bold side bands and numbers.","#FFF3D6","#D83232","#18202B","Satin"),
        new(StarterKind.Retro,"Retro sunset","Warm stripes and a flowing side gradient.","#B94B22","#F4B64E","#FFF1D4","Gloss"),
        new(StarterKind.Carbon,"Carbon sprint","Woven roof panels with bright accent lines.","#171A20","#FFB511","#FFFFFF","Metallic"),
        new(StarterKind.Neon,"Neon pulse","Colorful side gradients on a deep metallic base.","#141126","#ED39CE","#4CC9F0","Candy"),
        new(StarterKind.TwoTone,"Two-tone racer","Contrasting edge and face paint with racing accents.","#133C58","#D99642","#F5F3ED","Pearl / two-tone")
    ];
    public static DesignStarterOptions Defaults(StarterKind kind)
    {
        var style=Styles.Single(s=>s.Kind==kind);return new(){Kind=kind,BaseColor=style.Base,AccentColor=style.Accent,InkColor=style.Ink};
    }
    public static LiveryProject Paint(LiveryProject source,DesignStarterOptions options)
    {
        options.Validate();var style=Styles.Single(s=>s.Kind==options.Kind);
        return FinishPreset.All.Single(f=>f.Name==style.Finish).Apply(source with {Name=string.IsNullOrWhiteSpace(options.Name)?style.Name+(options.IncludeNumber?" "+options.Number:""):options.Name.Trim(),OuterColor=options.Kind==StarterKind.TwoTone?ColorLibrary.Normalize(options.AccentColor):ColorLibrary.Normalize(options.BaseColor),InnerColor=ColorLibrary.Normalize(options.BaseColor),UseSrgbPaintColors=true,OverrideArtworkFinish=true,ArtworkGloss=.35,ArtworkMetalness=0,EditorFile="",Banner="",CameraBookmarks=[],Description="Created with Livery Studio's "+style.Name+" starter. Every stripe, number and team decal remains editable."});
    }
    public static StarterLayout Layout(PaintMesh mesh,DesignStarterOptions options)
    {
        options.Validate();var body=mesh.Vertices.Where(v=>v.Kind==1).Select(v=>v.Position).ToArray();if(body.Length<3)throw new InvalidDataException("This model has no paintable body.");
        var min=body.Aggregate(Vector3.Min);var max=body.Aggregate(Vector3.Max);var span=max-min;var center=(min+max)*.5f;
        if(span.X<.2f||span.Y<.1f||span.Z<.5f)throw new InvalidDataException("This body is too small for a design starter.");
        var layers=new List<PaintLayer>();var notes=new List<string>();var style=Styles.Single(s=>s.Kind==options.Kind);
        SurfaceHit? Find(bool top)
        {
            // Raycast the complete car so glass and wheels continue to occlude
            // the body. Search nearby panels when the center is a window/gap.
            foreach(var z in new[]{0f,.08f,-.08f,.16f,-.16f,.28f,-.28f})
            foreach(var f in top?new[]{.5f,.42f,.58f}:new[]{.46f,.4f,.52f,.34f,.58f})
            {
                var origin=top?new Vector3(center.X+(f-.5f)*span.X,max.Y+span.Length(),center.Z+z*span.Z):new Vector3(max.X+span.Length(),min.Y+f*span.Y,center.Z+z*span.Z);
                var direction=top?-Vector3.UnitY:-Vector3.UnitX;var hit=mesh.Raycast(origin,direction);
                if(hit is {} p&&Vector3.Dot(p.Normal,-direction)>.55f)return p;
            }
            return null;
        }
        void Pattern(string name,SurfaceProjection projection,PatternArtworkOptions pattern,bool mirror=false,Guid? group=null)
        {
            layers.Add(new(){Name=name,Kind=LayerKind.Sticker,PatternArtwork=pattern,Mirror=mirror,GroupId=group,GroupName=group.HasValue?"Number badge":"",WrapAngle=70,WrapDepth=1,Samples=[projection]});
        }
        PatternArtworkOptions Solid(string hex)=>new(){ColorA=hex,ColorB=hex,Resolution=512,Aspect=1};
        if(Find(true) is {} roof)
        {
            var width=span.X*(options.Kind==StarterKind.Carbon?.65f:.4f);var height=span.Z*.92f;
            var projection=SurfaceProjection.At(roof,Vector3.UnitX,width,width/height,0,Vector3.UnitY) with {Center=new(center.X,roof.Position.Y,center.Z)};
            var pattern=options.Kind switch
            {
                StarterKind.Carbon=>new PatternArtworkOptions{Kind=PatternKind.CarbonWeave,ColorA=Shade(options.BaseColor,.65f),ColorB=Shade(options.BaseColor,1.5f),Repeats=32,Aspect=.5f,Resolution=1024},
                StarterKind.Rally=>new PatternArtworkOptions{Kind=PatternKind.Checker,ColorA=options.AccentColor,ColorB=options.InkColor,AlphaB=0,Repeats=16,Aspect=.5f,Resolution=1024},
                _=>new PatternArtworkOptions{Kind=PatternKind.Stripes,ColorA=options.InkColor,ColorB=options.InkColor,AlphaB=0,Fill=.32f,Phase=.16f,Repeats=4,Aspect=.5f,Resolution=1024}
            };
            Pattern(options.Kind==StarterKind.Carbon?"Carbon top panels":"Long top accents",projection,pattern);
            if(MeshEditing.SharedUvCount(mesh,roof)>0)notes.Add("Some top panels share texture areas; their artwork may repeat.");
        }
        else notes.Add("No clear top panel was found. Side artwork is still available.");
        if(Find(false) is {} side)
        {
            var width=span.Z*.88f;var height=span.Y*(options.Kind is StarterKind.Retro or StarterKind.Neon?.28f:.1f);
            var band=SurfaceProjection.At(side,-Vector3.UnitZ,width,width/height,0,Vector3.UnitX) with {Center=new(side.Position.X,side.Position.Y,center.Z)};
            var pattern=options.Kind is StarterKind.Retro or StarterKind.Neon?new PatternArtworkOptions{Kind=PatternKind.LinearGradient,ColorA=options.AccentColor,ColorB=options.InkColor,Aspect=4,Resolution=1024}:Solid(options.AccentColor);
            Pattern("Paired side accents",band,pattern,true);
            if(options.IncludeNumber)
            {
                var group=Guid.NewGuid();var badgeWidth=Math.Min(span.Z*.2f,span.Y*.65f);var badgeHeight=badgeWidth*.78f;
                var panel=SurfaceProjection.At(side,-Vector3.UnitZ,badgeWidth,badgeWidth/badgeHeight,0,Vector3.UnitX);
                Pattern("Number panel",panel,Solid(options.InkColor),true,group);
                var textColor=ContrastInk(options.InkColor,options.AccentColor);var number=new TextArtworkOptions{Text=options.Number,FontName="Segoe UI",Color=textColor,Bold=true};
                layers.Add(new(){Name="Race number "+options.Number,Kind=LayerKind.Sticker,TextArtwork=number,Mirror=true,GroupId=group,GroupName="Number badge",WrapAngle=65,WrapDepth=.5f,Samples=[panel with {Center=panel.Center+Vector3.UnitY*badgeHeight*.07f,Width=badgeWidth*.62f,Height=badgeHeight*.56f}]});
                if(!string.IsNullOrWhiteSpace(options.Team))layers.Add(new(){Name="Team · "+options.Team.Trim(),Kind=LayerKind.Sticker,TextArtwork=new(){Text=options.Team.Trim(),FontName="Segoe UI",Color=textColor,Bold=true},Mirror=true,GroupId=group,GroupName="Number badge",WrapAngle=65,WrapDepth=.5f,Samples=[panel with {Center=panel.Center-Vector3.UnitY*badgeHeight*.3f,Width=badgeWidth*.86f,Height=badgeHeight*.14f}]});
            }
            if(MeshEditing.SharedUvCount(mesh,side)>0)notes.Add("This car shares side texture areas. Linked panels also show the same artwork.");
        }
        else notes.Add("No clear side panel was found. Add a number manually to a visible body panel.");
        if(layers.Count==0)throw new InvalidDataException("No visible paintable panels were found for this starter.");
        foreach(var layer in layers)layer.Validate();return new(layers,notes,min,max);
    }
    private static string Shade(string hex,float scale)=>DesignColor.Hex(PaintFinish.Color(hex)*scale);
    private static string ContrastInk(string panel,string preferred)
    {
        static float Luma(string c){var rgb=PaintFinish.ToLinear(PaintFinish.Color(c));return Vector3.Dot(rgb,new(.2126f,.7152f,.0722f));}
        var first=Luma(panel);var second=Luma(preferred);if((Math.Max(first,second)+.05f)/(Math.Min(first,second)+.05f)>=4.5f)return ColorLibrary.Normalize(preferred);return first>.179f?"#111111":"#FFFFFF";
    }
    [SupportedOSPlatform("windows")]
    public static StarterDesign Create(LiveryProject templates,PaintMesh mesh,DesignStarterOptions options,string workspace,CancellationToken cancellation=default)
    {
        var project=Paint(templates,options);project.Validate();var layout=Layout(mesh,options);cancellation.ThrowIfCancellationRequested();
        var folder=Path.Combine(workspace,"starters",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        // Workshop templates include the stock livery. A starter needs a clean
        // alpha-black paint mask; keep the original surface map and normal/dirt
        // resources, which are independent of this new color foundation.
        var source=FinishMaps.Read(project.Albedo);var foundation=Path.Combine(folder,"clean-body.tga");FinishMaps.WriteTga(new(source.Width,source.Height,new byte[source.Pixels.Length]),foundation);cancellation.ThrowIfCancellationRequested();
        var document=LayerDocument.Create(project with {Albedo=foundation},folder);
        foreach(var layer in layout.Layers)
        {
            cancellation.ThrowIfCancellationRequested();layer.Asset=layer.PatternArtwork is {} pattern?PatternArtwork.Generate(pattern,folder,cancellation):TextArtwork.Generate(layer.TextArtwork!,folder);
            if(layer.TextArtwork is not null){var image=FinishMaps.Read(layer.Asset);var aspect=(float)image.Width/image.Height;layer.Samples=layer.Samples.Select(p=>{var width=Math.Min(p.Width,p.Height*aspect);return p with {Width=width,Height=width/aspect};}).ToList();}
        }
        document.Commit("Created "+Styles.Single(s=>s.Kind==options.Kind).Name,layers=>layers.AddRange(layout.Layers));var maps=document.Compose(mesh,project,cancellation);cancellation.ThrowIfCancellationRequested();
        var color=Path.Combine(folder,"albedo.tga");var surface=Path.Combine(folder,"surface.tga");var editor=Path.Combine(folder,"editor.json");FinishMaps.WriteTga(maps.Albedo,color);FinishMaps.WriteTga(maps.Structure,surface);document.Save(editor);
        project=project with {Albedo=color,Structure=surface,EditorFile=editor};ProjectBundle.Save(project,Path.Combine(folder,"Starter.flproject"));return new(project,layout);
    }
}
