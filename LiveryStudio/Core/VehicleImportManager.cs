using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LiveryStudio.Core;

public sealed record VehicleInstalledFile(string Relative,string Hash,string? BeforeHash,string Backup,string Block="");
public sealed record VehicleImportReceipt(int Version,string Id,int DonorIndex,string Name,string GameData,string State,string PaintId,VehicleInstalledFile[] Files,DateTimeOffset Created);
public sealed record ManagedVehicle(string ReceiptPath,VehicleImportReceipt Receipt);

// One durable journal owns the geometry, prefab, paint and registration writes.
// It is saved before the first game mutation and can resume an interrupted restore.
public sealed class VehicleImportManager(VehicleAssemblyService assembly)
{
    private const string Skins="Common/Tuning/Factories/CarsSkins.cfg",Rewards="Common/Tuning/Factories/Rewards.cfg";
    private string Root=>Path.Combine(assembly.Workspace,"vehicle-imports");
    private static string Normal(string path)=>path.Replace('\\','/');
    private static string Target(string data,string relative)
    {
        if(Path.IsPathRooted(relative)||relative.Split('/','\\').Any(p=>p is ".." or "." or ""))throw new InvalidDataException("Invalid vehicle file path.");
        var root=PathIdentity.Resolve(data);var path=PathIdentity.Resolve(Path.Combine(root,relative));if(!path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The vehicle path leaves the selected game.");return path;
    }
    private HashSet<string> Allowed(string id,int index,string paintId,bool independent=false)
    {
        if(!Regex.IsMatch(id,"^CAR_[0-9a-f]{32}$")||!Regex.IsMatch(paintId,"^BODY_[0-9a-f]{32}$"))throw new InvalidDataException("Invalid vehicle identity.");
        var donor=assembly.Donor(index);var car=Contract.Cars.Single(c=>c.Index==index);var prefix="GRAPH/CARS/"+car.Class+"/"+car.CarId+"/";
        if(donor.Class!=car.Class||donor.CarId!=car.CarId||!Normal(donor.BodyResource).StartsWith("DATA:/"+prefix,StringComparison.OrdinalIgnoreCase)||!Normal(donor.PrefabPath).StartsWith("Common/Archetypes/GAMEPLAY/CARS/"+car.Class+"/"+car.CarId+"/",StringComparison.OrdinalIgnoreCase)||!Normal(donor.PrefabPath).EndsWith(".PPF",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The donor paths do not match the selected car.");
        var allowed=new HashSet<string>(new[]{"WIN32/GRAPH/CARS/LIVERY_STUDIO/"+id[4..20]+"/BODY.P3D","WIN32/"+Normal(donor.BodyResource)[6..],Normal(donor.PrefabPath),"Common/TUNING/LIVERY_STUDIO/"+id+"/BREAKABLES.CFG","WIN32/"+prefix+paintId+".PMA","WIN32/"+prefix+"TEXTURES/"+paintId+"_DIFF.PTX","WIN32/"+prefix+"TEXTURES/"+paintId+"_IOR.PTX","WIN32/PACLOG/CUSTOMPAC/LIVERY_STUDIO/"+paintId+".PACLOG",Skins,Rewards},StringComparer.OrdinalIgnoreCase);
        if(independent)allowed.Add("WIN32/"+prefix+paintId+"_SIDES.PFX");return allowed;
    }
    private static bool Config(string relative)=>relative.Equals(Skins,StringComparison.OrdinalIgnoreCase)||relative.Equals(Rewards,StringComparison.OrdinalIgnoreCase);
    private static void NotRunning(string gameData)
    {
        var target=DesktopGame.Resolve(gameData);var running=DesktopGame.Running(target);try{if(running.Count>0)throw new InvalidOperationException("Close the non-VR game before changing its car files.");}finally{foreach(var process in running)process.Dispose();}
    }
    public IReadOnlyList<ManagedVehicle> List(string gameData)
    {
        if(!Directory.Exists(Root))return [];var result=new List<ManagedVehicle>();foreach(var file in Directory.EnumerateFiles(Root,"receipt.json",SearchOption.AllDirectories))try{result.Add(Read(file,gameData));}catch(InvalidDataException){}catch(JsonException){}return result.OrderByDescending(r=>r.Receipt.Created).ToArray();
    }
    public ManagedVehicle Read(string file,string gameData)
    {
        var full=PathIdentity.Resolve(file);var owned=PathIdentity.Resolve(Root);if(!full.StartsWith(owned+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||Path.GetFileName(full)!="receipt.json"||new FileInfo(full).Length>128*1024)throw new InvalidDataException("Choose a receipt owned by Car Builder.");
        var receipt=JsonSerializer.Deserialize<VehicleImportReceipt>(File.ReadAllText(full),Contract.Json)??throw new InvalidDataException("Invalid car receipt.");var allowed=Allowed(receipt.Id,receipt.DonorIndex,receipt.PaintId,receipt.Version==3);
        if(receipt.Version is not (2 or 3)||Directory.GetParent(full)?.Name!=receipt.Id||!PathIdentity.Resolve(receipt.GameData).Equals(PathIdentity.Resolve(gameData),StringComparison.OrdinalIgnoreCase)||receipt.Files.Length!=allowed.Count||!allowed.SetEquals(receipt.Files.Select(f=>Normal(f.Relative))))throw new InvalidDataException("Invalid vehicle receipt ownership.");
        for(var i=0;i<receipt.Files.Length;i++)
        {
            var f=receipt.Files[i];if(!Regex.IsMatch(f.Hash,"^[0-9A-F]{64}$")||f.BeforeHash is not null&&(!Regex.IsMatch(f.BeforeHash,"^[0-9A-F]{64}$")||f.Backup!=i+".before"))throw new InvalidDataException("Invalid vehicle receipt file.");_=Target(gameData,f.Relative);
            if(Config(f.Relative)){if(f.BeforeHash is null||f.Block.Length>16000||!f.Block.StartsWith("\r\n-- LIVERY_STUDIO_BEGIN "+receipt.PaintId+"\r\n",StringComparison.Ordinal)||!f.Block.EndsWith("-- LIVERY_STUDIO_END "+receipt.PaintId+"\r\n",StringComparison.Ordinal))throw new InvalidDataException("Invalid car paint registration.");}else if(f.Block.Length>0)throw new InvalidDataException("Unexpected registration block.");
            if(f.BeforeHash is not null){var backup=Path.Combine(Path.GetDirectoryName(full)!,f.Backup);if(!File.Exists(backup)||WorkshopService.Hash(backup)!=f.BeforeHash)throw new InvalidDataException("A donor recovery copy is missing or changed.");}
        }
        return new(full,receipt);
    }
    private static void Save(string path,VehicleImportReceipt receipt)=>AtomicFiles.Write(path,JsonSerializer.SerializeToUtf8Bytes(receipt,Contract.Json),keepPrevious:true);
    public ManagedVehicle Install(BuiltVehicle built,string gameData)
    {
        gameData=PathIdentity.Resolve(gameData);NotRunning(gameData);using var gate=GameImportLock.Enter(gameData);var allowed=Allowed(built.Id,built.Project.DonorIndex,built.Paint.Id,built.Paint.Project.IndependentPaintSides);
        if(built.Paint.Project.CarIndex!=built.Project.DonorIndex||built.Files.Count!=4||built.Hashes.Count!=4||built.Files.Keys.Any(p=>!allowed.Contains(Normal(p))||Config(p))||built.Hashes.Keys.Any(p=>!built.Files.ContainsKey(p)))throw new InvalidDataException("The car build contains unexpected files.");
        if(List(gameData).Any(r=>r.Receipt.DonorIndex==built.Project.DonorIndex&&r.Receipt.State is not ("Removed" or "Rolled back")))throw new InvalidOperationException("Restore the current replacement for this donor before installing another.");
        var target=DesktopGame.Resolve(gameData);if(WorkshopService.Hash(target.GameDll)!=built.TargetGameDllHash)throw new InvalidDataException("The target game changed after this car was built. Build again for this version.");WorkshopService.ValidateBuild(built.Paint);
        foreach(var old in built.OriginalHashes){var file=Target(gameData,Normal(old.Key));if(!File.Exists(file)||WorkshopService.Hash(file)!=old.Value)throw new InvalidDataException("A donor file changed after the build: "+old.Key);}
        var bytes=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);foreach(var file in built.Files){if(!built.Hashes.TryGetValue(file.Key,out var hash)||!File.Exists(file.Value)||WorkshopService.Hash(file.Value)!=hash)throw new InvalidDataException("A compiled car file is missing or changed.");bytes.Add(Normal(file.Key),File.ReadAllBytes(file.Value));}
        foreach(var file in built.Paint.Hashes)bytes.Add("WIN32/"+Normal(file.Key),File.ReadAllBytes(Path.Combine(built.Paint.BuildRoot,"Data","WIN32",file.Key)));
        bytes.Add("WIN32/PACLOG/CUSTOMPAC/LIVERY_STUDIO/"+built.Paint.Id+".PACLOG",File.ReadAllBytes(Path.Combine(built.Paint.PackageDirectory,built.Paint.Id+".PACLOG")));
        var car=built.Paint.Project.SelectedCar;var glass=$"GRAPH/CARS/{car.Class}/{car.CarId}/GLASS_01.PMA";if(!File.Exists(Target(gameData,"WIN32/"+glass)))glass="GRAPH/CARS/COMMON/GLASS.PMA";if(!File.Exists(Target(gameData,"WIN32/"+glass)))throw new InvalidDataException("The donor glass material is missing.");
        string Lua(string value)=>value.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r","\\r").Replace("\n","\\n");
        var key="WK_"+new string(built.Project.Name.Where(c=>c>=32&&c<127).ToArray()).ToUpperInvariant()+" "+built.Paint.Id[5..11].ToUpperInvariant();var marker="-- LIVERY_STUDIO_BEGIN "+built.Paint.Id;var end="-- LIVERY_STUDIO_END "+built.Paint.Id;
        var blocks=new Dictionary<string,string>{[Skins]=$"\r\n{marker}\r\nSkins[\"{Lua(key)}\"] = {{\r\n\tName = -1,\r\n\tResourceBody = \"{built.Paint.BodyResource}\",\r\n\tResourceGlass = \"DATA:/{glass}\",\r\n\tPACLog = \"{built.Paint.PaclogResource}\",\r\n\tCarType = \"{car.EngineType}\",\r\n\tTexture = \"MENU_GARAGE_SKINS_WORKSHOP\",\r\n\tTexturePack = \"DATA:/GUI_FLATOUT/TEXTURES/MENUS/LIVERIES_1.PSK\",\r\n\tAvailable = true,\r\n}}\r\n{end}\r\n",[Rewards]=$"\r\n{marker}\r\ntable.insert(Rewards.CarSkins, {{ Id = \"{Lua(key)}\" }})\r\n{end}\r\n"};
        foreach(var pair in blocks){var path=Target(gameData,pair.Key);if(!File.Exists(path))throw new InvalidDataException("The selected game must expose editable car and reward settings.");var original=File.ReadAllText(path);if(original.Contains(marker,StringComparison.Ordinal))throw new InvalidOperationException("This car paint is already registered.");bytes.Add(pair.Key,new UTF8Encoding(false).GetBytes(original+pair.Value));}
        if(bytes.Count!=allowed.Count||!allowed.SetEquals(bytes.Keys))throw new InvalidDataException("Incomplete vehicle transaction.");
        foreach(var relative in bytes.Keys)if(File.Exists(Target(gameData,relative))&&!Config(relative)&&!built.OriginalHashes.Keys.Any(k=>Normal(k).Equals(relative,StringComparison.OrdinalIgnoreCase)))throw new IOException("A custom car resource already exists. No files were replaced.");
        var folder=Path.Combine(Root,built.Id);Directory.CreateDirectory(folder);var receiptPath=Path.Combine(folder,"receipt.json");if(File.Exists(receiptPath))throw new InvalidOperationException("This car build already has an installation record.");var entries=new List<VehicleInstalledFile>();
        foreach(var file in bytes){var destination=Target(gameData,file.Key);var before=File.Exists(destination)?WorkshopService.Hash(destination):null;var backup=before is null?"":entries.Count+".before";if(before is not null)AtomicFiles.Write(Path.Combine(folder,backup),File.ReadAllBytes(destination));var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file.Value));entries.Add(new(file.Key,hash,before,backup,blocks.GetValueOrDefault(file.Key,"")));}
        var receipt=new VehicleImportReceipt(built.Paint.Project.IndependentPaintSides?3:2,built.Id,built.Project.DonorIndex,built.Project.Name,gameData,"Installing",built.Paint.Id,entries.ToArray(),DateTimeOffset.UtcNow);Save(receiptPath,receipt);AtomicFiles.Write(Path.Combine(folder,"build.json"),JsonSerializer.SerializeToUtf8Bytes(built,Contract.Json));
        try
        {
            foreach(var f in receipt.Files){var destination=Target(gameData,f.Relative);var current=File.Exists(destination)?WorkshopService.Hash(destination):null;if(current!=f.BeforeHash)throw new IOException("A car file changed during installation.");AtomicFiles.Write(destination,bytes[f.Relative]);}
            receipt=receipt with{State="Installed"};Save(receiptPath,receipt);return new(receiptPath,receipt);
        }
        catch{try{Restore(new(receiptPath,receipt),gameData,rolledBack:true);}catch{Save(receiptPath,receipt with{State="Needs recovery"});}throw;}
    }
    private static string WithoutBlock(string text,VehicleInstalledFile file,string paintId)
    {
        var marker="-- LIVERY_STUDIO_BEGIN "+paintId;var end="-- LIVERY_STUDIO_END "+paintId;
        if(!text.Contains(marker,StringComparison.Ordinal)&&!text.Contains(end,StringComparison.Ordinal))return text;
        var at=text.IndexOf(file.Block,StringComparison.Ordinal);if(at<0||text.IndexOf(marker,StringComparison.Ordinal)!=at+2||text.IndexOf(marker,at+file.Block.Length,StringComparison.Ordinal)>=0||text.IndexOf(end,text.IndexOf(end,StringComparison.Ordinal)+end.Length,StringComparison.Ordinal)>=0)throw new InvalidDataException("The car's registration was edited. Review it before restoring.");return text.Remove(at,file.Block.Length);
    }
    public void Verify(ManagedVehicle item,string gameData)
    {
        var entry=Read(item.ReceiptPath,gameData);if(entry.Receipt.State!="Installed")throw new InvalidOperationException("This car is not fully installed.");foreach(var file in entry.Receipt.Files){var path=Target(gameData,file.Relative);if(!File.Exists(path))throw new InvalidDataException("An installed car file is missing: "+file.Relative);if(Config(file.Relative)){var text=File.ReadAllText(path);if(!text.Contains(file.Block,StringComparison.Ordinal))throw new InvalidDataException("The car paint registration changed.");_=WithoutBlock(text,file,entry.Receipt.PaintId);}else if(WorkshopService.Hash(path)!=file.Hash)throw new InvalidDataException("An installed car file changed: "+file.Relative);}
    }
    public void Restore(ManagedVehicle item,string gameData,bool rolledBack=false)
    {
        gameData=PathIdentity.Resolve(gameData);NotRunning(gameData);using var gate=GameImportLock.Enter(gameData);var entry=Read(item.ReceiptPath,gameData);if(entry.Receipt.State is "Removed" or "Rolled back")return;
        foreach(var f in entry.Receipt.Files){var path=Target(gameData,f.Relative);var hash=File.Exists(path)?WorkshopService.Hash(path):null;if(Config(f.Relative)){if(hash is null)throw new InvalidDataException("A game settings file is missing.");_=WithoutBlock(File.ReadAllText(path),f,entry.Receipt.PaintId);}else if(hash!=f.Hash&&hash!=f.BeforeHash)throw new InvalidDataException("A car file was edited outside the app. Review it before restoring: "+f.Relative);}
        var receipt=entry.Receipt with{State="Restoring"};Save(entry.ReceiptPath,receipt);
        foreach(var f in receipt.Files)
        {
            var path=Target(gameData,f.Relative);if(Config(f.Relative))
            {
                var hash=WorkshopService.Hash(path);if(hash==f.BeforeHash)continue;
                if(hash==f.Hash)AtomicFiles.Write(path,File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(entry.ReceiptPath)!,f.Backup)));
                else{var current=File.ReadAllText(path);var revised=WithoutBlock(current,f,receipt.PaintId);if(current!=revised)AtomicFiles.Write(path,new UTF8Encoding(false).GetBytes(revised));}
            }
            else if(f.BeforeHash is not null)AtomicFiles.Write(path,File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(entry.ReceiptPath)!,f.Backup)));
            else if(File.Exists(path)){var removed=Path.Combine(Path.GetDirectoryName(entry.ReceiptPath)!,"Removed",Path.GetFileName(path)+"."+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.GetDirectoryName(removed)!);File.Move(path,removed,false);}
        }
        Save(entry.ReceiptPath,receipt with{State=rolledBack?"Rolled back":"Removed"});
    }
}
