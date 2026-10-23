using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiveryStudio.Core;

public enum LayerKind { Sticker, Color, Gloss, Metalness, Surface }
public sealed record PaintLayer
{
	public Guid Id {get;set;}=Guid.NewGuid();
	public string Name {get;set;}="Artwork";
	public LayerKind Kind {get;set;}
	public string Asset {get;set;}="";
	public string Color {get;set;}="#21DCC1";
	public TextArtworkOptions? TextArtwork {get;set;}
	public bool Visible {get;set;}=true;
	public bool Locked {get;set;}
	public PatternArtworkOptions? PatternArtwork {get;set;}
	public VectorArtworkOptions? VectorArtwork {get;set;}
	public RasterArtworkOptions? RasterArtwork {get;set;}
	public DecalEffectsOptions? Effects {get;set;}
	public Dictionary<string,SharedDesignColor> ColorLinks {get;set;}=[];
	public Guid? GroupId {get;set;}
	public string GroupName {get;set;}="";
	[JsonIgnore] public string GroupLabel=>(Locked?"Locked · ":"")+(GroupId.HasValue?"Group · "+GroupName:"");
	[JsonIgnore] public string BrowserType=>LayerBrowser.Type(this);
	[JsonIgnore] public string BrowserState=>LayerBrowser.State(this);
	public bool Mirror {get;set;}
	public bool ExteriorOnly {get;set;}
	public bool ProtectSharedUvs {get;set;}
	public float Opacity {get;set;}=1;
	public float Scale {get;set;}=1;
	public float ScaleX {get;set;}=1;
	public float ScaleY {get;set;}=1;
	public bool FlipHorizontal {get;set;}
	public bool FlipVertical {get;set;}
	public bool AlignWithCamera {get;set;}
	public float WrapAngle {get;set;}=75.52249f;
	public float WrapDepth {get;set;}=.35f;
	public float Rotation {get;set;}
	public float OffsetRight {get;set;}
	public float OffsetUp {get;set;}
	public float Gloss {get;set;}=.65f;
	public float Metalness {get;set;}
	public float Hardness {get;set;}=.85f;
	public List<SurfaceProjection> Samples {get;set;}=[];
	public List<SurfaceProjection> Erasures {get;set;}=[];
	public int[] Triangles {get;set;}=[];
	[JsonIgnore] public bool IsMaterial=>Kind is LayerKind.Gloss or LayerKind.Metalness or LayerKind.Surface;
	public override string ToString()=>Name;
	public void Validate()
	{
		if(Id==Guid.Empty||Name.Length>100||GroupId==Guid.Empty||GroupName.Length>80||(GroupId.HasValue&&string.IsNullOrWhiteSpace(GroupName))||Samples.Count<1||Samples.Count>20000||Erasures is null||Erasures.Count>20000||Erasures.Count>0&&Kind!=LayerKind.Color||Triangles.Length>2_000_000)throw new InvalidDataException("Invalid editor layer.");
		_ = PaintFinish.Color(Color);
		if(TextArtwork is not null){if(Kind!=LayerKind.Sticker)throw new InvalidDataException("Text artwork belongs to a decal layer.");TextArtwork.Validate();}
		if(PatternArtwork is not null){if(Kind!=LayerKind.Sticker||TextArtwork is not null)throw new InvalidDataException("Pattern artwork belongs to its own decal layer.");PatternArtwork.Validate();}
		if(VectorArtwork is not null){if(Kind!=LayerKind.Sticker||TextArtwork is not null||PatternArtwork is not null)throw new InvalidDataException("SVG artwork belongs to its own decal layer.");VectorArtwork.Validate();}
		if(RasterArtwork is not null){if(Kind!=LayerKind.Sticker||TextArtwork is not null||PatternArtwork is not null||VectorArtwork is not null)throw new InvalidDataException("Image color recipes belong to their own decal layer.");RasterArtwork.Validate();}
		if(Effects is not null){if(Kind!=LayerKind.Sticker)throw new InvalidDataException("Decal effects belong to a sticker layer.");Effects.Validate();}
		if(ColorLinks is null||ColorLinks.Count>256)throw new InvalidDataException("Too many shared color links.");
		foreach(var (key,role) in ColorLinks){if(role is null||!DesignPalette.FieldAllowed(this,key))throw new InvalidDataException("Invalid shared color field.");role.Validate();}
		if(!float.IsFinite(ScaleX)||ScaleX<.05f||ScaleX>20||!float.IsFinite(ScaleY)||ScaleY<.05f||ScaleY>20||!float.IsFinite(WrapAngle)||WrapAngle<1||WrapAngle>89||!float.IsFinite(WrapDepth)||WrapDepth<.01f||WrapDepth>2)throw new InvalidDataException("Decal stretch and wrap settings are outside their supported range.");
		if(!Enum.IsDefined(Kind)||!float.IsFinite(Opacity)||Opacity<0||Opacity>1||!float.IsFinite(Scale)||Scale<.02f||Scale>20||!float.IsFinite(Rotation)||!float.IsFinite(OffsetRight)||!float.IsFinite(OffsetUp)||!float.IsFinite(Gloss)||Gloss<0||Gloss>1||!float.IsFinite(Metalness)||Metalness<0||Metalness>1||!float.IsFinite(Hardness)||Hardness<0||Hardness>1||Triangles.Any(t=>t<0))throw new InvalidDataException("Layer settings are outside their supported range.");
		foreach(var p in Samples.Concat(Erasures))if(!Finite(p.Center)||!Finite(p.Normal)||!Finite(p.Right)||!Finite(p.Up)||p.Normal.LengthSquared()<.9f||p.Right.LengthSquared()<.9f||p.Up.LengthSquared()<.9f||!float.IsFinite(p.Width)||!float.IsFinite(p.Height)||p.Width<=0||p.Height<=0)throw new InvalidDataException("Invalid layer projection.");
	}
	private static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
	public SurfaceProjection Transform(SurfaceProjection p)
	{
		var a=Rotation*MathF.PI/180;var rotation=Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(p.Right,p.Up)),a);var right=Vector3.Transform(p.Right,rotation);var up=Vector3.Transform(p.Up,rotation);
		return p with {Center=p.Center+p.Right*OffsetRight+p.Up*OffsetUp,Right=right,Up=up,Width=p.Width*Scale*ScaleX,Height=p.Height*Scale*ScaleY,MinimumFacing=MathF.Cos(WrapAngle*MathF.PI/180),DepthRange=WrapDepth,FlipHorizontal=FlipHorizontal,FlipVertical=FlipVertical};
	}
	public static SurfaceProjection Reflected(SurfaceProjection p)
	{
		static Vector3 Reflect(Vector3 v)=>new(-v.X,v.Y,v.Z);
		// Preserve the upright, readable artwork orientation on the opposite body side.
		return p with {Center=Reflect(p.Center),Normal=Reflect(p.Normal),Right=-Reflect(p.Right),Up=Reflect(p.Up)};
	}
}

