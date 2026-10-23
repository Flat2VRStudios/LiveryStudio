using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LiveryStudio.Core;
public sealed record ManagedImport(string Id,string ReceiptPath,ImportReceipt Receipt,string Status,DateTimeOffset Imported)
{
	public string Display=>Receipt.Name+" · "+Status;
	public override string ToString()=>Display;
}

public sealed class ImportManager(WorkshopService service)
{
	private string Root=>Path.GetFullPath(Path.Combine(service.Workspace,"imports"));
	public IReadOnlyList<ManagedImport> List(string gameData)
	{
		if(!Directory.Exists(Root)||string.IsNullOrWhiteSpace(gameData))return [];
		var results=new List<ManagedImport>();
		foreach(var path in Directory.EnumerateFiles(Root,"receipt.json",SearchOption.AllDirectories))
		{
			try
			{
				var entry=Read(path,gameData);var status=entry.Receipt.RemovedAt is not null?"Removed":Status(entry);
				results.Add(entry with {Status=status});
			}
			catch(InvalidDataException){}catch(JsonException){}catch(FileNotFoundException){}
		}
		return results.OrderByDescending(i=>i.Imported).ToArray();
	}
	public ManagedImport Read(string receiptPath,string gameData)
	{
		var full=Path.GetFullPath(receiptPath);if(!full.StartsWith(Root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||Path.GetFileName(full)!="receipt.json")throw new InvalidDataException("Select an import receipt from this app's workspace.");
		var id=Directory.GetParent(full)?.Parent?.Name??"";if(!Regex.IsMatch(id,"^BODY_[0-9a-f]{32}$"))throw new InvalidDataException("Invalid import receipt identity.");
		if(new FileInfo(full).Length>128*1024)throw new InvalidDataException("Invalid import receipt size.");
		var receipt=JsonSerializer.Deserialize<ImportReceipt>(File.ReadAllText(full),Contract.Json)??throw new InvalidDataException("Invalid import receipt.");
		var data=PathIdentity.Resolve(gameData);if(!PathIdentity.Resolve(receipt.GameData).Equals(data,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("This receipt belongs to another game folder.");
		receipt=receipt with {GameData=data,InstalledFiles=receipt.InstalledFiles.Select(PathIdentity.Resolve).ToArray(),InstalledHashes=receipt.InstalledHashes?.ToDictionary(k=>PathIdentity.Resolve(k.Key),v=>v.Value,StringComparer.OrdinalIgnoreCase)};
		if(receipt.InstalledFiles.Length!=4||receipt.InstalledFiles.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=4)throw new InvalidDataException("The import receipt must own exactly three livery assets and one PACLOG.");
		var owned=Contract.Cars.SelectMany(car=>{var prefix=Path.Combine(data,"WIN32","GRAPH","CARS",car.Class,car.CarId);return new[]{Path.Combine(prefix,id+".PMA"),Path.Combine(prefix,"TEXTURES",id+"_DIFF.PTX"),Path.Combine(prefix,"TEXTURES",id+"_IOR.PTX")};}).Append(Path.Combine(data,"WIN32","PACLOG","CUSTOMPAC","LIVERY_STUDIO",id+".PACLOG")).ToHashSet(StringComparer.OrdinalIgnoreCase);
		foreach(var path in receipt.InstalledFiles)
		{
			var native=Path.GetFullPath(path);var prefix=Path.Combine(data,"WIN32")+Path.DirectorySeparatorChar;
			if(!native.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||!owned.Contains(native))throw new InvalidDataException("The receipt contains a file outside this livery.");
		}
		return new(id,full,receipt,"",File.GetLastWriteTimeUtc(full));
	}
	private Dictionary<string,string> ExpectedHashes(ManagedImport entry)
	{
		if(entry.Receipt.InstalledHashes is {} hashes)return hashes;
		var buildFile=Path.Combine(service.Workspace,"builds",entry.Id,"livery.json");if(!File.Exists(buildFile))throw new InvalidDataException("This older import needs its saved build before the app can verify and remove it.");
		var built=JsonSerializer.Deserialize<BuiltLivery>(File.ReadAllText(buildFile),Contract.Json)??throw new InvalidDataException("The saved build is invalid.");
		var result=built.Hashes.ToDictionary(k=>PathIdentity.Resolve(Path.Combine(entry.Receipt.GameData,"WIN32",k.Key)),v=>v.Value,StringComparer.OrdinalIgnoreCase);
		var pac=entry.Receipt.InstalledFiles.Single(f=>f.EndsWith(".PACLOG",StringComparison.OrdinalIgnoreCase));result[pac]=WorkshopService.Hash(Path.Combine(built.PackageDirectory,entry.Id+".PACLOG"));return result;
	}
	private string Status(ManagedImport entry)
	{
		try
		{
			var hashes=ExpectedHashes(entry);if(entry.Receipt.InstalledFiles.Any(f=>!File.Exists(f)))return "Files missing";
			if(entry.Receipt.InstalledFiles.Any(f=>!hashes.TryGetValue(f,out var expected)||WorkshopService.Hash(f)!=expected))return "Files changed";
			var factory=Settings(entry.Receipt.GameData,"CarsSkins.cfg");if(!File.ReadAllText(factory).Contains("-- LIVERY_STUDIO_BEGIN "+entry.Id))return "Registration missing";return "Installed";
		}
		catch(IOException){return "Unavailable";}catch(InvalidDataException){return "Needs saved build";}
	}
	private static string Settings(string data,string name)=>Path.Combine(data,"Common","Tuning","Factories",name);
	private static (string Text,string Block) RemoveBlock(string original,string id,string? expected)
	{
		var expression=@"(?m)^-- LIVERY_STUDIO_BEGIN "+Regex.Escape(id)+@"\r?\n[\s\S]*?^-- LIVERY_STUDIO_END "+Regex.Escape(id)+@"\r?\n?";
		var matches=Regex.Matches(original,expression);if(matches.Count!=1)throw new InvalidDataException("The livery registration is missing or duplicated. Game settings were left unchanged.");
		var block=matches[0].Value;if(expected is not null&&block.Trim()!=expected.Trim())throw new InvalidDataException("This livery's registration was edited outside the app. Review it before removal.");
		return (original.Remove(matches[0].Index,matches[0].Length),block);
	}
	public void ValidateRemoval(ManagedImport item,string gameData)
	{
		var entry=Read(item.ReceiptPath,gameData);if(entry.Receipt.RemovedAt is not null)throw new InvalidOperationException("This import is already removed.");
		var hashes=ExpectedHashes(entry);
		foreach(var path in entry.Receipt.InstalledFiles)if(!File.Exists(path)||!hashes.TryGetValue(path,out var hash)||WorkshopService.Hash(path)!=hash)throw new InvalidDataException("An installed livery file is missing or changed. No game files were removed.");
		_ = RemoveBlock(File.ReadAllText(Settings(gameData,"CarsSkins.cfg")),entry.Id,entry.Receipt.FactoryBlock);
		_ = RemoveBlock(File.ReadAllText(Settings(gameData,"Rewards.cfg")),entry.Id,entry.Receipt.RewardsBlock);
	}
	public void Remove(ManagedImport item,string gameData,string? replacedBy=null)
	{
		using var gate=GameImportLock.Enter(gameData);ValidateRemoval(item,gameData);var entry=Read(item.ReceiptPath,gameData);
		var factory=Settings(gameData,"CarsSkins.cfg");var rewards=Settings(gameData,"Rewards.cfg");var before=File.ReadAllText(factory);var beforeRewards=File.ReadAllText(rewards);
		var after=RemoveBlock(before,entry.Id,entry.Receipt.FactoryBlock).Text;var afterRewards=RemoveBlock(beforeRewards,entry.Id,entry.Receipt.RewardsBlock).Text;
		var archive=Path.Combine(Path.GetDirectoryName(entry.ReceiptPath)!,"removed",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(archive);
		File.WriteAllText(Path.Combine(archive,"CarsSkins.cfg.before"),before);File.WriteAllText(Path.Combine(archive,"Rewards.cfg.before"),beforeRewards);
		var moved=new List<(string From,string To)>();var factoryWritten=false;var rewardsWritten=false;
		try
		{
			if(File.ReadAllText(factory)!=before||File.ReadAllText(rewards)!=beforeRewards)throw new IOException("Game settings changed during removal. Retry after closing the other editor.");
			foreach(var path in entry.Receipt.InstalledFiles){var relative=Path.GetRelativePath(Path.Combine(entry.Receipt.GameData,"WIN32"),path);var target=Path.Combine(archive,"files",relative);Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Move(path,target,false);moved.Add((path,target));}
			if(File.ReadAllText(factory)!=before||File.ReadAllText(rewards)!=beforeRewards)throw new IOException("Game settings changed while files were archived.");
			AtomicFiles.Write(rewards,Encoding.UTF8.GetBytes(afterRewards));rewardsWritten=true;
			if(File.ReadAllText(factory)!=before)throw new IOException("Skin settings changed while removal was committed.");
			AtomicFiles.Write(factory,Encoding.UTF8.GetBytes(after));factoryWritten=true;
			var removed=entry.Receipt with {RemovedAt=DateTimeOffset.UtcNow,ReplacedBy=replacedBy};AtomicFiles.Write(entry.ReceiptPath,JsonSerializer.SerializeToUtf8Bytes(removed,Contract.Json),keepPrevious:true);
		}
		catch
		{
			if(factoryWritten&&File.ReadAllText(factory)==after)AtomicFiles.Write(factory,Encoding.UTF8.GetBytes(before));
			if(rewardsWritten&&File.ReadAllText(rewards)==afterRewards)AtomicFiles.Write(rewards,Encoding.UTF8.GetBytes(beforeRewards));
			foreach(var move in moved.AsEnumerable().Reverse())if(!File.Exists(move.From)&&File.Exists(move.To))File.Move(move.To,move.From,false);throw;
		}
	}
	public ImportReceipt Replace(ManagedImport old,BuiltLivery replacement,string gameData)
	{
		using var gate=GameImportLock.Enter(gameData);ValidateRemoval(old,gameData);WorkshopService.ValidateBuild(replacement);
		var imported=service.Import(replacement,gameData);
		try{Remove(old,gameData,replacement.Id);return imported;}
		catch
		{
			var added=List(gameData).Single(i=>i.Id==replacement.Id&&i.Status=="Installed");Remove(added,gameData);throw;
		}
	}
}

internal sealed class GameImportLock:IDisposable
{
	private readonly Mutex mutex;
	private GameImportLock(Mutex mutex){this.mutex=mutex;}
	public static GameImportLock Enter(string gameData)
	{
		var key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(PathIdentity.Resolve(gameData).ToUpperInvariant())))[..24];var mutex=new Mutex(false,"Local\\LiveryStudio-"+key);
		try{if(!mutex.WaitOne(TimeSpan.FromSeconds(3))){mutex.Dispose();throw new IOException("Another Livery Studio window is updating this game. Try again in a moment.");}}catch(AbandonedMutexException){}
		return new(mutex);
	}
	public void Dispose(){mutex.ReleaseMutex();mutex.Dispose();}
}
