using LiveryStudio.Core;
using System.Security.Cryptography;
using System.Text;

internal static class SteamCarChecks
{
    public static void Run(string temp,Action<bool,string> check,Action<Action,string> reject)
    {
        var root=Path.Combine(temp,"steam-car");Directory.CreateDirectory(root);
        string FileAt(string name,string text){var p=Path.Combine(root,name);Directory.CreateDirectory(Path.GetDirectoryName(p)!);File.WriteAllText(p,text);return p;}
        var input=FileAt("input","original pixels");var pkg=Path.Combine(root,"roundtrip.PKG");
        GamePackage.Write(pkg,new Dictionary<string,string>{{"WIN32/TEST/A.PTX",input},{"COMMON/TUNING/B.CFG",input}});
        var archive=new GamePackage(pkg);
        check(archive.Entries.Count==2&&Encoding.UTF8.GetString(archive.Read("win32\\test\\a.ptx"))=="original pixels","Retail package writer roundtrips paths, bytes and CRCs");
        reject(()=>GamePackage.Normalize("WIN32/../OTHER"),"Reject package path traversal");
        reject(()=>GamePackage.Normalize("D:/OTHER"),"Reject absolute package asset paths");
        reject(()=>archive.Read("WIN32/TEST/A.PTX",2),"Bound decompressed retail asset size");
        var bad=Path.Combine(root,"damaged.PKG");File.Copy(pkg,bad);using(var file=File.OpenWrite(bad)){file.Position=archive.Entries["WIN32/TEST/A.PTX"].Offset+4;file.WriteByte(99);}
        reject(()=>new GamePackage(bad).Read("WIN32/TEST/A.PTX"),"Reject retail package payload CRC mismatch");
        var truncated=FileAt("truncated.PKG","PPKG");reject(()=>new GamePackage(truncated),"Reject truncated retail package header");
        byte[] Frame(params byte[][] blocks){using var m=new MemoryStream();using(var w=new BinaryWriter(m,Encoding.UTF8,true)){w.Write(0x184d2204u);w.Write((byte)0x40);w.Write((byte)0x40);w.Write((byte)0);foreach(var b in blocks){w.Write((uint)b.Length);w.Write(b);}w.Write(0u);}return m.ToArray();}
        var lz4=Frame([0x40,65,66,67,68],[0x00,4,0]);
        check(Encoding.ASCII.GetString(GamePackage.Lz4(lz4,8))=="ABCDABCD","Decode dependent LZ4 matches across retail blocks");
        reject(()=>GamePackage.Lz4(Frame([0x00,0,0]),4),"Reject zero-distance LZ4 match");
        reject(()=>GamePackage.Lz4(lz4,7),"Reject LZ4 output exceeding declared size");
        reject(()=>GamePackage.Lz4(lz4[..^1],8),"Reject truncated LZ4 terminator");

        var game=Path.Combine(root,"game");Directory.CreateDirectory(Path.Combine(game,"WIN32"));
        var exe=FileAt("game/Flatout.exe","retail executable");FileAt("game/steam_api64.dll","steam runtime");
        var stock=Path.Combine(game,SteamCarInstall.StockPackage);Directory.CreateDirectory(Path.GetDirectoryName(stock)!);GamePackage.Write(stock,new Dictionary<string,string>{{SteamCarInstall.Model,input}});
        var original=Encoding.UTF8.GetBytes("DATA:/GRAPH/CARS/DERBY/BUICK/BODY_01.PMA\r\n");
        var paclog=Path.Combine(game,SteamCarInstall.Paclog);Directory.CreateDirectory(Path.GetDirectoryName(paclog)!);File.WriteAllBytes(paclog,original);
        var workspace=Path.Combine(root,"workspace");var installer=new SteamCarInstall(workspace);
        SteamCarBuild Build(string content){
            var id="STEAM_"+Guid.NewGuid().ToString("N");var body="BODY_"+Guid.NewGuid().ToString("N");var path="WIN32/GRAPH/CARS/DERBY/BUICK/";
            var files=new Dictionary<string,string>{{SteamCarInstall.Model,FileAt(id+"/model",content)},{SteamCarInstall.Material,FileAt(id+"/material",content)},{path+body+"_SIDES.PFX",input},{path+"TEXTURES/"+body+"_DIFF.PTX",input},{path+"TEXTURES/"+body+"_IOR.PTX",input}};
            var package=Path.Combine(root,id+".PKG");GamePackage.Write(package,files);var list=Path.Combine(root,id+".PACLOG");File.WriteAllBytes(list,SteamCarInstall.LoadingList(original,files.Keys));
            return new(id,content,game,package,WorkshopService.Hash(package),list,WorkshopService.Hash(list),Convert.ToHexString(SHA256.HashData(original)),WorkshopService.Hash(exe),new GamePackage(stock).IndexHash,files.ToDictionary(p=>p.Key,p=>WorkshopService.Hash(p.Value)));
        }
        var stockHash=WorkshopService.Hash(stock);var first=Build("first paint");var receipt=installer.Install(first);installer.Verify(receipt);
        check(File.Exists(Path.Combine(game,SteamCarInstall.Package))&&WorkshopService.Hash(stock)==stockHash,"Install Steam car without changing the retail archive");
        check(SteamCarInstall.ReadBaselinePaclog(workspace,game).SequenceEqual(original),"Installed car retains exact original loading list");
        var second=Build("second paint");var updated=installer.Install(second);installer.Verify(updated);
        check(installer.Current(game)?.Id==second.Id&&SteamCarInstall.ReadBaselinePaclog(workspace,game).SequenceEqual(original),"Updating the Steam look preserves the original restoration baseline");
        File.AppendAllText(paclog,"external edit");reject(()=>installer.Restore(game),"Preserve externally edited Steam car files during restore");
        reject(()=>installer.Install(Build("conflict")),"Reject update after external Steam file edit");
        File.Copy(second.Paclog,paclog,true);installer.Restore(game);
        check(!File.Exists(Path.Combine(game,SteamCarInstall.Package))&&File.ReadAllBytes(paclog).SequenceEqual(original)&&installer.Current(game)?.State=="Removed"&&WorkshopService.Hash(stock)==stockHash,"Restore removes only owned package and returns exact original loading list");
        installer.Restore(game);check(File.ReadAllBytes(paclog).SequenceEqual(original),"Steam car restore is repeatable");
        var failing=new SteamCarInstall(workspace,(p,b)=>{if(PathIdentity.Resolve(p).Equals(PathIdentity.Resolve(paclog),StringComparison.OrdinalIgnoreCase))throw new IOException("Simulated loading-list failure");AtomicFiles.Write(p,b);});
        reject(()=>failing.Install(Build("failed install")),"Report failure between Steam package and loading-list publication");
        check(!File.Exists(Path.Combine(game,SteamCarInstall.Package))&&File.ReadAllBytes(paclog).SequenceEqual(original)&&installer.Current(game)?.State=="Removed","Partial Steam installation rolls back exact prior file states");
        var prior=installer.Install(Build("before failed update"));
        reject(()=>failing.Install(Build("failed update")),"Report interrupted Steam car update");
        installer.Verify(prior);check(installer.Current(game)?.Id==prior.Id&&WorkshopService.Hash(Path.Combine(game,SteamCarInstall.Package))==prior.PackageHash,"Failed Steam update restores previous package and receipt");
        var interrupted=installer.Install(Build("interrupted update"));
        var currentFile=Directory.EnumerateFiles(workspace,"current.json",SearchOption.AllDirectories).Single();var recovery=Path.Combine(Path.GetDirectoryName(currentFile)!,interrupted.Id);
        File.Copy(Path.Combine(recovery,"before.PKG"),Path.Combine(game,SteamCarInstall.Package),true);File.Copy(Path.Combine(recovery,"before.PACLOG"),paclog,true);
        File.WriteAllText(currentFile,System.Text.Json.JsonSerializer.Serialize(interrupted with {State="Installing"},Contract.Json));
        reject(()=>SteamCarInstall.ReadBaselinePaclog(workspace,game),"Require restoring an interrupted Steam transaction before rebuilding");
        installer.Restore(game);
        check(installer.Current(game)?.State=="Removed"&&!File.Exists(Path.Combine(game,SteamCarInstall.Package))&&File.ReadAllBytes(paclog).SequenceEqual(original),"Recover interrupted update from either previous or new owned file states");
        var stale=Build("stale game");File.AppendAllText(exe,"update");reject(()=>installer.Install(stale),"Require rebuilding after Steam executable update");
        var unscoped=Build("unscoped");unscoped.Assets.Add("COMMON/TUNING/FACTORIES/REWARDS.CFG",WorkshopService.Hash(input));reject(()=>installer.Install(unscoped),"Reject car package ownership of unrelated retail settings");
        var badList=Build("bad list");File.AppendAllText(badList.Paclog,"DATA:/UNRELATED/FILE");badList=badList with {PaclogHash=WorkshopService.Hash(badList.Paclog)};
        reject(()=>installer.Install(badList),"Reject unrelated additions to the retail livery loading list");
        var competing=Path.Combine(game,"WIN32/PKG/CHUNK_0/OTHER.PKG");GamePackage.Write(competing,new Dictionary<string,string>{{SteamCarInstall.Model,input}});
        reject(()=>installer.Install(Build("competing")),"Reject another local package replacing the same Steam car");
        var stockLayout="{\"nodes\":[{\"name\":\"Body\",\"mesh\":true,\"sections\":2,\"lods\":3}]}";
        VehicleAssemblyService.ValidateSteamLayout(stockLayout,stockLayout);check(true,"Accept unchanged retail hierarchy and material slots");
        reject(()=>VehicleAssemblyService.ValidateSteamLayout(stockLayout,stockLayout.Replace("\"sections\":2","\"sections\":1")),"Reject collapsed retail material slots");
        reject(()=>VehicleAssemblyService.ValidateSteamLayout(stockLayout,stockLayout.Replace("Body","Other")),"Reject mismatched retail panel hierarchy");
    }
}