public sealed record EditCheckpoint(string Label,DateTimeOffset Created,List<PaintLayer> Layers)
{
	public List<SharedDesignColor> Palette {get;init;}=[];
	public PaintColorState? PaintColors {get;init;}
	public PaintFinishState? Finish {get;init;}
	public bool CleanPaint {get;init;}
}
public sealed record CompositeMaps(PaintImage Albedo,PaintImage Structure);
public sealed class LayerDocument
{
	public int Version {get;set;}=3;
	public int CarIndex {get;set;}
	public string BaseAlbedo {get;set;}="";
	public string BaseStructure {get;set;}="";
	public int SeamPadding {get;set;}=2;
	public List<EditCheckpoint> History {get;set;}=[new("Original textures",DateTimeOffset.UtcNow,[])];
	public int Cursor {get;set;}
	[JsonIgnore] public List<PaintLayer> Layers=>History[Cursor].Layers;
	[JsonIgnore] public bool CanUndo=>Cursor>0;
	[JsonIgnore] public bool CanRedo=>Cursor<History.Count-1;
	[JsonIgnore] public bool HasMaterial=>Layers.Any(l=>l.Visible&&l.IsMaterial);
	public static readonly JsonSerializerOptions Json=new(Contract.Json){IncludeFields=true};
	public static LayerDocument Create(LiveryProject p,string folder)
	{
		Directory.CreateDirectory(folder);
		string Copy(string source,string name){var path=Path.Combine(folder,name+Path.GetExtension(source));File.Copy(source,path,false);return path;}
		return new(){CarIndex=p.CarIndex,BaseAlbedo=Copy(p.Albedo,"original-color"),BaseStructure=Copy(p.Structure,"original-surface")};
	}
	public void Validate(bool files=true)
	{
        if(Version is not (1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9)||CarIndex<0||CarIndex>=Contract.Cars.Count||History.Count<1||History.Count>128||Cursor<0||Cursor>=History.Count||SeamPadding<0||SeamPadding>8)throw new InvalidDataException("This editor document is invalid or uses an unsupported version.");
        if(Version<9&&History.Any(s=>s.Finish is not null||s.CleanPaint||s.Layers.Any(l=>l.Erasures.Count>0||l.ExteriorOnly||l.ProtectSharedUvs)))throw new InvalidDataException("Undoable finishes and protected paint require editor format version 9.");
        foreach(var state in History)state.Finish?.Validate();
		if(Version<4&&History.Any(s=>s.Layers.Any(l=>l.PatternArtwork is not null)))throw new InvalidDataException("Editable patterns require editor format version 4.");
		if(Version<5&&History.Any(s=>s.Layers.Any(l=>l.VectorArtwork is not null)))throw new InvalidDataException("Retained SVG sources require editor format version 5.");
		if(Version<6&&History.Any(SharedColorFormat))throw new InvalidDataException("Shared colors require editor format version 6.");
		if(Version<7&&History.Any(s=>s.Layers.Any(l=>l.Effects is not null)))throw new InvalidDataException("Decal effects require editor format version 7.");
        if(Version<8&&History.Any(s=>s.Layers.Any(l=>l.RasterArtwork?.Preparation is not null)))throw new InvalidDataException("Editable image preparation requires editor format version 8.");
		if(files&&(!File.Exists(BaseAlbedo)||!File.Exists(BaseStructure)))throw new FileNotFoundException("The editor's original textures are missing. Open a portable project to recover them.");
		foreach(var state in History){if(state.Palette is null)throw new InvalidDataException("Missing design palette.");DesignPalette.ValidateLayers(state.Layers,state.Palette,state.PaintColors);if(state.Layers.Count>512)throw new InvalidDataException("A document supports up to 512 layers.");if(state.Layers.Select(l=>l.Id).Distinct().Count()!=state.Layers.Count)throw new InvalidDataException("Duplicate layer IDs.");foreach(var layer in state.Layers){layer.Validate();layer.Effects?.Validate(files);if(files&&layer.Kind==LayerKind.Sticker&&!File.Exists(layer.Asset))throw new FileNotFoundException("A sticker source is missing: "+layer.Asset);if(files&&layer.VectorArtwork is {} vector&&!File.Exists(vector.SourceFile))throw new FileNotFoundException("An original SVG source is missing: "+vector.SourceFile);if(files&&layer.RasterArtwork is {} raster&&!File.Exists(raster.SourceFile))throw new FileNotFoundException("An original image source is missing: "+raster.SourceFile);}}
	}
	public void Commit(string label,Action<List<PaintLayer>> edit)
	{
		var next=JsonSerializer.Deserialize<List<PaintLayer>>(JsonSerializer.Serialize(Layers,Json),Json)!;
		edit(next);foreach(var layer in next){var before=Layers.FirstOrDefault(l=>l.Id==layer.Id);if(before is not null)DesignPalette.DetachEditedLinks(layer,before);layer.Validate();}
		foreach(var locked in Layers.Where(l=>l.Locked))
		{
			var changed=next.FirstOrDefault(l=>l.Id==locked.Id);if(changed is null||JsonSerializer.Serialize(locked with {Locked=false},Json)!=JsonSerializer.Serialize(changed with {Locked=false},Json))throw new InvalidOperationException("Unlock '"+locked.Name+"' before changing it.");
		}
		if(next.Count>512)throw new InvalidOperationException("This project already contains 512 layers.");
		var current=History[Cursor];var palette=JsonSerializer.Deserialize<List<SharedDesignColor>>(JsonSerializer.Serialize(current.Palette,Json),Json)!;
		foreach(var role in DesignPalette.LinkedColors(next)){var index=palette.FindIndex(c=>c.Id==role.Id);if(index>=0)palette[index]=role;else palette.Add(role);}
		if(palette.Count>32)throw new InvalidOperationException("This design already has 32 shared colors.");
		var checkpoint=new EditCheckpoint(label,DateTimeOffset.UtcNow,next){Palette=palette,PaintColors=current.PaintColors is null?null:JsonSerializer.Deserialize<PaintColorState>(JsonSerializer.Serialize(current.PaintColors,Json),Json),Finish=current.Finish,CleanPaint=current.CleanPaint};
        Version=Math.Max(Version,next.Any(l=>l.Erasures.Count>0||l.ExteriorOnly||l.ProtectSharedUvs)?9:next.Any(l=>l.RasterArtwork?.Preparation is not null)?8:next.Any(l=>l.Effects is not null)?7:SharedColorFormat(checkpoint)?6:next.Any(l=>l.VectorArtwork is not null)?5:next.Any(l=>l.PatternArtwork is not null)?4:3);History.RemoveRange(Cursor+1,History.Count-Cursor-1);History.Add(checkpoint);
		if(History.Count>128)History.RemoveAt(0);Cursor=History.Count-1;
	}
	private static bool SharedColorFormat(EditCheckpoint state)=>state.PaintColors is not null||state.Palette.Count>0||state.Layers.Any(l=>l.ColorLinks.Count>0||l.RasterArtwork is not null||l.VectorArtwork?.Recolors.Count>0);
	public LayerDocument Snapshot(string label,IReadOnlyList<PaintLayer> layers)
	{
		var state=History[Cursor] with {Label=label,Layers=layers.ToList()};
		var copy=JsonSerializer.Deserialize<EditCheckpoint>(JsonSerializer.Serialize(state,Json),Json)!;
		return new(){Version=Version,CarIndex=CarIndex,BaseAlbedo=BaseAlbedo,BaseStructure=BaseStructure,SeamPadding=SeamPadding,History=[copy]};
	}
	public bool Undo(){if(!CanUndo)return false;Cursor--;return true;}
	public bool Redo(){if(!CanRedo)return false;Cursor++;return true;}
	public void Restore(int checkpoint){if(checkpoint<0||checkpoint>=History.Count)throw new ArgumentOutOfRangeException(nameof(checkpoint));Cursor=checkpoint;}
	public void Save(string path)
	{
		if(History.Any(s=>s.Finish is not null||s.CleanPaint||s.Layers.Any(l=>l.Erasures.Count>0)))Version=Math.Max(Version,9);
		if(History.Any(s=>s.Layers.Any(l=>l.PatternArtwork is not null)))Version=Math.Max(Version,4);
		if(History.Any(s=>s.Layers.Any(l=>l.VectorArtwork is not null)))Version=Math.Max(Version,5);
		if(History.Any(SharedColorFormat))Version=Math.Max(Version,6);
		if(History.Any(s=>s.Layers.Any(l=>l.Effects is not null)))Version=Math.Max(Version,7);
        if(History.Any(s=>s.Layers.Any(l=>l.RasterArtwork?.Preparation is not null)))Version=Math.Max(Version,8);
		Validate();AtomicFiles.Write(path,JsonSerializer.SerializeToUtf8Bytes(this,Json),keepPrevious:true);
	}
	public static LayerDocument Read(string path)
	{
		LayerDocument Parse(string file){if(new FileInfo(file).Length>32*1024*1024)throw new InvalidDataException("The editor document is too large.");var d=JsonSerializer.Deserialize<LayerDocument>(File.ReadAllText(file),Json)??throw new InvalidDataException("Empty editor document.");d.Validate();return d;}
		try{return Parse(path);}catch(JsonException){if(File.Exists(path+".previous"))return Parse(path+".previous");throw;}
	}
	public CompositeMaps Compose(PaintMesh mesh,LiveryProject project,CancellationToken cancellation=default,CompositionCache? cache=null)
		=>cache is null?ComposeCore(mesh,project,cancellation):cache.Compose(this,mesh,project,cancellation);
	internal CompositeMaps ComposeCore(PaintMesh mesh,LiveryProject project,CancellationToken cancellation,CompositionCache? cache=null)
	{
		cancellation.ThrowIfCancellationRequested();Validate();project=DesignPalette.ResolvePaint(this,project);if(CarIndex!=project.CarIndex)throw new InvalidDataException("These layers belong to a different car.");
		var baseColor=cache?.BaseImage(BaseAlbedo,cancellation)??FinishMaps.Read(BaseAlbedo,cancellation);var baseSurface=FinishMaps.Artwork(cache?.BaseImage(BaseStructure,cancellation)??FinishMaps.Read(BaseStructure,cancellation),project,cancellation);
		if(History[Cursor].CleanPaint)baseColor=new(baseColor.Width,baseColor.Height,new byte[baseColor.Pixels.Length]);
		if(HasMaterial)FinishMaps.InitializePaintSurface(baseSurface,baseColor,project,cancellation);
		var color=new TexturePainter(baseColor);var surface=new TexturePainter(baseSurface);
		var paintColor=PaintFinish.ToSrgb(PaintFinish.PaintColor(project,project.InnerColor));
		var images=new DecalImageCache(cancellation);
		foreach(var layer in Layers.Where(l=>l.Visible))
		{
			cancellation.ThrowIfCancellationRequested();var image=layer.Kind==LayerKind.Sticker?images.Get(layer.Asset,layer.Effects):null;
			PaintImage? erased=null;if(layer.Erasures.Count>0){var stencil=new TexturePainter(new(color.Image.Width,color.Image.Height,new byte[color.Image.Pixels.Length]));foreach(var dab in layer.Erasures)stencil.Stamp(mesh,dab,null,Vector3.One,1,Vector3.Zero,SimplePlacement.Mask(mesh,SimplePlacement.OnSurface(new()),dab.Normal,cancellation:cancellation),hardness:1,cancellation:cancellation,hardEdge:true);erased=stencil.Image;}
			foreach(var sample in layer.Samples)
			{
				cancellation.ThrowIfCancellationRequested();var projection=layer.Transform(sample);
				void Apply(SurfaceProjection p)
				{
					var targetMask=SimplePlacement.Mask(mesh,layer,p.Normal,cancellation:cancellation);
					if(layer.IsMaterial)
					{
						// The game reads local surface values on alpha-marked textured panels.
						// Promote uniform paint to matching texture RGB without covering existing artwork.
						color.Stamp(mesh,p,null,paintColor,layer.Opacity,paintColor,targetMask,layer.Hardness,markSurface:true,cancellation:cancellation);
						surface.Stamp(mesh,p,null,new(layer.Gloss,layer.Metalness,0),layer.Opacity,paintColor,targetMask,layer.Hardness,material:layer.Kind,cancellation:cancellation);
					}
					else color.Stamp(mesh,p,image,PaintFinish.Color(layer.Color),layer.Opacity,paintColor,targetMask,layer.Hardness,blend:layer.Effects?.Blend??DecalBlend.Normal,cancellation:cancellation,erased:erased);
				}
				Apply(projection);if(layer.Mirror)Apply(PaintLayer.Reflected(projection));
			}
		}
		if(SeamPadding>0&&Layers.Count>0){var coverage=cache?.Coverage(mesh,color.Image.Width,color.Image.Height,cancellation)??MeshEditing.UvCoverage(mesh,color.Image.Width,color.Image.Height,cancellation);MeshEditing.Bleed(color.Image,coverage,SeamPadding,cancellation);if(surface.Image.Width==color.Image.Width&&surface.Image.Height==color.Image.Height)MeshEditing.Bleed(surface.Image,coverage,SeamPadding,cancellation);}
		return new(color.Image,surface.Image);
	}
}

public static class AtomicFiles
{
	public static void Write(string path,byte[] data,bool keepPrevious=false)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
		try{using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(data);stream.Flush(true);}if(keepPrevious&&File.Exists(path))File.Copy(path,path+".previous",true);File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
	}
}
