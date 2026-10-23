using LiveryStudio.Core;
using System.Text.Json;
using System.Runtime.InteropServices;

internal static class WorkshopChecks
{
 public static void Run(Action<bool,string> Check)
 {


    Check(SteamWorkshop.FlatOutVR==3844750&&SteamWorkshop.FlatOut4==402130,"Distinct consumer app IDs");
    var rejected=false;try{SteamWorkshop.GameName(480);}catch(ArgumentException){rejected=true;}Check(rejected,"Reject unrelated Workshop targets");
    var type=typeof(SteamWorkshop);
    var create=type.GetNestedType("CreateResult",System.Reflection.BindingFlags.NonPublic)!;
    var submit=type.GetNestedType("SubmitResult",System.Reflection.BindingFlags.NonPublic)!;
    Check(Marshal.SizeOf(create)==24&&Marshal.OffsetOf(create,"Id").ToInt32()==8&&Marshal.OffsetOf(create,"NeedsAgreement").ToInt32()==16,"Create callback matches Steamworks Windows ABI");
    Check(Marshal.SizeOf(submit)==16&&Marshal.OffsetOf(submit,"Id").ToInt32()==8&&Marshal.OffsetOf(submit,"NeedsAgreement").ToInt32()==4,"Submit callback matches Steamworks Windows ABI");
    var receipt=new WorkshopPublication(SteamWorkshop.FlatOutVR,12345678901234,false,true,DateTimeOffset.UtcNow);
    Check(JsonSerializer.Deserialize<WorkshopPublication>(JsonSerializer.Serialize(receipt,Contract.Json),Contract.Json)==receipt,"Receipt roundtrip retains a 64-bit ID and agreement state");
    Check(receipt.Url.EndsWith("12345678901234"),"Item URL retains the exact ID");
    var fixture=Path.Combine(Path.GetTempPath(),"LiveryStudioWorkshopChecks",Guid.NewGuid().ToString("N"));
    var id="BODY_"+Guid.NewGuid().ToString("N");var content=Path.Combine(fixture,"Workshop");
    Directory.CreateDirectory(Path.Combine(content,id));Directory.CreateDirectory(Path.Combine(fixture,"Data/WIN32/Pkg/CustomPkg"));
    File.WriteAllText(Path.Combine(fixture,"Data/WIN32/Pkg/CustomPkg/Pkg.pkg"),"fixture package");File.WriteAllText(Path.Combine(fixture,"Data/WIN32/Paclog.PACLOG"),"fixture inventory");
    var pkg=Path.Combine(content,id,id+".PKG");File.Copy(Path.Combine(fixture,"Data/WIN32/Pkg/CustomPkg/Pkg.pkg"),pkg);File.Copy(Path.Combine(fixture,"Data/WIN32/Paclog.PACLOG"),Path.Combine(content,id+".PACLOG"));
    var build=new BuiltLivery(id,new(),fixture,content,"","",[]);
    void Reject(Action action,string name){var denied=false;try{action();}catch(InvalidDataException){denied=true;}Check(denied,name);}
    SteamWorkshop.ValidatePackage(build);Check(true,"Upload package matches its compiler outputs");
    Check(SteamWorkshop.ReceiptPath(build,SteamWorkshop.FlatOutVR)!=SteamWorkshop.ReceiptPath(build,SteamWorkshop.FlatOut4),"Publishing receipts stay separate for each game");
    Reject(()=>SteamWorkshop.ValidatePackage(build with {PackageDirectory=Path.GetTempPath()}),"Reject a content folder outside the saved build");
    File.AppendAllText(pkg,"changed");Reject(()=>SteamWorkshop.ValidatePackage(build),"Reject modified package payloads");
    File.Copy(Path.Combine(fixture,"Data/WIN32/Pkg/CustomPkg/Pkg.pkg"),pkg,true);File.WriteAllText(Path.Combine(content,"private-notes.txt"),"do not upload");
    Reject(()=>SteamWorkshop.ValidatePackage(build),"Reject extra files in the upload folder");

 }
}
