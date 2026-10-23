using System.Text.Json;
using LiveryStudio.Core;

internal static class ImportChecks
{
	public static void Run(string temp,Action<bool,string> check,Action<Action,string> reject)
	{
		var folder=Path.Combine(temp,"managed-import");var game=Path.Combine(folder,"game");var factory=Path.Combine(game,"Common/Tuning/Factories/CarsSkins.cfg");var rewards=Path.Combine(Path.GetDirectoryName(factory)!,"Rewards.cfg");Directory.CreateDirectory(Path.GetDirectoryName(factory)!);File.WriteAllText(factory,"Skins = {}\r\n");File.WriteAllText(rewards,"Rewards = { CarSkins = {} }\r\n");var glass=Path.Combine(game,"WIN32/GRAPH/CARS/COMMON/GLASS.PMA");Directory.CreateDirectory(Path.GetDirectoryName(glass)!);File.WriteAllText(glass,"glass");
		var service=new WorkshopService(WorkshopService.DefaultTools,Path.Combine(folder,"workspace"));var manager=new ImportManager(service);
		BuiltLivery Build(string name)
		{
			var id="BODY_"+Guid.NewGuid().ToString("N");var root=Path.Combine(folder,id);var prefix="GRAPH/CARS/DERBY/BUICK/";var paths=new[]{prefix+id+".PMA",prefix+"TEXTURES/"+id+"_DIFF.PTX",prefix+"TEXTURES/"+id+"_IOR.PTX"};var hashes=new Dictionary<string,string>();foreach(var relative in paths){var native=Path.Combine(root,"Data/WIN32",relative);Directory.CreateDirectory(Path.GetDirectoryName(native)!);File.WriteAllText(native,relative);hashes[relative]=WorkshopService.Hash(native);}var pkg=Path.Combine(root,"Workshop");Directory.CreateDirectory(Path.Combine(pkg,id));File.WriteAllText(Path.Combine(pkg,id+".PACLOG"),"assets");File.WriteAllText(Path.Combine(pkg,id,id+".PKG"),"package");return new(id,new(){Name=name,CarIndex=0},root,pkg,"DATA:/"+paths[0],"DATA:/PACLOG/CUSTOMPAC/LIVERY_STUDIO/"+id+".PACLOG",hashes);
		}
		var first=Build("First design");var second=Build("Later design");service.Import(first,game);service.Import(second,game);File.AppendAllText(factory,"-- custom setting added later\r\n");File.AppendAllText(rewards,"-- later unlock edit\r\n");
		var firstReceipt=manager.List(game).Single(e=>e.Id==first.Id);File.WriteAllText(firstReceipt.ReceiptPath,JsonSerializer.Serialize(firstReceipt.Receipt with {InstalledHashes=null},Contract.Json));var legacyBuild=Path.Combine(service.Workspace,"builds",first.Id,"livery.json");Directory.CreateDirectory(Path.GetDirectoryName(legacyBuild)!);File.WriteAllText(legacyBuild,JsonSerializer.Serialize(first,Contract.Json));
		var entries=manager.List(game);check(entries.Single(e=>e.Id==first.Id).Status=="Installed","Legacy receipts verify saved build paths containing native forward-slash resource names");check(entries.Count==2&&entries.All(e=>e.Status=="Installed"),"Import manager identifies verified installations independently of later settings edits");
		manager.Remove(entries.Single(e=>e.Id==first.Id),game);check(File.ReadAllText(factory).Contains(second.Id)&&File.ReadAllText(factory).Contains("custom setting added later")&&!File.ReadAllText(factory).Contains(first.Id),"Removing one import preserves later liveries and unrelated skin settings");
		check(File.ReadAllText(rewards).Contains(second.Id)&&File.ReadAllText(rewards).Contains("later unlock edit")&&!File.ReadAllText(rewards).Contains(first.Id),"Import removal preserves later unlock settings");
		check(manager.List(game).Single(e=>e.Id==first.Id).Status=="Removed"&&Directory.EnumerateFiles(service.Workspace,"*.PMA",SearchOption.AllDirectories).Any(),"Removed native files remain recoverable in the receipt archive");
		var replacement=Build("Updated design");manager.Replace(manager.List(game).Single(e=>e.Id==second.Id),replacement,game);check(manager.List(game).Single(e=>e.Id==replacement.Id).Status=="Installed"&&manager.List(game).Single(e=>e.Id==second.Id).Receipt.ReplacedBy==replacement.Id,"Updating an import installs the replacement and records its archived predecessor");
		var current=manager.List(game).Single(e=>e.Id==replacement.Id);var altered=current.Receipt.InstalledFiles[0];File.AppendAllText(altered,"manual edit");var before=File.ReadAllText(factory);reject(()=>manager.Remove(current,game),"Refuse to remove externally changed native files");check(File.ReadAllText(factory)==before&&File.Exists(altered),"Rejected removal preserves game settings and changed native content");
		reject(()=>manager.Read(current.ReceiptPath,Path.Combine(folder,"other-game")),"Import receipts cannot target a different connected game folder");
		var suffix=current.Receipt with {InstalledFiles=[..current.Receipt.InstalledFiles.Take(3),Path.Combine(game,"WIN32","GRAPH","CARS","DERBY","BUICK","TEXTURES",current.Id+"_UNOWNED.PTX")]};File.WriteAllText(current.ReceiptPath,JsonSerializer.Serialize(suffix,Contract.Json));reject(()=>manager.Read(current.ReceiptPath,game),"Reject foreign assets even when their names share this livery's GUID prefix");
		var foreign=current.Receipt with {InstalledFiles=[Path.Combine(folder,"outside.PMA"),..current.Receipt.InstalledFiles.Skip(1)]};File.WriteAllText(current.ReceiptPath,JsonSerializer.Serialize(foreign,Contract.Json));reject(()=>manager.Read(current.ReceiptPath,game),"Reject import manifests that refer outside the game's native directory");
	}
}
