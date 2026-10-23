using LiveryStudio.Core;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO.Compression;

var passed=0;
void Check(bool ok,string name){if(!ok)throw new Exception("FAILED: "+name);Console.WriteLine("PASS "+name);passed++;}
void Reject(Action action,string name){bool rejected=false;try{action();}catch{rejected=true;}Check(rejected,name);}
var repo=AppContext.BaseDirectory;
while(!File.Exists(Path.Combine(repo,"LiveryStudio","Core","LiveryStudio.Core.csproj")))repo=Directory.GetParent(repo)?.FullName ?? throw new DirectoryNotFoundException("Run tests from the Livery Studio checkout.");
var gameSourceRepo=Environment.GetEnvironmentVariable("LIVERY_STUDIO_GAME_SOURCE") ?? repo;
if(args.Contains("--doors"))
{
    DoorStampChecks.Run(Check);
    foreach(var car in Contract.Cars)DoorStampChecks.CheckMesh(PaintMesh.Read(Path.Combine(repo,"LiveryStudio/artifacts/models",car.Index.ToString("00")+".flsm")),car.Name,Check);
    Console.WriteLine($"Door checks passed: {passed}");return;
}
var temp=Path.Combine(Path.GetTempPath(),"LiveryStudioTests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
if(args.Contains("--portable"))
{
    EditorChecks.Run(temp,Check,Reject);CreativeChecks.Run(Check,Reject);DirectEditingChecks.Run(Check);
    ArtworkPointChecks.Run(Check,Reject);GroupChecks.Run(temp,Check,Reject);LayoutChecks.Run(Check,Reject);
    CameraChecks.Run(temp,Check,Reject);ColorChecks.Run(temp,Check,Reject);LayerBrowserChecks.Run(temp,Check,Reject);
    DecalEffectChecks.Run(temp,Check,Reject);DecalMaskPaintingChecks.Run(temp,Check,Reject);
    CompositionCacheChecks.Run(temp,Check);CompositeSaveChecks.Run(temp,Check);
    ImportChecks.Run(temp,Check,Reject);CompilerChecks.Run(temp,Check);SessionChecks.Run(temp,Check);
    GameInstallationChecks.Run(temp,Check);SteamCarChecks.Run(temp,Check,Reject);WorkshopChecks.Run(Check);
    Console.WriteLine(JsonSerializer.Serialize(new{Passed=passed,Scope="Portable synthetic and filesystem checks; no game or Workshop upload"},Contract.Json));return;
}
if(args.Contains("--steam-car")){SteamCarChecks.Run(temp,Check,Reject);Console.WriteLine($"Steam car checks passed: {passed}");return;}
if(args.Contains("--independent")){IndependentSidesChecks.Run(repo,temp,Check);Console.WriteLine($"Independent side checks passed: {passed}");return;}
if(args.Contains("--setup")){GameInstallationChecks.Run(temp,Check);Console.WriteLine($"Game setup checks passed: {passed}");return;}
if(args.Contains("--simple")){CompositionCacheChecks.Run(temp,Check);CompositeSaveChecks.Run(temp,Check);PreviewQualityChecks.Run(repo,temp,Check,Reject);SimpleStudioChecks.Run(repo,temp,Check,Reject);DoorStampChecks.Run(Check);Console.WriteLine($"Simple studio checks passed: {passed}");return;}
if(args.Contains("--sessions")){SessionChecks.Run(temp,Check);Console.WriteLine($"Session checks passed: {passed}");return;}
File.WriteAllBytes(Path.Combine(temp,"sample.png"),[1,2,3]);
var draft=new LiveryProject{Albedo=Path.Combine(temp,"sample.png"),Structure=Path.Combine(temp,"sample.png"),CarIndex=26,OuterColor="#FF8000",InnerColor="#102030",OuterGloss=.27,InnerGloss=.49,Metalness=.38,ClearCoat=.76};
Check(Contract.Cars.Count==27,"All 27 cars recovered");
var enums=Regex.Matches(File.ReadAllText(Path.Combine(gameSourceRepo,"Flatout/Code/Sources/FODefines.h")),@"PA_DECLARE_ENUM\( ((?:DERBY|CLASSIC|ALLSTARS)_\w+) \)").Select(m=>m.Groups[1].Value).ToArray();
foreach(var car in Contract.Cars)Check(enums.Contains(car.EngineType),"Game car mapping: "+car.Display+" => "+car.EngineType);
CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("fr-FR");
Check(LiveryProject.Float(.38)=="0.38","Invariant decimal serialization");
Check(LiveryProject.ColorVector("#FF0000")=="1;0;0;1.0","Paint vector encoding");
var legacy=Path.Combine(temp,"Config.xml");File.WriteAllText(legacy,draft.ToLegacyXml());var round=LiveryProject.Read(legacy);
Check(round.CarIndex==26&&round.OuterColor==draft.OuterColor&&round.InnerColor==draft.InnerColor&&round.ClearCoat==draft.ClearCoat,"Original Config.xml roundtrip restores car, colors and values");
Reject(()=> (draft with {Name="\n"}).Validate(),"Reject empty livery name");
Reject(()=> (draft with {Metalness=double.NaN}).Validate(),"Reject NaN finish value");
Reject(()=> (draft with {OuterColor="#GG0000"}).Validate(),"Reject invalid color");
Reject(()=> (draft with {Structure=Path.Combine(temp,"missing.tga")}).Validate(),"Reject missing texture");
Reject(()=>draft.Validate(true),"Publishing requires a banner");
byte[] Header(int type,int depth,int flags){var b=new byte[18];b[2]=(byte)type;b[12]=2;b[14]=1;b[16]=(byte)depth;b[17]=(byte)flags;return b;}
var tga=TgaDecoder.Decode([..Header(2,24,32),0,0,255,0,255,0]);Check(tga.Width==2&&tga.Pixels.SequenceEqual(new byte[]{255,0,0,255,0,255,0,255}),"Decode raw 24-bit TGA color channels");
var rle=TgaDecoder.Decode([..Header(10,32,32),129,17,34,51,128]);Check(rle.Pixels.SequenceEqual(new byte[]{51,34,17,128,51,34,17,128}),"Decode RLE TGA with alpha");
var reverse=TgaDecoder.Decode([..Header(2,24,48),0,0,255,0,255,0]);Check(reverse.Pixels[0]==0&&reverse.Pixels[1]==255,"Decode right-origin TGA orientation");
// Values follow the installed game's CarPaint.cg material equations, independently of lighting.
var finishProject=draft with {OuterColor="#FF0000",InnerColor="#0000FF",OuterGloss=.2,InnerGloss=.8,Metalness=.7,ClearCoat=.6};
var paintSample=PaintFinish.Sample(finishProject,new(0,0,0,0),new(.4f,.3f,0),.25f);
Check(System.Numerics.Vector3.Distance(paintSample.Color,new(.75f,0,.25f))<.001f,"Preview uses the game's linear facing-angle paint blend");
Check(Math.Abs(paintSample.Gloss-.35f)<.001f&&Math.Abs(paintSample.Metalness-.7f)<.001f&&Math.Abs(paintSample.ClearCoat-.6f)<.001f,"Preview retains both gloss layers, metalness and clear coat");
var orangeDisplay=PaintFinish.ToDisplay(PaintFinish.Color("#F56824"));
Check(orangeDisplay.Y>.65f&&orangeDisplay.Y<.69f&&orangeDisplay.Z>.39f&&orangeDisplay.Z<.43f,"Workshop orange is gamma-encoded to the game's brighter tan display color");
var decalSample=PaintFinish.Sample(finishProject,new(.5f,.5f,.5f,1),new(.4f,.3f,0),1);
Check(decalSample.Color.X>.21f&&decalSample.Color.X<.22f&&Math.Abs(decalSample.Gloss-.4f)<.001f&&Math.Abs(decalSample.Metalness-.3f)<.001f&&decalSample.ClearCoat==0,"Opaque artwork uses linearized texture color and the original surface map");
var chosen=finishProject with {UseSrgbPaintColors=true,OuterColor="#808080",InnerColor="#F56824",OverrideArtworkFinish=true,ArtworkGloss=.3,ArtworkMetalness=.7,DamageGloss=.17,DamageMetalness=.92};
var chosenVector=LiveryProject.ColorVector("#808080",true).Split(';');
Check(Math.Abs(float.Parse(chosenVector[0],CultureInfo.InvariantCulture)-.21586f)<.0001f,"Color picker mode writes correctly linearized game paint uniforms");
var nativeXml=System.Xml.Linq.XDocument.Parse(WorkshopService.MaterialXml(chosen,"BODY_01234567890123456789012345678901"));
var damageVector=nativeXml.Descendants("param").Single(p=>p.Attribute("name")?.Value=="DamageRoughnessMetalness").Attribute("value")!.Value.Split(';');
Check(damageVector.Length==4&&damageVector.All(s=>double.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out _)),"Native damage finish contains four valid numeric components");
Check(double.Parse(damageVector[0],CultureInfo.InvariantCulture)==.17&&double.Parse(damageVector[1],CultureInfo.InvariantCulture)==.92,"Native damage finish preserves both independent values");
var originalMap=new PaintImage(2,1,[12,34,255,67,89,101,23,45]);var changedMap=FinishMaps.Artwork(originalMap,chosen);
Check(changedMap.Pixels[0]==76&&changedMap.Pixels[1]==178&&changedMap.Pixels[2]==255&&changedMap.Pixels[3]==67&&changedMap.Pixels[6]==23&&originalMap.Pixels[0]==12,"Custom artwork finish changes only gloss and metalness, preserving masks and source data");
var mapFile=Path.Combine(temp,"finish.tga");FinishMaps.WriteTga(changedMap,mapFile);var mapRound=FinishMaps.Read(mapFile);
Check(mapRound.Pixels.SequenceEqual(changedMap.Pixels)&&mapRound.Width==2&&mapRound.Height==1,"Generated surface map roundtrip preserves RGBA and orientation");
var overrideSample=PaintFinish.Sample(chosen,new(.5f,.5f,.5f,1),new(.8f,.1f,.2f),1);
Check(Math.Abs(overrideSample.Gloss-.24f)<.001f&&Math.Abs(overrideSample.Metalness-.56f)<.001f,"Preview applies artwork finish with the game's blue structure mask");
var advancedXml=Path.Combine(temp,"Advanced.xml");File.WriteAllText(advancedXml,chosen.ToLegacyXml());var advancedRound=LiveryProject.Read(advancedXml);
Check(advancedRound.InnerColor==chosen.InnerColor&&advancedRound.OuterColor==chosen.OuterColor&&advancedRound.UseSrgbPaintColors&&advancedRound.OverrideArtworkFinish&&advancedRound.ArtworkGloss==.3&&advancedRound.DamageMetalness==.92,"Advanced material XML restores color mode, artwork and damage settings");
var oldJson=JsonSerializer.Deserialize<LiveryProject>("{\"Name\":\"Old project\",\"OuterColor\":\"#F56824\"}",Contract.Json)!;
Check(!oldJson.UseSrgbPaintColors&&!oldJson.OverrideArtworkFinish&&oldJson.DamageGloss==1&&oldJson.DamageMetalness==.784314,"Existing projects preserve their original game material interpretation");
foreach(var preset in FinishPreset.All){var applied=preset.Apply(chosen);Check(applied.InnerColor==chosen.InnerColor&&applied.Albedo==chosen.Albedo&&applied.OverrideArtworkFinish&&applied.Metalness==preset.Metalness,"Finish preset preserves artwork and colors: "+preset.Name);}
Reject(()=>(chosen with {ArtworkGloss=double.NaN}).Validate(),"Reject invalid artwork finish");
Reject(()=>(chosen with {DamageMetalness=2}).Validate(),"Reject invalid damage finish");
Reject(()=>TgaDecoder.Decode(Header(2,32,32)),"Reject truncated TGA");
Reject(()=>TgaDecoder.Decode([..Header(10,32,32),255,0,0,0,255]),"Reject oversized TGA packet");

