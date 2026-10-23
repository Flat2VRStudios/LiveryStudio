using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LiveryStudio.Core;

public sealed record VehicleBinding(string Node,int Section,string Role,string Resource);
public sealed record VehicleDonor(int Index,string Name,string Class,string CarId,string BodyResource,string PrefabPath,string BreakablesPath,string FrontWheels,string RearWheels,float[] PreviewCenter,VehicleBinding[] Bindings,Dictionary<string,string> Files)
{
    public override string ToString()=>Name+" · "+Class;
}
public sealed record VehicleFit
{
    public bool AutoFit {get;set;}=true;
    public double Width {get;set;}=1;
    public double Length {get;set;}=1;
    public double Height {get;set;}=1;
    public double Yaw {get;set;}
    public double Side {get;set;}
    public double Forward {get;set;}
    public double Up {get;set;}
    public void Validate()
    {
        if(new[]{Width,Length,Height}.Any(v=>!double.IsFinite(v)||v<.01||v>100)||!double.IsFinite(Yaw)||Math.Abs(Yaw)>360||new[]{Side,Forward,Up}.Any(v=>!double.IsFinite(v)||Math.Abs(v)>20))throw new ArgumentException("Enter valid scale, rotation and offset values.");
    }
}
public sealed record VehicleSceneReport(int ImportedTriangles,int RemovedExteriorTriangles,int RetainedInteriorTriangles,bool GeneratedBoxUv,double UnitCentimeters,double[][] GameSpaceBounds,string[] MeshNodes,string[] EmptyNodes)
{
    public void ValidateCompiled(string readerOutput)
    {
        using var document=JsonDocument.Parse(readerOutput);
        var root=document.RootElement;
        if(!root.GetProperty("resourceLoaded").GetBoolean())throw new InvalidDataException("The engine could not read the compiled assembly.");
        var meshes=root.GetProperty("nodes").EnumerateArray().Where(n=>n.GetProperty("mesh").GetBoolean()).ToArray();
        var names=meshes.Select(n=>n.GetProperty("name").GetString()).ToHashSet(StringComparer.Ordinal);
        if(MeshNodes.Where(n=>!n.Contains("_LOD",StringComparison.OrdinalIgnoreCase)).Any(n=>!names.Contains(n)))throw new InvalidDataException("The native compiler lost an assembled mesh.");
        // The reader reports the base LOD's declarations. A surviving node
        // name alone does not prove it contains drawable, textured sections.
        if(meshes.Length==0||meshes.Any(n=>!n.TryGetProperty("sections",out var sections)||!sections.TryGetInt32(out var count)||count<1||!n.TryGetProperty("uvChannels",out var channels)||channels.ValueKind!=JsonValueKind.Array||channels.GetArrayLength()!=count||channels.EnumerateArray().Any(c=>!c.TryGetInt32(out var uv)||uv<1)))throw new InvalidDataException("The native compiler lost texture coordinates or render sections. No game files were changed.");
    }
}
public sealed record VehicleAssemblyProject
{
    public int Version {get;set;}=1;
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public string Name {get;set;}="My custom car";
    public int DonorIndex {get;set;}
    public bool KeepInterior {get;set;}=true;
    public VehicleFit Fit {get;set;}=new();
    public string ExteriorFile {get;set;}="";
    public string ExteriorHash {get;set;}="";
    public string DonorHash {get;set;}="";
    public string AssemblyFile {get;set;}="";
    public string BindingFile {get;set;}="";
    public string ReportFile {get;set;}="";
    public string PreviewFile {get;set;}="";
    public string PreparedRecipeHash {get;set;}="";
    public string PreparedDesign {get;set;}="";
    public LiveryProject Paint {get;set;}=new(){Name="Custom car paint",OuterColor="#167EBD",InnerColor="#167EBD",OuterGloss=.65,InnerGloss=.65,Metalness=.25,ClearCoat=.8,UseSrgbPaintColors=true,FlatExteriorNormals=true};
    public void Validate(bool source=true)
    {
        if(Version!=1||!Regex.IsMatch(Id,"^[0-9a-f]{32}$")||string.IsNullOrWhiteSpace(Name)||Name.Length>100||Name.Any(char.IsControl)||DonorIndex is <0 or >26||Fit is null||Paint is null)throw new InvalidDataException("Invalid car project.");
        Fit.Validate();
        if(source&&(!File.Exists(ExteriorFile)||!new[]{".fbx",".obj",".glb"}.Contains(Path.GetExtension(ExteriorFile).ToLowerInvariant())||new FileInfo(ExteriorFile).Length>256L*1024*1024))throw new InvalidDataException("Choose an existing FBX, OBJ or GLB exterior smaller than 256 MB.");
    }
    public string RecipeHash()=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{DonorIndex,DonorHash,KeepInterior,Fit,ExteriorHash},Contract.Json)+(Paint.IndependentPaintSides?"|independent-paint-sides-v1":""))));
    public bool Prepared=>PreparedRecipeHash==RecipeHash()&&new[]{AssemblyFile,BindingFile,ReportFile,PreviewFile}.All(File.Exists);
}
public sealed record BuiltVehicle(string Id,VehicleAssemblyProject Project,string Root,BuiltLivery Paint,Dictionary<string,string> Files,Dictionary<string,string> Hashes,VehicleSceneReport Scene,Dictionary<string,string> OriginalHashes,string TargetGameDllHash);

