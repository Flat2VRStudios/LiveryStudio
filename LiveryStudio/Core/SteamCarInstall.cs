using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LiveryStudio.Core;

public sealed record SteamCarReceipt(int Version,string Id,string Name,string GameRoot,string State,string PackageHash,string PaclogHash,string OriginalPaclogHash,DateTimeOffset Installed,string? PreviousPackageHash=null,string? PreviousPaclogHash=null);

// A Steam install owns one additional package and one backed-up loading list.
// It never rewrites a stock archive, executable, factory, profile or save file.
public sealed class SteamCarInstall
{
    private readonly string workspace;
    private readonly Action<string,byte[]> publish;
    public SteamCarInstall(string workspace):this(workspace,(p,b)=>AtomicFiles.Write(p,b)){}
    internal SteamCarInstall(string workspace,Action<string,byte[]> publish){this.workspace=Path.GetFullPath(workspace);this.publish=publish;}
    public const string StockPackage="WIN32/PKG/CHUNK_0/CHUNK_0.PKG";
    public const string Package="WIN32/PKG/CHUNK_0/ZZ_LIVERY_STUDIO_BUICK.PKG";
    public const string Paclog="WIN32/PACLOG/CUSTOMPAC/DERBY/BUICK_01.PACLOG";
    public const string Model="WIN32/GRAPH/CARS/DERBY/BUICK/BUICK.P3D";
    public const string Material="WIN32/GRAPH/CARS/DERBY/BUICK/BODY_01.PMA";
    private string Root(string game)=>Path.Combine(workspace,"steam-car-imports",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(PathIdentity.Resolve(game).ToUpperInvariant())))[..24]);
    private string CurrentFile(string game)=>Path.Combine(Root(game),"current.json");
    private string Folder(SteamCarReceipt receipt)=>Path.Combine(Root(receipt.GameRoot),receipt.Id);
    private static string Target(string game,string relative)
    {
        var root=PathIdentity.Resolve(game);var file=PathIdentity.Resolve(Path.Combine(root,relative));if(!file.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("A game file leaves its installation folder.");return file;
    }
    private static bool IsHash(string value)=>Regex.IsMatch(value,"^[0-9A-F]{64}$");
    public SteamCarReceipt? Current(string game)
    {
        var file=CurrentFile(game);if(!File.Exists(file))return null;if(new FileInfo(file).Length>16*1024)throw new InvalidDataException("Invalid Steam car receipt.");
        var receipt=JsonSerializer.Deserialize<SteamCarReceipt>(File.ReadAllText(file),Contract.Json)??throw new InvalidDataException("Invalid Steam car receipt.");
        if(receipt.Version!=1||!Regex.IsMatch(receipt.Id,"^STEAM_[0-9a-f]{32}$")||!PathIdentity.Resolve(receipt.GameRoot).Equals(PathIdentity.Resolve(game),StringComparison.OrdinalIgnoreCase)||!IsHash(receipt.PackageHash)||!IsHash(receipt.PaclogHash)||!IsHash(receipt.OriginalPaclogHash)||receipt.PreviousPackageHash is {} previousPackage&&!IsHash(previousPackage)||receipt.PreviousPaclogHash is {} previousPaclog&&!IsHash(previousPaclog)||receipt.State is not ("Installing" or "Installed" or "Restoring" or "Removed"))throw new InvalidDataException("Invalid Steam car ownership.");
        var baseline=Path.Combine(Folder(receipt),"original.PACLOG");if(!File.Exists(baseline)||WorkshopService.Hash(baseline)!=receipt.OriginalPaclogHash)throw new InvalidDataException("The original Steam livery loading list is missing or changed.");return receipt;
    }
    public static byte[] ReadBaselinePaclog(string workspace,string game)
    {
        var installer=new SteamCarInstall(workspace);var current=installer.Current(game);
        if(current is {State:"Installed"}){installer.Verify(current);return File.ReadAllBytes(Path.Combine(installer.Folder(current),"original.PACLOG"));}
        if(current is {State:"Restoring" or "Installing"})throw new InvalidOperationException("Restore the interrupted Steam car installation before building another.");
        var file=Target(game,Paclog);if(!File.Exists(file)||new FileInfo(file).Length>1024*1024)throw new InvalidDataException("This Steam installation does not expose the expected livery loading list.");return File.ReadAllBytes(file);
    }
    public void Verify(SteamCarReceipt receipt)
    {
        var current=Current(receipt.GameRoot);if(current is null||current.Id!=receipt.Id||current.State!="Installed")throw new InvalidOperationException("This Steam car is not currently installed.");
        foreach(var (relative,hash) in new[]{(Package,current.PackageHash),(Paclog,current.PaclogHash)}){var file=Target(current.GameRoot,relative);if(!File.Exists(file)||WorkshopService.Hash(file)!=hash)throw new InvalidDataException("An installed Steam car file changed. Restore or review it before continuing.");}
    }
    public static void RequireClosed(string game)
    {
        var expected=PathIdentity.Resolve(Path.Combine(game,"Flatout.exe"));
        foreach(var process in Process.GetProcessesByName("Flatout"))using(process){try{if(process.MainModule?.FileName is {} path&&PathIdentity.Resolve(path).Equals(expected,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Close FlatOut 4 before installing or restoring the car.");}catch(System.ComponentModel.Win32Exception){throw new InvalidOperationException("Close FlatOut 4 before changing its car files.");}}
    }
    public SteamCarReceipt Install(SteamCarBuild build)
    {
        var game=GameInstallationDiscovery.Inspect(build.GameRoot)??throw new InvalidDataException("The Steam game installation is missing.");
        if(game.Kind!=GameInstallationKind.Steam||!Regex.IsMatch(build.Id,"^STEAM_[0-9a-f]{32}$"))throw new InvalidDataException("Invalid Steam car build.");
        RequireClosed(game.Root);using var gate=GameImportLock.Enter(game.Root);
        if(WorkshopService.Hash(game.Executable)!=build.ExecutableHash||new GamePackage(Target(game.Root,StockPackage)).IndexHash!=build.ArchiveIndexHash)throw new InvalidDataException("Steam updated the game after this car was built. Build it again.");
        if(!File.Exists(build.Package)||WorkshopService.Hash(build.Package)!=build.PackageHash||!File.Exists(build.Paclog)||WorkshopService.Hash(build.Paclog)!=build.PaclogHash)throw new InvalidDataException("The built Steam car changed or is incomplete.");
        var package=new GamePackage(build.Package);ValidateAssets(package,build.Assets);
        var old=Current(game.Root);if(old is {State:"Restoring" or "Installing"})throw new InvalidOperationException("Restore the interrupted Steam car installation first.");if(old is {State:"Installed"})Verify(old);
        var target=Target(game.Root,Package);var paclog=Target(game.Root,Paclog);
        if(File.Exists(target)&&old is not {State:"Installed"})throw new IOException("Another package already uses this car's local install path. No files were replaced.");
        var baseline=ReadBaselinePaclog(workspace,game.Root);if(Convert.ToHexString(SHA256.HashData(baseline))!=build.OriginalPaclogHash)throw new InvalidDataException("The game's livery loading list changed after this build.");
        if(!LoadingList(baseline,build.Assets.Keys).AsSpan().SequenceEqual(File.ReadAllBytes(build.Paclog)))throw new InvalidDataException("Unexpected changes to the car's livery loading list.");
        // Reject a competing package rather than relying on file enumeration
        // order to overwrite another local modification of this car.
        foreach(var other in Directory.EnumerateFiles(Path.GetDirectoryName(target)!,"*.PKG",SearchOption.AllDirectories)){
            if(PathIdentity.Resolve(other).Equals(target,StringComparison.OrdinalIgnoreCase)||PathIdentity.Resolve(other).Equals(Target(game.Root,StockPackage),StringComparison.OrdinalIgnoreCase))continue;
            if(new GamePackage(other).Entries.Keys.Any(n=>n==Model||n==Material))throw new IOException("Another local package changes Grosse pointe. Restore it before installing this car.");
        }
        var receipt=new SteamCarReceipt(1,build.Id,build.Name,game.Root,"Installed",build.PackageHash,build.PaclogHash,build.OriginalPaclogHash,DateTimeOffset.UtcNow);var folder=Folder(receipt);Directory.CreateDirectory(folder);
        var journal=Path.Combine(folder,"transaction.json");if(File.Exists(journal))throw new InvalidOperationException("This build already has an installation record.");
        AtomicFiles.Write(Path.Combine(folder,"original.PACLOG"),baseline);
        var previousPaclog=File.ReadAllBytes(paclog);var previousHash=Convert.ToHexString(SHA256.HashData(previousPaclog));var previousPackage=File.Exists(target)?WorkshopService.Hash(target):null;
        if(previousHash!=(old is {State:"Installed"}?old.PaclogHash:build.OriginalPaclogHash))throw new IOException("The livery loading list changed during installation.");
        AtomicFiles.Write(Path.Combine(folder,"before.PACLOG"),previousPaclog);if(previousPackage is not null)File.Copy(target,Path.Combine(folder,"before.PKG"));
        receipt=receipt with {PreviousPackageHash=previousPackage,PreviousPaclogHash=previousHash};
        AtomicFiles.Write(journal,JsonSerializer.SerializeToUtf8Bytes(receipt with {State="Installing"},Contract.Json));
        var wrotePackage=false;var wrotePaclog=false;
        try
        {
            AtomicFiles.Write(CurrentFile(game.Root),JsonSerializer.SerializeToUtf8Bytes(receipt with {State="Installing"},Contract.Json));
            if((File.Exists(target)?WorkshopService.Hash(target):null)!=previousPackage||WorkshopService.Hash(paclog)!=previousHash)throw new IOException("The game files changed during installation.");
            publish(target,File.ReadAllBytes(build.Package));wrotePackage=true;
            if(WorkshopService.Hash(paclog)!=previousHash)throw new IOException("The livery loading list changed during installation.");
            publish(paclog,File.ReadAllBytes(build.Paclog));wrotePaclog=true;
            AtomicFiles.Write(journal,JsonSerializer.SerializeToUtf8Bytes(receipt,Contract.Json));AtomicFiles.Write(CurrentFile(game.Root),JsonSerializer.SerializeToUtf8Bytes(receipt,Contract.Json));Verify(receipt);return receipt;
        }
        catch
        {
            if(wrotePaclog&&WorkshopService.Hash(paclog)==build.PaclogHash)AtomicFiles.Write(paclog,previousPaclog);
            if(wrotePackage&&File.Exists(target)&&WorkshopService.Hash(target)==build.PackageHash){if(previousPackage is null)File.Move(target,Path.Combine(folder,"rolled-back.PKG"));else AtomicFiles.Write(target,File.ReadAllBytes(Path.Combine(folder,"before.PKG")));}
            if(old is not null)AtomicFiles.Write(CurrentFile(game.Root),JsonSerializer.SerializeToUtf8Bytes(old,Contract.Json));else if(File.Exists(CurrentFile(game.Root)))File.Delete(CurrentFile(game.Root));throw;
        }
    }
    internal static void ValidateAssets(GamePackage package,IReadOnlyDictionary<string,string> hashes)
    {
        var effect=hashes.Keys.SingleOrDefault(n=>Regex.IsMatch(n,@"^WIN32/GRAPH/CARS/DERBY/BUICK/BODY_[0-9a-f]{32}_SIDES\.PFX$",RegexOptions.IgnoreCase));
        if(effect is null)throw new InvalidDataException("The Steam car paint effect is missing.");var prefix=effect[..^10];
        var texturePrefix=prefix.Replace("/BUICK/BODY_","/BUICK/TEXTURES/BODY_",StringComparison.OrdinalIgnoreCase);
        var expected=new HashSet<string>(new[]{Model,Material,effect,texturePrefix+"_DIFF.PTX",texturePrefix+"_IOR.PTX"},StringComparer.OrdinalIgnoreCase);
        if(hashes.Count!=5||!expected.SetEquals(hashes.Keys)||!expected.SetEquals(package.Entries.Keys))throw new InvalidDataException("Unexpected files in the Steam car package.");
        foreach(var asset in hashes)if(!IsHash(asset.Value)||Convert.ToHexString(SHA256.HashData(package.Read(asset.Key,128*1024*1024)))!=asset.Value)throw new InvalidDataException("A Steam car asset failed verification.");
    }
    internal static byte[] LoadingList(byte[] original,IEnumerable<string> assets)
    {
        if(original.Length>1024*1024)throw new InvalidDataException("Oversized livery loading list.");
        var text=new UTF8Encoding(false,true).GetString(original);
        if(!text.Contains("BODY_01.PMA",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The retail livery loading list has changed.");
        var extra=assets.Select(GamePackage.Normalize).Where(n=>n!=Model&&n!=Material).Order(StringComparer.Ordinal).Select(n=>"DATA:/"+n[6..]);
        return Encoding.UTF8.GetBytes(text.TrimEnd()+"\r\n"+string.Join("\r\n",extra)+"\r\n");
    }
    public void Restore(string game)
    {
        RequireClosed(game);using var gate=GameImportLock.Enter(game);var current=Current(game);if(current is null||current.State=="Removed")return;
        var target=Target(game,Package);var paclog=Target(game,Paclog);var pacHash=WorkshopService.Hash(paclog);var packageHash=File.Exists(target)?WorkshopService.Hash(target):null;
        bool Previous(string file,string? expected,string? actual)=>current.State=="Installing"&&expected is not null&&expected==actual&&File.Exists(Path.Combine(Folder(current),file))&&WorkshopService.Hash(Path.Combine(Folder(current),file))==expected;
        if(pacHash!=current.PaclogHash&&pacHash!=current.OriginalPaclogHash&&!Previous("before.PACLOG",current.PreviousPaclogHash,pacHash)||packageHash is not null&&packageHash!=current.PackageHash&&!Previous("before.PKG",current.PreviousPackageHash,packageHash))throw new InvalidDataException("A local car file was edited outside the app. No game files were restored.");
        var restoring=current with {State="Restoring",PackageHash=packageHash??current.PackageHash};AtomicFiles.Write(CurrentFile(game),JsonSerializer.SerializeToUtf8Bytes(restoring,Contract.Json));
        if(pacHash!=current.OriginalPaclogHash)AtomicFiles.Write(paclog,File.ReadAllBytes(Path.Combine(Folder(current),"original.PACLOG")));
        if(File.Exists(target)){var archived=Path.Combine(Folder(current),"restored.PKG");if(File.Exists(archived))throw new IOException("A previous restore archive already exists.");File.Move(target,archived);}
        AtomicFiles.Write(CurrentFile(game),JsonSerializer.SerializeToUtf8Bytes(current with {State="Removed"},Contract.Json));
    }
}