var id="BODY_"+Guid.NewGuid().ToString("N");draft.CarIndex=0;
var root=Path.Combine(temp,"build");var prefix="GRAPH/CARS/DERBY/BUICK/";var files=new[]{prefix+id+".PMA",prefix+"TEXTURES/"+id+"_DIFF.PTX",prefix+"TEXTURES/"+id+"_IOR.PTX"};
var hashes=new Dictionary<string,string>();foreach(var f in files){var p=Path.Combine(root,"Data","WIN32",f);Directory.CreateDirectory(Path.GetDirectoryName(p)!);File.WriteAllText(p,f);hashes[f]=WorkshopService.Hash(p);}
var pkg=Path.Combine(root,"Workshop");Directory.CreateDirectory(Path.Combine(pkg,id));File.WriteAllText(Path.Combine(pkg,id+".PACLOG"),"DATA:/"+files[0]);File.WriteAllText(Path.Combine(pkg,id,id+".PKG"),"test package");
var built=new BuiltLivery(id,draft,root,pkg,"DATA:/"+files[0],"DATA:/PACLOG/CUSTOMPAC/LIVERY_STUDIO/"+id+".PACLOG",hashes);
var game=Path.Combine(temp,"game");var factory=Path.Combine(game,"Common","Tuning","Factories","CarsSkins.cfg");Directory.CreateDirectory(Path.GetDirectoryName(factory)!);File.WriteAllText(factory,"Skins = {}\r\n");
var rewards=Path.Combine(Path.GetDirectoryName(factory)!,"Rewards.cfg");File.WriteAllText(rewards,"Rewards = { CarSkins = {} }\r\n");
var glass=Path.Combine(game,"WIN32","GRAPH","CARS","COMMON","GLASS.PMA");Directory.CreateDirectory(Path.GetDirectoryName(glass)!);File.WriteAllText(glass,"glass");
var service=new WorkshopService(WorkshopService.DefaultTools,Path.Combine(temp,"workspace"));
WorkshopService.ValidateBuild(built with {Hashes=hashes.ToDictionary(k=>k.Key.ToUpperInvariant(),v=>v.Value)});
Check(true,"Accept native compiler uppercase resource names");
WorkshopService.ValidateBuild(built with {Project=draft with {Albedo="missing-original.png",Structure="missing-original.tga"}});
Check(true,"Finished native builds do not depend on original source texture paths");
var receipt=service.Import(built,game);
Check(File.ReadAllText(receipt.Backup)=="Skins = {}\r\n","Import preserves original settings backup");
Check(File.ReadAllText(receipt.RewardsBackup!)=="Rewards = { CarSkins = {} }\r\n","Import preserves original unlock settings backup");
Check(File.ReadAllText(rewards).Contains("table.insert(Rewards.CarSkins, { Id = \""+receipt.Key+"\" })")&&WorkshopService.Hash(rewards)==receipt.RewardsHash,"Import registers matching unconditional skin unlock");
Check(receipt.InstalledFiles.All(File.Exists)&&File.ReadAllText(factory).Contains("CarType = \"DERBY_Buick\""),"Local import stages native files and registers new skin");
var after=File.ReadAllText(factory);Reject(()=>service.Import(built,game),"Reject duplicate import");Check(after==File.ReadAllText(factory),"Duplicate import preserves game settings");
var conflictGame=Path.Combine(temp,"conflict-game");
var conflictFactory=Path.Combine(conflictGame,"Common","Tuning","Factories","CarsSkins.cfg");Directory.CreateDirectory(Path.GetDirectoryName(conflictFactory)!);File.WriteAllText(conflictFactory,"Skins = {}\r\n");
var conflictRewards=Path.Combine(Path.GetDirectoryName(conflictFactory)!,"Rewards.cfg");File.WriteAllText(conflictRewards,"Rewards = { CarSkins = {} }\r\n");
var conflictGlass=Path.Combine(conflictGame,"WIN32","GRAPH","CARS","COMMON","GLASS.PMA");Directory.CreateDirectory(Path.GetDirectoryName(conflictGlass)!);File.WriteAllText(conflictGlass,"glass");
var conflictTexture=Path.Combine(conflictGame,"WIN32",files[1]);Directory.CreateDirectory(Path.GetDirectoryName(conflictTexture)!);File.WriteAllText(conflictTexture,"existing content");
Reject(()=>service.Import(built,conflictGame),"Reject an import that would overwrite an existing native texture");
Check(File.ReadAllText(conflictFactory)=="Skins = {}\r\n"&&File.ReadAllText(conflictRewards)=="Rewards = { CarSkins = {} }\r\n","Failed import preserves both game settings files");
Check(!File.Exists(Path.Combine(conflictGame,"WIN32",files[0]))&&File.ReadAllText(conflictTexture)=="existing content","Failed import removes staged files and preserves existing content");
var zip=Path.Combine(temp,"export.zip");WorkshopService.Export(built,zip);using(var archive=ZipFile.OpenRead(zip)){Check(archive.GetEntry(id+".PACLOG")!=null&&archive.GetEntry(id+"/"+id+".PKG")!=null,"Export preserves original Workshop layout");}
var malicious=built with {Hashes=new Dictionary<string,string>{{"../../../escape.PTX","x"}}};Reject(()=>WorkshopService.ValidateBuild(malicious),"Reject invalid file manifest and path traversal");
var fileToCorrupt=Path.Combine(root,"Data","WIN32",files[0]);File.AppendAllText(fileToCorrupt,"tampered");Reject(()=>WorkshopService.ValidateBuild(built),"Reject tampered compiled content");
// Surface projection tests use real UV triangles, not screen-space implementation mocks.
float[] quad=[-1,-1,0,0,0,1,0,1,1, 1,-1,0,0,0,1,1,1,1, 1,1,0,0,0,1,1,0,1, -1,-1,0,0,0,1,0,1,1, 1,1,0,0,0,1,1,0,1, -1,1,0,0,0,1,0,0,1];
var surface=new PaintMesh(quad);var hit=surface.Raycast(new(0,0,2),new(0,0,-1));
Check(hit is not null && System.Numerics.Vector2.Distance(hit.Value.UV,new(.5f,.5f))<.001f,"Ray hit interpolates the original livery UV");
Check(surface.Raycast(new(4,0,2),new(0,0,-1)) is null,"Ray outside the car does not hit");
var blocker=quad.Select((v,i)=>i%9==2?.5f:i%9==8?2:v).ToArray();var blocked=new PaintMesh([..quad,..blocker]);
Check(blocked.Raycast(new(0,0,2),new(0,0,-1)) is null,"A glass panel occludes painting behind it");
var projection=SurfaceProjection.At(hit!.Value,System.Numerics.Vector3.UnitX,1,1,0);
Check(projection.Map(new(.25f,0,0),System.Numerics.Vector3.UnitZ,out var mapped)&&Math.Abs(mapped.X-.75f)<.001f,"Sticker projection maps world positions to artwork coordinates");
Check(!projection.Map(new(0,0,-1),System.Numerics.Vector3.UnitZ,out _),"Sticker cannot project onto the opposite side of the car");
Check(!projection.Map(new(0,0,0),-System.Numerics.Vector3.UnitZ,out _),"Sticker rejects back-facing panels");
var rgba=Enumerable.Repeat((byte)255,64*64*4).ToArray();var surfacePainter=new TexturePainter(new PaintImage(64,64,rgba));
Check(surfacePainter.Image.Pixels.SequenceEqual(rgba),"Hover projection does not alter the texture");
var transparent=new PaintImage(1,1,[255,0,0,0]);
surfacePainter.Begin();Check(surfacePainter.Stamp(surface,projection,transparent,new(1,0,0),1,new(0,1,0))==0&&!surfacePainter.Commit(),"Transparent sticker pixels preserve the base texture");
var redSticker=new PaintImage(1,1,[255,0,0,255]);surfacePainter.Begin();var painted=surfacePainter.Stamp(surface,projection,redSticker,new(1,0,0),1,new(0,1,0));surfacePainter.Commit();
var midpoint=(32*64+32)*4;
Check(painted>100&&surfacePainter.Image.Pixels[midpoint]==255&&surfacePainter.Image.Pixels[midpoint+1]==0&&surfacePainter.Image.Pixels[midpoint+3]==255,"Stamp writes RGB and sets the game artwork alpha");
Check(surfacePainter.Image.Pixels[0]==255&&surfacePainter.Image.Pixels[3]==255,"Stamp preserves pixels outside its footprint");
Check(rgba.All(v=>v==255),"Painting preserves the imported original image");
Check(surfacePainter.Undo()&&surfacePainter.Image.Pixels.SequenceEqual(rgba),"Undo restores the original texture including its alpha mask");
Check(surfacePainter.Redo()&&surfacePainter.Image.Pixels[midpoint+1]==0,"Redo restores the stamped artwork");
surfacePainter.Begin();surfacePainter.Stamp(surface,projection,null,new(0,0,1),1,new(0,1,0));surfacePainter.Commit();
Check(surfacePainter.Image.Pixels[midpoint+2]>240&&surfacePainter.Undo()&&surfacePainter.Image.Pixels[midpoint]==255,"Brush stroke is independently undoable after a sticker");
var masked=Enumerable.Repeat((byte)0,64*64*4).ToArray();var maskedPainter=new TexturePainter(new PaintImage(64,64,masked));
maskedPainter.Begin();maskedPainter.Stamp(surface,projection,redSticker,new(1,0,0),.5f,new(0,1,0));maskedPainter.Commit();
Check(maskedPainter.Image.Pixels[midpoint]==128&&maskedPainter.Image.Pixels[midpoint+1]==128&&maskedPainter.Image.Pixels[midpoint+3]==255,"Partial-opacity artwork blends with the game paint color on alpha-zero areas");
Check(maskedPainter.Image.Pixels[(0*64+0)*4+3]==0,"Painted texture preserves the game paint mask outside a stamp");
Check(maskedPainter.Undo()&&maskedPainter.Image.Pixels.SequenceEqual(masked),"Undo restores alpha-zero paint regions byte-for-byte");
var duplicatedPainter=new TexturePainter(new PaintImage(64,64,rgba));duplicatedPainter.Begin();duplicatedPainter.Stamp(new PaintMesh([..quad,..quad]),projection,redSticker,new(1,0,0),.5f,new(0,1,0));duplicatedPainter.Commit();
Check(duplicatedPainter.Image.Pixels[midpoint+1]==128,"Shared UV triangles do not apply sticker opacity twice");
Reject(()=>new PaintMesh([float.NaN]),"Reject a corrupt preview mesh");
Reject(()=>new PaintImage(2,2,[0]).Validate(),"Reject malformed sticker pixel data");
var previews=Path.Combine(repo,"LiveryStudio","artifacts","models");if(Directory.Exists(previews)){foreach(var car in Contract.Cars){var preview=PaintMesh.Read(Path.Combine(previews,car.Index.ToString("00")+".flsm"));Check(preview.Vertices.Any(v=>v.Kind==1)&&preview.Radius>1&&preview.Radius<12,"Bundled real model and UV data: "+car.Name);CameraChecks.CheckMesh(preview,car.Name,Check);StarterChecks.CheckMesh(preview,car.Name,Check);}}
EditorChecks.Run(temp,Check,Reject);
CreativeChecks.Run(Check,Reject);
DirectEditingChecks.Run(Check);
ArtworkPointChecks.Run(Check,Reject);
GroupChecks.Run(temp,Check,Reject);
ArtworkReplacementChecks.Run(temp,Check,Reject);
ImagePreparationChecks.Run(temp,Check,Reject);
OppositeSideChecks.Run(temp,repo,Check,Reject);
LayoutChecks.Run(Check,Reject);
ArtworkRepeatChecks.Run(temp,repo,Check,Reject);
CameraChecks.Run(temp,Check,Reject);
PatternChecks.Run(temp,Check,Reject);
ColorChecks.Run(temp,Check,Reject);
ArtworkBrowsingChecks.Run(temp,Check,Reject);
if(OperatingSystem.IsWindows())PhotographyChecks.Run(repo,temp,Check,Reject);
StarterChecks.Run(repo,temp,Check,Reject);
LayerBrowserChecks.Run(temp,Check,Reject);
if(OperatingSystem.IsWindows())VectorChecks.Run(repo,temp,Check,Reject);
if(OperatingSystem.IsWindows())DesignPaletteChecks.Run(temp,Check,Reject);
DecalEffectChecks.Run(temp,Check,Reject);
CompositionPerformanceChecks.Run(temp,Check);
CompositionCacheChecks.Run(temp,Check);
if(OperatingSystem.IsWindows())CompositeSaveChecks.Run(temp,Check);
PreviewQualityChecks.Run(repo,temp,Check,Reject);
if(OperatingSystem.IsWindows())SimpleStudioChecks.Run(repo,temp,Check,Reject);
DecalMaskPaintingChecks.Run(temp,Check,Reject);
MeshQueryChecks.Run(repo,Check);
if(OperatingSystem.IsWindows())TextChecks.Run(temp,Check,Reject);
ImportChecks.Run(temp,Check,Reject);
CompilerChecks.Run(temp,Check);
SessionChecks.Run(temp,Check);
GameInstallationChecks.Run(temp,Check);
IndependentSidesChecks.Run(repo,temp,Check);
SteamCarChecks.Run(temp,Check,Reject);
WorkshopChecks.Run(Check);
DoorStampChecks.Run(Check);
foreach(var car in Contract.Cars)DoorStampChecks.CheckMesh(PaintMesh.Read(Path.Combine(previews,car.Index.ToString("00")+".flsm")),car.Name,Check);
GraphicsChecks.Run(repo,temp,Check);
var summary=new{Passed=passed,Scratch=temp,NativeBakeTest="Recorded separately from these unit and filesystem tests"};Console.WriteLine(JsonSerializer.Serialize(summary,Contract.Json));