public sealed partial class VehicleAssemblyService
{
    public string RuntimeRoot {get;}
    public string Workspace {get;}
    public string ModelsRoot {get;}
    public static string DefaultRuntime=>Path.Combine(AppContext.BaseDirectory,"VehicleTools");
    public event Action<string>? Log;
    public event Action<int,string>? Progress;
    public VehicleAssemblyService(string? runtime=null,string? workspace=null,string? models=null)
    {
        RuntimeRoot=Path.GetFullPath(runtime??DefaultRuntime);var compiler=Path.Combine(RuntimeRoot,"Bin","Compiler_r.exe");if(File.Exists(compiler))RuntimeRoot=Directory.GetParent(Path.GetDirectoryName(PathIdentity.PhysicalFile(compiler))!)!.FullName;Workspace=Path.GetFullPath(workspace??Path.Combine(WorkshopService.DefaultWorkspace,"vehicles"));ModelsRoot=Path.GetFullPath(models??Path.Combine(AppContext.BaseDirectory,"Models"));
    }
    public IReadOnlyList<VehicleDonor> Donors()=>JsonSerializer.Deserialize<VehicleDonor[]>(File.ReadAllText(Path.Combine(RuntimeRoot,"Donors","catalog.json")),Contract.Json)??throw new InvalidDataException("The donor catalog is missing.");
    public VehicleDonor Donor(int index)=>Donors().SingleOrDefault(d=>d.Index==index)??throw new InvalidOperationException("This car is not included in the bundled donor pack.");
    public string DonorFolder(VehicleDonor donor)=>Path.Combine(RuntimeRoot,"Donors",donor.Index.ToString("00"));
    public void ValidateTools()
    {
        foreach(var name in new[]{"Compiler_r.exe","VehicleSceneBridge.exe","VehicleSceneProbe.exe"})if(!File.Exists(Path.Combine(RuntimeRoot,"Bin",name)))throw new FileNotFoundException("The bundled car tools are incomplete. Extract the whole app folder. Missing "+name);
    }
    private void ValidateDonor(VehicleDonor donor)
    {
        foreach(var f in donor.Files){if(Path.GetFileName(f.Key)!=f.Key)throw new InvalidDataException("Invalid donor file name.");var file=Path.Combine(DonorFolder(donor),f.Key);if(!File.Exists(file)||WorkshopService.Hash(file)!=f.Value)throw new InvalidDataException("A bundled donor source is missing or changed: "+f.Key);}
    }
    public static string DonorSignature(VehicleDonor donor)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(donor.Files.OrderBy(f=>f.Key,StringComparer.OrdinalIgnoreCase),Contract.Json))));
    private string Job()
    {
        var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FLSV",Guid.NewGuid().ToString("N")[..12]);Directory.CreateDirectory(path);var marker=Path.Combine(path,"worker-owned.txt");File.WriteAllText(marker,"Livery Studio isolated vehicle build");return Path.GetDirectoryName(PathIdentity.PhysicalFile(marker))!;
    }
    private async Task<string> Run(string executable,IEnumerable<string> arguments,CancellationToken cancellation,int timeout=90)
    {
        var start=new ProcessStartInfo(Path.Combine(RuntimeRoot,"Bin",executable)){WorkingDirectory=Path.Combine(RuntimeRoot,"Bin"),UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var a in arguments)start.ArgumentList.Add(a);
        using var process=Process.Start(start)??throw new InvalidOperationException("Could not start the bundled "+executable);
        var output=process.StandardOutput.ReadToEndAsync(cancellation);var error=process.StandardError.ReadToEndAsync(cancellation);using var limit=CancellationTokenSource.CreateLinkedTokenSource(cancellation);limit.CancelAfter(TimeSpan.FromSeconds(timeout));
        try{await process.WaitForExitAsync(limit.Token);}catch(OperationCanceledException){if(!process.HasExited)process.Kill(entireProcessTree:true);try{await Task.WhenAll(output,error);}catch(OperationCanceledException){}if(cancellation.IsCancellationRequested)throw;throw new TimeoutException("The car worker exceeded its time limit. Your source files are preserved.");}
        var text=await output;var errors=await error;Log?.Invoke(executable+" "+string.Join(' ',start.ArgumentList)+Environment.NewLine+text+errors);
        if(process.ExitCode!=0||Regex.IsMatch(text+"\n"+errors,@"(?m)^\s*ERROR:"))throw new InvalidOperationException(executable+" could not complete the car build. "+(errors.Split('\n').FirstOrDefault(s=>s.Trim().Length>0)??"See the build log."));return text;
    }
    public async Task ImportAsync(VehicleAssemblyProject project,string source,CancellationToken cancellation=default)
    {
        project.Validate(source:false);ValidateTools();var folder=Path.Combine(Workspace,"projects",project.Id,"sources",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var ext=Path.GetExtension(source).ToLowerInvariant();if(!new[]{".fbx",".obj",".glb"}.Contains(ext)||!File.Exists(source)||new FileInfo(source).Length>256L*1024*1024)throw new ArgumentException("Choose an FBX, OBJ or GLB exterior smaller than 256 MB.");
        var destination=Path.Combine(folder,"Exterior"+ext);if(!PathIdentity.Resolve(source).Equals(PathIdentity.Resolve(destination),StringComparison.OrdinalIgnoreCase))File.Copy(source,destination,true);
        var candidate=JsonSerializer.Deserialize<VehicleAssemblyProject>(JsonSerializer.Serialize(project,Contract.Json),Contract.Json)!;
        candidate.ExteriorFile=destination;candidate.ExteriorHash=WorkshopService.Hash(destination);
        await PrepareAsync(candidate,cancellation);
        cancellation.ThrowIfCancellationRequested();
        project.ExteriorFile=candidate.ExteriorFile;project.ExteriorHash=candidate.ExteriorHash;project.DonorHash=candidate.DonorHash;project.AssemblyFile=candidate.AssemblyFile;project.BindingFile=candidate.BindingFile;project.ReportFile=candidate.ReportFile;project.PreviewFile=candidate.PreviewFile;project.PreparedRecipeHash=candidate.PreparedRecipeHash;project.PreparedDesign=candidate.PreparedDesign;project.Paint=candidate.Paint;
    }
    public async Task<VehicleSceneReport> PrepareAsync(VehicleAssemblyProject project,CancellationToken cancellation=default)
    {
        project.Validate();ValidateTools();var donor=Donor(project.DonorIndex);ValidateDonor(donor);
        if(WorkshopService.Hash(project.ExteriorFile)!=project.ExteriorHash)throw new InvalidDataException("The imported exterior changed. Import the updated source before rebuilding.");
        if(project.Paint.IndependentPaintSides&&(project.ExteriorHash!=donor.Files["Body.FBX"]||!project.KeepInterior||project.Fit!=new VehicleFit{AutoFit=false}))throw new InvalidDataException("Independent sides retain the original car geometry. Create a new corrected copy from the main studio to change cars.");
        project.DonorHash=DonorSignature(donor);
        var job=Job();var source=Path.Combine(job,"Exterior"+Path.GetExtension(project.ExteriorFile));File.Copy(project.ExteriorFile,source);
        var generatedGlbUv=false;if(Path.GetExtension(source).Equals(".glb",StringComparison.OrdinalIgnoreCase)){var converted=Path.Combine(job,"Exterior.obj");generatedGlbUv=await Task.Run(()=>VehicleGlb.WriteObj(source,converted,cancellation),cancellation);source=converted;}
        var assembled=Path.Combine(job,"Assembly.FBX");var map=Path.Combine(job,"Layout.tsv");var previewBody=Path.Combine(job,"Body.flsm");var reportFile=Path.Combine(job,"Report.json");
        Progress?.Invoke(10,"Fitting exterior and retaining donor parts…");
        string F(double value)=>value.ToString("R",CultureInfo.InvariantCulture);
        if(project.Paint.IndependentPaintSides)await Run("VehicleSceneBridge.exe",new[]{"paint-sides",source,Path.Combine(DonorFolder(donor),"bindings.tsv"),assembled,map,previewBody,reportFile},cancellation);
        else await Run("VehicleSceneBridge.exe",new[]{"assemble",Path.Combine(DonorFolder(donor),"Body.FBX"),source,Path.Combine(DonorFolder(donor),"bindings.tsv"),assembled,map,previewBody,reportFile,project.KeepInterior?"1":"0",project.Fit.AutoFit?"1":"0",F(project.Fit.Width),F(project.Fit.Length),F(project.Fit.Height),F(project.Fit.Yaw),F(project.Fit.Side),F(-project.Fit.Forward),F(project.Fit.Up)},cancellation);
        var report=JsonSerializer.Deserialize<VehicleSceneReport>(File.ReadAllText(reportFile),Contract.Json)??throw new InvalidDataException("Invalid assembled scene report.");
        if(generatedGlbUv){report=report with{GeneratedBoxUv=true};File.WriteAllText(reportFile,JsonSerializer.Serialize(report,Contract.Json));}
        if(report.ImportedTriangles<1||!project.Paint.IndependentPaintSides&&project.KeepInterior&&donor.Bindings.Any(b=>b.Role.StartsWith("interior",StringComparison.Ordinal))&&report.RetainedInteriorTriangles<1)throw new InvalidDataException("The assembled model lost required exterior/interior geometry.");
        var preview=Path.Combine(job,"Preview.flsm");ComposePreview(previewBody,Path.Combine(ModelsRoot,donor.Index.ToString("00")+".flsm"),donor.PreviewCenter,preview);
        // A geometry change invalidates triangle-index masks. Keep its old
        // editable livery as a recovery bundle before starting a fresh surface.
        if(project.PreparedRecipeHash.Length>0&&project.PreparedRecipeHash!=project.RecipeHash())
        {
            if(project.Paint.EditorFile.Length>0){
                if(project.PreparedDesign.Length==0)throw new InvalidOperationException("Save and reopen this older car project before changing geometry, so its original model can be recovered.");
                var previous=JsonSerializer.Deserialize<VehicleAssemblyProject>(project.PreparedDesign,Contract.Json)??throw new InvalidDataException("The previous car recipe is invalid.");previous.Paint=project.Paint;
                var recovery=Path.Combine(Workspace,"projects",project.Id,"Previous car "+DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff")+".flcar");VehicleProjectBundle.Save(previous,recovery);
            }
            project.Paint.EditorFile="";project.Paint.Albedo="";project.Paint.Structure="";
        }
        if(!File.Exists(project.Paint.Albedo)){var paint=Path.Combine(Workspace,"projects",project.Id,"paint-"+Guid.NewGuid().ToString("N")[..8]);Directory.CreateDirectory(paint);var color=new PaintImage(1024,1024,new byte[1024*1024*4]);var surface=new PaintImage(1024,1024,new byte[1024*1024*4]);for(var i=0;i<color.Pixels.Length;i+=4){color.Pixels[i]=color.Pixels[i+1]=color.Pixels[i+2]=255;surface.Pixels[i]=surface.Pixels[i+1]=surface.Pixels[i+2]=surface.Pixels[i+3]=255;}project.Paint.Albedo=Path.Combine(paint,"color.tga");project.Paint.Structure=Path.Combine(paint,"surface.tga");FinishMaps.WriteTga(color,project.Paint.Albedo);FinishMaps.WriteTga(surface,project.Paint.Structure);}
        project.AssemblyFile=assembled;project.BindingFile=map;project.ReportFile=reportFile;project.PreviewFile=preview;project.PreparedRecipeHash=project.RecipeHash();project.Paint.CarIndex=project.DonorIndex;project.Paint.FlatExteriorNormals=!project.Paint.IndependentPaintSides;RememberPrepared(project);Progress?.Invoke(100,"Exterior ready · donor parts retained");return report;
    }
    public static void RememberPrepared(VehicleAssemblyProject project)
    {
        if(!project.Prepared)return;var previous=project.PreparedDesign;project.PreparedDesign="";
        try{project.PreparedDesign=JsonSerializer.Serialize(project,Contract.Json);}catch{project.PreparedDesign=previous;throw;}
    }
    private static void ComposePreview(string body,string stock,float[] center,string output)
    {
        var custom=PaintMesh.Read(body);var donor=PaintMesh.Read(stock);if(center.Length!=3)throw new InvalidDataException("Invalid donor preview frame.");var values=custom.NativeVertices.ToList();for(var i=0;i<donor.Vertices.Length;i++)if(donor.Vertices[i].Kind==3){for(var k=0;k<9;k++)values.Add(donor.NativeVertices[i*9+k]+(k<3?center[k]:0));}
        var lo=new Vector3(float.MaxValue);var hi=new Vector3(float.MinValue);for(var i=0;i<values.Count;i+=9){var p=new Vector3(values[i],values[i+1],values[i+2]);lo=Vector3.Min(lo,p);hi=Vector3.Max(hi,p);}var origin=(lo+hi)*.5f;for(var i=0;i<values.Count;i+=9){values[i]-=origin.X;values[i+1]-=origin.Y;values[i+2]-=origin.Z;}
        using var file=File.Create(output);using var writer=new BinaryWriter(file);writer.Write(0x4d534c46u);writer.Write((uint)(values.Count/9));foreach(var v in values)writer.Write(v);
    }
    public async Task<BuiltVehicle> BuildAsync(VehicleAssemblyProject project,WorkshopService paintService,string gameData,CancellationToken cancellation=default)
    {
        project.Validate();if(!project.Prepared)throw new InvalidOperationException("Update the preview after changing the exterior or fit.");var game=DesktopGame.Resolve(gameData);var donor=Donor(project.DonorIndex);ValidateDonor(donor);if(project.DonorHash!=DonorSignature(donor))throw new InvalidOperationException("The donor pack changed. Update the preview before building.");
        var snapshot=JsonSerializer.Deserialize<VehicleAssemblyProject>(JsonSerializer.Serialize(project,Contract.Json),Contract.Json)!;
        // Recompose portable projects with the current worker. This upgrades
        // older FBX channel conventions while retaining their paint and UVs.
        var scene=await PrepareAsync(snapshot,cancellation);
        var id="CAR_"+Guid.NewGuid().ToString("N");var root=Job();var data=Path.Combine(root,"Data");var relative="GRAPH/CARS/LIVERY_STUDIO/"+id[4..20];var source=Path.Combine(data,"Assets",relative);Directory.CreateDirectory(source);Directory.CreateDirectory(Path.Combine(data,"WIN32"));Directory.CreateDirectory(Path.Combine(data,"Settings"));Directory.CreateDirectory(Path.Combine(data,"COMMON/ENGINE/ARCHETYPES"));Directory.CreateDirectory(Path.Combine(data,"COMMON/ARCHETYPES"));
        foreach(var setting in Directory.EnumerateFiles(Path.Combine(RuntimeRoot,"Settings"),"*.XML"))File.Copy(setting,Path.Combine(data,"Settings",Path.GetFileName(setting)));
        File.Copy(snapshot.AssemblyFile,Path.Combine(source,"BODY.FBX"));var p3d=XDocument.Load(Path.Combine(DonorFolder(donor),"Body.P3D.XML"));p3d.Descendants("p3d").Single().SetAttributeValue("path","SOURCES:/"+relative+"/BODY.FBX");p3d.Save(Path.Combine(source,"BODY.P3D.XML"));
        Progress?.Invoke(10,"Compiling the new exterior and donor interior…");await Run("Compiler_r.exe",new[]{"/Input","ASSETS:/"+relative+"/BODY.P3D.XML","/Platform","PC","/ProjectPath",root},cancellation);
        var read=await Run("VehicleSceneProbe.exe",new[]{Path.Combine(data,"WIN32"),relative+"/BODY.P3D"},cancellation);scene.ValidateCompiled(read);
        snapshot.Paint.Name=snapshot.Name;snapshot.Paint.CarIndex=snapshot.DonorIndex;snapshot.Paint.FlatExteriorNormals=!snapshot.Paint.IndependentPaintSides;Progress?.Invoke(35,"Building your paint with the bundled livery tools…");var paint=await paintService.BuildAsync(snapshot.Paint,cancellation,snapshot.Paint.IndependentPaintSides?Path.Combine(RuntimeRoot,"PaintSides.PFX"):null);
        var psi=ComposeScene(donor,snapshot,relative,paint.BodyResource);psi.Save(Path.Combine(source,"BODY.PSI.XML"));await Run("Compiler_r.exe",new[]{"/Input","ASSETS:/"+relative+"/BODY.PSI.XML","/Platform","PC","/ProjectPath",root},cancellation);
        var staging=Path.Combine(root,"Install");Directory.CreateDirectory(staging);var prefab=Path.Combine(gameData,donor.PrefabPath);var breakables=Path.Combine(gameData,donor.BreakablesPath);if(!File.Exists(prefab)||!File.Exists(breakables))throw new InvalidDataException("This game does not expose the donor's prefab and damage configuration.");
        var contract=VehicleContract.Prepare(File.ReadAllText(prefab),File.ReadAllText(breakables),scene,id);var prefabOutput=Path.Combine(staging,"Body.PPF");var breakableOutput=Path.Combine(staging,"Breakables.cfg");File.WriteAllText(prefabOutput,contract.Prefab,new UTF8Encoding(false));File.WriteAllText(breakableOutput,contract.Breakables,new UTF8Encoding(false));
        var compiled=Path.Combine(data,"WIN32",relative);var files=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){["WIN32/"+relative+"/BODY.P3D"]=Path.Combine(compiled,"BODY.P3D"),["WIN32/"+donor.BodyResource[6..]]=Path.Combine(compiled,"BODY.PSI"),[donor.PrefabPath]=prefabOutput,["Common/TUNING/LIVERY_STUDIO/"+id+"/BREAKABLES.CFG"]=breakableOutput};
        foreach(var path in files.Values)if(!File.Exists(path)||new FileInfo(path).Length==0)throw new InvalidDataException("A required car output is missing.");
        var originals=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){[donor.PrefabPath]=WorkshopService.Hash(prefab),[donor.BreakablesPath]=WorkshopService.Hash(breakables),["WIN32/"+donor.BodyResource[6..]]=WorkshopService.Hash(Path.Combine(gameData,"WIN32",donor.BodyResource[6..]))};
        var built=new BuiltVehicle(id,snapshot,root,paint,files,files.ToDictionary(k=>k.Key,v=>WorkshopService.Hash(v.Value),StringComparer.OrdinalIgnoreCase),scene,originals,WorkshopService.Hash(game.GameDll));File.WriteAllText(Path.Combine(root,"vehicle-build.json"),JsonSerializer.Serialize(built,Contract.Json));Progress?.Invoke(100,"Car built · ready for a non-VR test");return built;
    }
    private XDocument ComposeScene(VehicleDonor donor,VehicleAssemblyProject project,string relative,string material)
    {
        var original=XDocument.Load(Path.Combine(DonorFolder(donor),"Body.PSI.XML"));var scene=original.Descendants("scene_instance").Single();scene.SetAttributeValue("scene","ASSETS:/"+relative+"/BODY.P3D.XML");var originals=scene.Descendants("bind").Where(b=>b.Attribute("material") is not null).ToDictionary(b=>(b.Attribute("mesh")!.Value,int.Parse(b.Attribute("geometry")!.Value,CultureInfo.InvariantCulture)),b=>new XElement(b));var parent=scene.Descendants("bind").First(b=>b.Attribute("material") is not null).Parent!;foreach(var bind in scene.Descendants("bind").Where(b=>b.Attribute("material") is not null).ToArray())bind.Remove();
        // The source binds live in a dedicated bindings element. Preserve its
        // existing container/physics binds and replace only render sections.
        foreach(var row in File.ReadLines(project.BindingFile)){var fields=row.Split('\t');if(fields.Length!=5)throw new InvalidDataException("Invalid assembled material layout.");var old=int.Parse(fields[2],CultureInfo.InvariantCulture);var bind=old>=0&&originals.TryGetValue((fields[0],old),out var existing)?new XElement(existing):new XElement("bind",new XAttribute("isGeometryTwoSided","false"));bind.SetAttributeValue("mesh",fields[0]);bind.SetAttributeValue("geometry",fields[1]);bind.SetAttributeValue("material",fields[4]=="__EXTERIOR__"?material.Replace("DATA:/","ASSETS:/",StringComparison.OrdinalIgnoreCase)+".XML":fields[4]);parent.Add(bind);}
        return original;
    }
}

