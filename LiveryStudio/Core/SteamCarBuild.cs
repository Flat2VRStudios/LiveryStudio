using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace LiveryStudio.Core;

public sealed record SteamCarBuild(string Id,string Name,string GameRoot,string Package,string PackageHash,string Paclog,string PaclogHash,string OriginalPaclogHash,string ExecutableHash,string ArchiveIndexHash,Dictionary<string,string> Assets);

public sealed partial class VehicleAssemblyService
{
    public async Task<SteamCarBuild> BuildSteamAsync(VehicleAssemblyProject project,WorkshopService paintService,string gameRoot,CancellationToken cancellation=default)
    {
        var game=GameInstallationDiscovery.Inspect(gameRoot);
        if(game is not {Kind:GameInstallationKind.Steam}||project.DonorIndex!=0||!project.Paint.IndependentPaintSides)throw new InvalidOperationException("Steam car installation currently supports Grosse pointe with independent sides.");
        project.Validate();if(!project.Prepared)throw new InvalidOperationException("Update the car preview before building.");
        var snapshot=JsonSerializer.Deserialize<VehicleAssemblyProject>(JsonSerializer.Serialize(project,Contract.Json),Contract.Json)!;var scene=await PrepareAsync(snapshot,cancellation);var donor=Donor(0);
        var stock=new GamePackage(Path.Combine(game.Root,SteamCarInstall.StockPackage));
        // Check the actual retail assets, including their CRCs. No developer
        // prefab, factory settings or scene instance is installed into Steam.
        var stockModel=stock.Read(SteamCarInstall.Model);stock.Read(SteamCarInstall.Material);stock.Read("WIN32/"+donor.BodyResource[6..]);
        var originalPaclog=SteamCarInstall.ReadBaselinePaclog(Workspace,game.Root);
        var id="STEAM_"+Guid.NewGuid().ToString("N");var root=Job();var data=Path.Combine(root,"Data");var relative="GRAPH/CARS/DERBY/BUICK";var source=Path.Combine(data,"Assets",relative);Directory.CreateDirectory(source);
        foreach(var path in new[]{"WIN32","Settings","COMMON/ENGINE/ARCHETYPES","COMMON/ARCHETYPES"})Directory.CreateDirectory(Path.Combine(data,path));
        foreach(var setting in Directory.EnumerateFiles(Path.Combine(RuntimeRoot,"Settings"),"*.XML"))File.Copy(setting,Path.Combine(data,"Settings",Path.GetFileName(setting)));
        File.Copy(snapshot.AssemblyFile,Path.Combine(source,"BUICK.FBX"));var p3d=XDocument.Load(Path.Combine(DonorFolder(donor),"Body.P3D.XML"));p3d.Descendants("p3d").Single().SetAttributeValue("path","SOURCES:/"+relative+"/BUICK.FBX");p3d.Save(Path.Combine(source,"BUICK.P3D.XML"));
        Progress?.Invoke(15,"Compiling independent paint for Steam…");await Run("Compiler_r.exe",new[]{"/Input","ASSETS:/"+relative+"/BUICK.P3D.XML","/Platform","PC","/ProjectPath",root},cancellation);
        var read=await Run("VehicleSceneProbe.exe",new[]{Path.Combine(data,"WIN32"),relative+"/BUICK.P3D"},cancellation);scene.ValidateCompiled(read);
        // Both scene layouts must expose the same drawable sections: the retail
        // PSI, breakables, handling and collision registrations remain intact.
        var stockFile=Path.Combine(root,"Stock/WIN32",relative,"BUICK.P3D");Directory.CreateDirectory(Path.GetDirectoryName(stockFile)!);File.WriteAllBytes(stockFile,stockModel);
        var oldRead=await Run("VehicleSceneProbe.exe",new[]{Path.Combine(root,"Stock/WIN32"),relative+"/BUICK.P3D"},cancellation);ValidateSteamLayout(oldRead,read);
        snapshot.Paint.FlatExteriorNormals=false;var paint=await paintService.BuildAsync(snapshot.Paint,cancellation,Path.Combine(RuntimeRoot,"PaintSides.PFX"));
        var files=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){[SteamCarInstall.Model]=Path.Combine(data,"WIN32",relative,"BUICK.P3D")};
        foreach(var asset in paint.Hashes){var target=asset.Key.EndsWith(".PMA",StringComparison.OrdinalIgnoreCase)?SteamCarInstall.Material:"WIN32/"+asset.Key;files.Add(target,Path.Combine(paint.BuildRoot,"Data/WIN32",asset.Key));}
        var paclog=Path.Combine(root,"BUICK_01.PACLOG");File.WriteAllBytes(paclog,SteamCarInstall.LoadingList(originalPaclog,files.Keys));
        var package=Path.Combine(root,id+".PKG");GamePackage.Write(package,files);var verify=new GamePackage(package);
        foreach(var file in files)if(!verify.Read(file.Key,128*1024*1024).AsSpan().SequenceEqual(File.ReadAllBytes(file.Value)))throw new InvalidDataException("The Steam car package failed verification.");
        var packageCheck=Path.Combine(root,"PackageCheck");Directory.CreateDirectory(packageCheck);
        var packageRead=await Run("VehicleSceneProbe.exe",new[]{packageCheck,SteamCarInstall.Model,package},cancellation);
        scene.ValidateCompiled(packageRead);ValidateSteamLayout(oldRead,packageRead);
        cancellation.ThrowIfCancellationRequested();var result=new SteamCarBuild(id,project.Name,game.Root,package,WorkshopService.Hash(package),paclog,WorkshopService.Hash(paclog),Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(originalPaclog)),WorkshopService.Hash(game.Executable),stock.IndexHash,files.ToDictionary(p=>GamePackage.Normalize(p.Key),p=>WorkshopService.Hash(p.Value)));
        File.WriteAllText(Path.Combine(root,"steam-build.json"),JsonSerializer.Serialize(result,Contract.Json));Progress?.Invoke(100,"Steam car ready");return result;
    }
    internal static void ValidateSteamLayout(string stock,string corrected)
    {
        static Dictionary<string,JsonElement> Nodes(string json){using var d=JsonDocument.Parse(json);return d.RootElement.GetProperty("nodes").EnumerateArray().ToDictionary(n=>n.GetProperty("name").GetString()!,n=>n.Clone(),StringComparer.Ordinal);}
        var old=Nodes(stock);var current=Nodes(corrected);
        if(!old.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(current.Keys))throw new InvalidDataException("The corrected model does not match this Steam car's hierarchy.");
        foreach(var (name,node) in old)if(node.GetProperty("mesh").GetBoolean()){
            var next=current[name];if(!next.GetProperty("mesh").GetBoolean()||next.GetProperty("sections").GetInt32()!=node.GetProperty("sections").GetInt32()||next.GetProperty("lods").GetInt32()!=node.GetProperty("lods").GetInt32())throw new InvalidDataException("The corrected model changed the Steam car's render sections or LODs: "+name);
        }
    }
}
