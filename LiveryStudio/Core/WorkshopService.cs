using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LiveryStudio.Core;

public sealed class WorkshopService(string toolsRoot, string workspace)
{
	public string ToolsRoot { get; } = Path.GetFullPath(toolsRoot);
	public string Workspace { get; } = Path.GetFullPath(workspace);
	public static string DefaultTools => Path.Combine(AppContext.BaseDirectory,"Workshop");
	public static string DefaultWorkspace => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlatOutLiveryStudio");
	public event Action<string>? Log;
	public event Action<int,string>? Progress;
	private string? compilerBin;
	public void ValidateTools()
	{
		foreach (var name in new[] { "Compiler_r.exe", "KtPkg.exe", "SteamWorkShopSender.exe" })
			if (!File.Exists(Path.Combine(ToolsRoot, "Bin", name))) throw new FileNotFoundException("The app bundle is incomplete. Extract the entire Livery Studio ZIP. Missing " + name);
	}
	public (string Albedo, string Structure) Sample(Car car)
	{
		var folder=Path.Combine(ToolsRoot,"Doc",car.Category);
		string Find(string suffix) => Directory.EnumerateFiles(folder,"*.png").FirstOrDefault(f=>Path.GetFileNameWithoutExtension(f).Equals(car.Template+suffix,StringComparison.OrdinalIgnoreCase)) ?? throw new FileNotFoundException("The car's sample texture was not found.");
		return (Find("_Diff"),Find("_IOR"));
	}
	public static string MaterialXml(LiveryProject project,string id)
	{
		var car=project.SelectedCar;var relative=$"GRAPH/CARS/{car.Class}/{car.CarId}";
		var values=new Dictionary<string,string>{
			["DiffuseMap"]=$"ASSETS:/{relative}/TEXTURES/{id}_DIFF.PTX.XML",
			["StructureMap"]=$"ASSETS:/{relative}/TEXTURES/{id}_IOR.PTX.XML",
			["NormalMap"]=$"ASSETS:/{relative}/TEXTURES/BODY_NRM.PTX.XML",
			["DirtMap"]=$"ASSETS:/{relative}/TEXTURES/BODY_DIRT_DIFF_FAKE.PTX.XML",
			["DiffuseColor1"]=LiveryProject.ColorVector(project.OuterColor,project.UseSrgbPaintColors),
			["DiffuseColor2"]=LiveryProject.ColorVector(project.InnerColor,project.UseSrgbPaintColors),
			["Gloss1"]=LiveryProject.Float(project.OuterGloss),["Gloss2"]=LiveryProject.Float(project.InnerGloss),
			["Metalness"]=LiveryProject.Float(project.Metalness),["ClearCoat"]=LiveryProject.Float(project.ClearCoat),
			["DamageRoughnessMetalness"]=$"{LiveryProject.Float(project.DamageGloss)}; {LiveryProject.Float(project.DamageMetalness)}; 0; 0"};
		var document=System.Xml.Linq.XDocument.Parse(Contract.Template("Material_PMA.txt"));
		if(project.IndependentPaintSides)document.Descendants("material").Single().SetAttributeValue("effect",$"ASSETS:/{relative}/{id}_SIDES.PFX.XML");
		foreach(var parameter in document.Descendants("param"))if(values.TryGetValue(parameter.Attribute("name")!.Value,out var value))parameter.SetAttributeValue("value",value);
		if(project.FlatExteriorNormals)foreach(var trigger in document.Descendants("trigger").Where(t=>t.Attribute("name")?.Value=="Use NormalMap"))trigger.SetAttributeValue("value","false");
		return document.ToString();
	}
	public async Task<BuiltLivery> BuildAsync(LiveryProject project, CancellationToken cancellation = default,string? independentPaintEffect=null)
	{
		project.Validate(); ValidateTools();
		if(project.IndependentPaintSides&&!File.Exists(independentPaintEffect))throw new InvalidOperationException("Independent sides require the matching car model. Open this car project in Car Builder to build and install it.");
		// Snapshot the draft. Editing the UI while compiling cannot change the output.
		project=JsonSerializer.Deserialize<LiveryProject>(JsonSerializer.Serialize(project,Contract.Json),Contract.Json)!;
		var car=project.SelectedCar;
		var id="BODY_"+Guid.NewGuid().ToString("N");
		var root=Path.Combine(Workspace,"builds",id);
		var data=Path.Combine(root,"Data");
		var relative=$"GRAPH/CARS/{car.Class}/{car.CarId}";
		var source=Path.Combine(data,"Assets",relative.Replace('/',Path.DirectorySeparatorChar));
		Directory.CreateDirectory(Path.Combine(source,"TEXTURES")); Directory.CreateDirectory(Path.Combine(data,"Settings")); Directory.CreateDirectory(Path.Combine(data,"WIN32"));
		File.WriteAllText(Path.Combine(data,"Settings","TEXTURE.SETTINGS.XML"),Contract.Template("TEXTURE_SETTINGS.txt"),new UTF8Encoding(false));
		var layeredSurface=project.EditorFile.Length>0&&LayerDocument.Read(project.EditorFile).HasMaterial;
		var albedoExt=Path.GetExtension(project.Albedo); var structureExt=project.OverrideArtworkFinish&&!layeredSurface?".tga":Path.GetExtension(project.Structure);
		File.Copy(project.Albedo,Path.Combine(source,"TEXTURES",id+"_DIFF"+albedoExt));
		var structureDestination=Path.Combine(source,"TEXTURES",id+"_IOR"+structureExt);
		if(project.OverrideArtworkFinish&&!layeredSurface)FinishMaps.WriteTga(FinishMaps.Artwork(FinishMaps.Read(project.Structure),project),structureDestination);else File.Copy(project.Structure,structureDestination);
		File.WriteAllText(Path.Combine(source,"TEXTURES",id+"_DIFF.PTX.XML"),Contract.Template("Diffuse_PTX.txt").Replace("ASSETS:/DiffuseEXT_VALUE",$"SOURCES:/{relative}/TEXTURES/{id}_DIFF{albedoExt}"));
		File.WriteAllText(Path.Combine(source,"TEXTURES",id+"_IOR.PTX.XML"),Contract.Template("Structure_PTX.txt").Replace("ASSETS:/StructureEXT_VALUE",$"SOURCES:/{relative}/TEXTURES/{id}_IOR{structureExt}"));
		var material=MaterialXml(project,id);
		File.WriteAllText(Path.Combine(source,id+".PMA.XML"),material);
		var assets=new[] { $"ASSETS:/{relative}/TEXTURES/{id}_DIFF.PTX.XML",$"ASSETS:/{relative}/TEXTURES/{id}_IOR.PTX.XML",$"ASSETS:/{relative}/{id}.PMA.XML" };
		var labels=new[] { "Compiling albedo", "Compiling structure", "Compiling paint material" };
		using var log=new StreamWriter(Path.Combine(root,"build.log"),false,new UTF8Encoding(false));
		void Report(string line) { lock(log){log.WriteLine(line);log.Flush();} Log?.Invoke(line); }
		for(var i=0;i<assets.Length;i++)
		{
			Progress?.Invoke(i*25,labels[i]);
			await RunAsync("Compiler_r.exe",["/Input",assets[i],"/Platform","PC","/ProjectPath",root],Report,cancellation);
		}
		var paclog=new[] { $"DATA:/{relative}/{id}.PMA","DATA:/EFFECTS/CARPAINT.PFX",$"DATA:/{relative}/TEXTURES/{id}_DIFF.PTX",$"DATA:/{relative}/TEXTURES/BODY_NRM.PTX",$"DATA:/{relative}/TEXTURES/{id}_IOR.PTX",$"DATA:/{relative}/TEXTURES/BODY_DIRT_DIFF_FAKE.PTX","DATA:/GRAPH/CARS/COMMON/GLASS.PMA","DATA:/GRAPH/CARS/COMMON/TEXTURES/BODY_DIRT_IOR_FAKE.PTX","DATA:/GRAPH/CARS/COMMON/TEXTURES/BODY_DAMAGE_DIFF_FAKE.PTX","DATA:/GRAPH/CARS/COMMON/TEXTURES/BODY_DAMAGE_NRM_FAKE.PTX","DATA:/EFFECTS/VEHICLEGLASS.PFX",$"DATA:/{relative}/TEXTURES/BODY_DAMAGE_DIFF.PTX",$"DATA:/{relative}/TEXTURES/BODY_DAMAGE_NRM.PTX","DATA:/TEXTURES/COMMON_MAP/WHITE_ALPHA.PTX" };
		if(project.IndependentPaintSides){File.Copy(independentPaintEffect!,Path.Combine(data,"WIN32",relative,id+"_SIDES.PFX"));paclog=paclog.Append($"DATA:/{relative}/{id}_SIDES.PFX").ToArray();}
		File.WriteAllLines(Path.Combine(data,"WIN32","Paclog.PACLOG"),paclog);
		var settings=Path.Combine(root,"GamePkg.cfg"); File.WriteAllText(settings,Contract.Template("GamePkg.txt"));
		var pkg=Path.Combine(data,"WIN32","Pkg","CustomPkg","Pkg.pkg");
		Progress?.Invoke(75,"Building Workshop package");
		await RunAsync("KtPkg.exe",["compress",data,pkg,"WIN32","DATA:/","-R","-S",settings,"-silent","-paclog","Data:/paclog.PACLOG"],Report,cancellation);
		if(!File.Exists(pkg) || new FileInfo(pkg).Length<64) throw new InvalidDataException("The package builder did not produce a valid output. See the build log.");
		var package=Path.Combine(root,"Workshop"); Directory.CreateDirectory(Path.Combine(package,id));
		File.Copy(pkg,Path.Combine(package,id,id+".PKG")); File.Copy(Path.Combine(data,"WIN32","Paclog.PACLOG"),Path.Combine(package,id+".PACLOG"));
		var hashes=Directory.EnumerateFiles(Path.Combine(data,"WIN32"),"*",SearchOption.AllDirectories).Where(f=>Regex.IsMatch(Path.GetFileName(f),"^"+id+@".*\.(PTX|PMA)$",RegexOptions.IgnoreCase)).ToDictionary(f=>Path.GetRelativePath(Path.Combine(data,"WIN32"),f).Replace('\\','/'),Hash);
		if(hashes.Count!=3) throw new InvalidDataException("Expected two compiled textures and a material.");
		if(project.IndependentPaintSides)hashes.Add(relative+"/"+id+"_SIDES.PFX",Hash(Path.Combine(data,"WIN32",relative,id+"_SIDES.PFX")));
		project.Albedo=Path.Combine(source,"TEXTURES",id+"_DIFF"+albedoExt);
		project.Structure=Path.Combine(source,"TEXTURES",id+"_IOR"+structureExt);
		if(File.Exists(project.Banner)){var banner=Path.Combine(root,"banner"+Path.GetExtension(project.Banner));File.Copy(project.Banner,banner);project.Banner=banner;}
		ProjectBundle.Save(project,Path.Combine(root,"Source.flproject"));
		var result=new BuiltLivery(id,project,root,package,$"DATA:/{relative}/{id}.PMA",$"DATA:/PACLOG/CUSTOMPAC/LIVERY_STUDIO/{id}.PACLOG",hashes);
		File.WriteAllText(Path.Combine(root,"livery.json"),JsonSerializer.Serialize(result,Contract.Json));
		File.WriteAllText(Path.Combine(root,"Config.xml"),project.ToLegacyXml());
		Progress?.Invoke(100,"Livery ready"); return result;
	}
	private async Task RunAsync(string exe,IEnumerable<string> args,Action<string> log,CancellationToken ct)
	{
		var arguments=args.ToArray();
		for(var attempt=0;attempt<3;attempt++)
		{
			var bin=exe=="Compiler_r.exe"&&compilerBin is not null?compilerBin:Path.Combine(ToolsRoot,"Bin");
			var start=new ProcessStartInfo(Path.Combine(bin,exe)) { WorkingDirectory=bin,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
			if(compilerBin is not null&&exe=="Compiler_r.exe")start.Environment["DXVK_LOG_LEVEL"]="none";
			foreach(var arg in arguments)start.ArgumentList.Add(arg);
			log(start.FileName+" "+string.Join(" ",start.ArgumentList.Select(a=>'"'+a+'"')));
			using var process=Process.Start(start) ?? throw new InvalidOperationException("Could not start "+exe);
			var directXFailed=false;
			async Task Read(StreamReader reader){while(await reader.ReadLineAsync() is{} line){if(line.Contains("Couldn't initialize DirectX",StringComparison.OrdinalIgnoreCase))directXFailed=true;log(line);}}
			var reads=Task.WhenAll(Read(process.StandardOutput),Read(process.StandardError));
			try {await process.WaitForExitAsync(ct);await reads;}
			catch(OperationCanceledException){if(!process.HasExited)process.Kill(entireProcessTree:true);await reads;throw;}
			if(process.ExitCode==0)return;
			if(directXFailed&&exe=="Compiler_r.exe"&&attempt<2)
			{
				if(compilerBin is null){compilerBin=CompilerCompatibility.Prepare(ToolsRoot,Workspace);if(compilerBin is not null)log("Windows DirectX 9 could not initialize. Retrying with the bundled DXVK compatibility runtime.");}
				else log("The graphics compiler could not initialize. Retrying...");
				await Task.Delay(500*(attempt+1),ct);continue;
			}
			throw new InvalidOperationException(directXFailed?"The bundled compiler could not initialize graphics. Check the graphics driver and see Build log for details.":$"{exe} failed (exit {process.ExitCode}). See the build log.");
		}
	}
	public async Task<WorkshopPublication> PublishAsync(BuiltLivery livery,CancellationToken ct=default,uint appId=SteamWorkshop.FlatOutVR)
	{
		if(livery.Project.IndependentPaintSides)throw new InvalidOperationException("This design includes a corrected car model. Install its car project through Car Builder; the standard livery Workshop format cannot include it.");
		livery.Project.Validate(publishing:true,textureSources:false); ValidateBuild(livery);
		return await SteamWorkshop.PublishAsync(livery,ToolsRoot,appId,s=>Log?.Invoke(s),ct);
	}
	public static string Hash(string path) { using var s=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(s)); }
	public static void ValidateBuild(BuiltLivery livery)
	{
		if(!Regex.IsMatch(livery.Id,"^BODY_[0-9a-f]{32}$"))throw new InvalidDataException("Invalid livery ID.");
		livery.Project.Validate(textureSources:false);
		var car=livery.Project.SelectedCar;
		var prefix=$"GRAPH/CARS/{car.Class}/{car.CarId}/";
		var expected=new[]{prefix+livery.Id+".PMA",prefix+"TEXTURES/"+livery.Id+"_DIFF.PTX",prefix+"TEXTURES/"+livery.Id+"_IOR.PTX"};
		if(livery.Project.IndependentPaintSides)expected=expected.Append(prefix+livery.Id+"_SIDES.PFX").ToArray();
		// The native compiler uppercases resource names on disk.
		if(livery.Hashes.Count!=expected.Length||expected.Any(p=>!livery.Hashes.Keys.Contains(p,StringComparer.OrdinalIgnoreCase)))throw new InvalidDataException("The livery must contain its material, two texture maps and any required paint effect.");
		foreach(var file in livery.Hashes)
		{
			var path=Path.GetFullPath(Path.Combine(livery.BuildRoot,"Data","WIN32",file.Key));
			if(!path.StartsWith(Path.GetFullPath(Path.Combine(livery.BuildRoot,"Data","WIN32"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) || !File.Exists(path) || Hash(path)!=file.Value)throw new InvalidDataException("A compiled livery file is missing or changed: "+file.Key);
		}
	}
	public static void Export(BuiltLivery livery,string file)
	{
		if(livery.Project.IndependentPaintSides)throw new InvalidOperationException("Save this corrected model as a car project through Car Builder. A standard livery ZIP cannot include the matching geometry.");
		ValidateBuild(livery); var temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
		ZipFile.CreateFromDirectory(livery.PackageDirectory,temp); File.Move(temp,file,true);
	}
	public ImportReceipt Import(BuiltLivery livery,string gameData)
	{
		if(livery.Project.IndependentPaintSides)throw new InvalidOperationException("Install this corrected model through Car Builder so its geometry and paint are installed together.");
		ValidateBuild(livery); gameData=Path.GetFullPath(gameData);using var importGate=GameImportLock.Enter(gameData);
		var factory=Path.Combine(gameData,"Common","Tuning","Factories","CarsSkins.cfg");
		if(!File.Exists(factory))throw new InvalidOperationException("This installation does not support direct livery imports. Use Steam Workshop, or choose an installation that supports direct imports in Setup.");
		var original=File.ReadAllText(factory); var marker="-- LIVERY_STUDIO_BEGIN "+livery.Id;
		if(original.Contains(marker))throw new InvalidOperationException("This livery is already imported.");
		var rewards=Path.Combine(gameData,"Common","Tuning","Factories","Rewards.cfg");
		if(!File.Exists(rewards))throw new InvalidOperationException("The game is missing editable Rewards.cfg settings needed to unlock the imported livery.");
		var originalRewards=File.ReadAllText(rewards);
		var car=livery.Project.SelectedCar;
		var glass=$"GRAPH/CARS/{car.Class}/{car.CarId}/GLASS_01.PMA";
		if(!File.Exists(Path.Combine(gameData,"WIN32",glass)))glass="GRAPH/CARS/COMMON/GLASS.PMA";
		if(!File.Exists(Path.Combine(gameData,"WIN32",glass)))throw new InvalidOperationException("The selected game is missing its car glass material.");
		var backups=Path.Combine(Workspace,"imports",livery.Id,Guid.NewGuid().ToString("N")); Directory.CreateDirectory(backups);
		var backup=Path.Combine(backups,"CarsSkins.cfg.before"); File.Copy(factory,backup,false);
		var rewardsBackup=Path.Combine(backups,"Rewards.cfg.before"); File.Copy(rewards,rewardsBackup,false);
		var created=new List<string>();
		string? committed=null, rewardsCommitted=null;
		try
		{
			foreach(var file in livery.Hashes)
			{
				var dest=Path.Combine(gameData,"WIN32",file.Key.Replace('/',Path.DirectorySeparatorChar));
				Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(Path.Combine(livery.BuildRoot,"Data","WIN32",file.Key),dest,false); created.Add(dest);
			}
			var pac=Path.Combine(gameData,"WIN32","PACLOG","CUSTOMPAC","LIVERY_STUDIO",livery.Id+".PACLOG"); Directory.CreateDirectory(Path.GetDirectoryName(pac)!);
			File.Copy(Path.Combine(livery.PackageDirectory,livery.Id+".PACLOG"),pac,false); created.Add(pac);
			string Lua(string s)=>s.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r","\\r").Replace("\n","\\n");
			var title=new string(livery.Project.Name.Where(c=>c>=32 && c<127).ToArray()).ToUpperInvariant();
			var key="WK_"+title+" "+livery.Id[5..11].ToUpperInvariant();
			var block=$"\r\n{marker}\r\nSkins[\"{Lua(key)}\"] = {{\r\n\tName = -1,\r\n\tResourceBody = \"{livery.BodyResource}\",\r\n\tResourceGlass = \"DATA:/{glass}\",\r\n\tPACLog = \"{livery.PaclogResource}\",\r\n\tCarType = \"{car.EngineType}\",\r\n\tTexture = \"MENU_GARAGE_SKINS_WORKSHOP\",\r\n\tTexturePack = \"DATA:/GUI_FLATOUT/TEXTURES/MENUS/LIVERIES_1.PSK\",\r\n\tAvailable = true,\r\n}}\r\n-- LIVERY_STUDIO_END {livery.Id}\r\n";
			var rewardBlock=$"\r\n{marker}\r\ntable.insert(Rewards.CarSkins, {{ Id = \"{Lua(key)}\" }})\r\n-- LIVERY_STUDIO_END {livery.Id}\r\n";
			// Refuse to overwrite changes made by another writer while files were staged.
			if(File.ReadAllText(factory)!=original||File.ReadAllText(rewards)!=originalRewards)throw new IOException("Game settings changed during import; retry after closing the other editor.");
			var rewardsTemp=rewards+"."+livery.Id+".tmp"; File.WriteAllText(rewardsTemp,originalRewards+rewardBlock,new UTF8Encoding(false)); File.Move(rewardsTemp,rewards,true);rewardsCommitted=originalRewards+rewardBlock;
			var temp=factory+"."+livery.Id+".tmp"; File.WriteAllText(temp,original+block,new UTF8Encoding(false)); File.Move(temp,factory,true);committed=original+block;
			var receipt=new ImportReceipt(livery.Project.Name,gameData,key,backup,created.ToArray(),Hash(factory),rewardsBackup,Hash(rewards),created.ToDictionary(p=>p,Hash,StringComparer.OrdinalIgnoreCase),block,rewardBlock);
			File.WriteAllText(Path.Combine(backups,"receipt.json"),JsonSerializer.Serialize(receipt,Contract.Json)); return receipt;
		}
		catch
		{
			void Restore(string path,string before,string? after){if(after is not null&&File.ReadAllText(path)==after){var temp=path+".rollback."+Guid.NewGuid().ToString("N");File.WriteAllText(temp,before,new UTF8Encoding(false));File.Move(temp,path,true);}}
			Restore(factory,original,committed);Restore(rewards,originalRewards,rewardsCommitted);
			foreach(var file in created)File.Delete(file); throw;
		}
	}
}