public static class VehicleContract
{
    public sealed record Result(string Prefab,string Breakables,string[] RemovedBreakables);
    public static Result Prepare(string prefab,string breakables,VehicleSceneReport scene,string id)
    {
        if(!Regex.IsMatch(id,"^CAR_[0-9a-f]{32}$"))throw new InvalidDataException("Invalid car build identity.");var nodes=scene.MeshNodes.ToHashSet(StringComparer.Ordinal);var document=XDocument.Parse(prefab);
        foreach(var c in document.Descendants("component"))
        {
            var type=c.Attribute("class")?.Value;
            if(type=="PlayAll::GameServices::CarDeformer")foreach(var p in c.Descendants("prop").Where(p=>p.Attribute("name")?.Value is "NodeNames" or "Nodes with noise"))p.SetAttributeValue("value",string.Join(',',Regex.Matches(p.Attribute("value")?.Value??"",@"\{([^}]+)\}").Select(m=>m.Groups[1].Value).Where(nodes.Contains).Select(n=>"{"+n+"}")));
            if(type=="Rc2VehicleBreakables")c.Descendants("prop").Single(p=>p.Attribute("name")?.Value=="Breakables File").SetAttributeValue("value","DATA:/TUNING/LIVERY_STUDIO/"+id+"/BREAKABLES.CFG");
        }
        var start=Regex.Match(breakables,@"\bBreakables\s*=\s*\{");if(!start.Success)throw new InvalidDataException("The donor damage table is missing.");var opening=breakables.IndexOf('{',start.Index);var end=ClosingBrace(breakables,opening);var table=breakables[(opening+1)..end];var kept=new List<string>();var removed=new List<string>();var offset=0;
        while(offset<table.Length)
        {
            var match=Regex.Match(table[offset..],@"(?m)^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*");if(!match.Success)break;var name=match.Groups[1].Value;var begin=offset+match.Index;var value=offset+match.Index+match.Length;int finish;if(value<table.Length&&table[value]=='{')finish=ClosingBrace(table,value)+1;else{finish=table.IndexOf('\n',value);if(finish<0)finish=table.Length;}var entry=table[begin..finish].Trim().TrimEnd(',');var refs=Regex.Matches(entry,@"ElementName\s*=\s*""([^""]+)""").Select(m=>m.Groups[1].Value).ToArray();var rigid=Regex.Match(entry,@"BreakableCreateRigid\(\s*""([^""]+)""\s*,\s*(?:""([^""]+)""|nil)");bool valid=refs.All(nodes.Contains)&&(!rigid.Success||nodes.Contains(rigid.Groups[1].Value)&&(rigid.Groups[2].Value.Length==0||nodes.Contains(rigid.Groups[2].Value)));if(valid)kept.Add(entry+",");else removed.Add(name);offset=finish;
        }
        if(kept.Count==0||!kept.Any(s=>s.Contains("BreakableCreateWheel",StringComparison.Ordinal)))throw new InvalidDataException("The donor wheel damage contract could not be retained.");var output=breakables[..(opening+1)]+"\n"+string.Join("\n",kept)+"\n}"+breakables[(end+1)..];return new(document.ToString(),output,removed.ToArray());
    }
    private static int ClosingBrace(string text,int opening)
    {
        var depth=0;var quoted=false;var escape=false;var comment=false;for(var i=opening;i<text.Length;i++){var c=text[i];if(comment){if(c=='\n')comment=false;continue;}if(quoted){if(escape)escape=false;else if(c=='\\')escape=true;else if(c=='"')quoted=false;continue;}if(c=='-'&&i+1<text.Length&&text[i+1]=='-'){comment=true;i++;continue;}if(c=='"'){quoted=true;continue;}if(c=='{')depth++;else if(c=='}'&&--depth==0)return i;}throw new InvalidDataException("Unbalanced donor damage configuration.");
    }
}
