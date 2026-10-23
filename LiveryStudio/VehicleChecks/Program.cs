using LiveryStudio.Core;
using System.Text.Json;

if(args.Length==7&&args[0]=="--restore")
{
    var assembly=new VehicleAssemblyService(args[1],args[3],args[2]);
    var manager=new VehicleImportManager(assembly);
    var entry=manager.Read(args[4],args[5]);manager.Restore(entry,args[5]);
    Console.WriteLine(JsonSerializer.Serialize(manager.Read(args[4],args[5]).Receipt,Contract.Json));return;
}
var reopen=args.Length>0&&args[0]=="--project";if(reopen)args=args[1..];
if(args.Length<5)throw new ArgumentException("Expected [--project] runtime, models, workspace, exterior or .flcar, game Data folder");
var service=new VehicleAssemblyService(args[0],args[2],args[1]);service.Log+=text=>File.AppendAllText(Path.Combine(args[2],"worker.log"),text+Environment.NewLine);service.Progress+=(n,s)=>Console.WriteLine($"{n}% {s}");Directory.CreateDirectory(args[2]);
var project=reopen?VehicleProjectBundle.Open(args[3],args[2]):new VehicleAssemblyProject{Name="Widebody Flight",DonorIndex=0,Fit=new(){AutoFit=false}};
if(!reopen)await service.ImportAsync(project,args[3]);else if(!project.Prepared)await service.PrepareAsync(project);
File.WriteAllText(Path.Combine(args[2],"project.json"),JsonSerializer.Serialize(project,Contract.Json));
Console.WriteLine("Prepared "+project.PreviewFile);
var paint=new WorkshopService(args.Length>5?args[5]:WorkshopService.DefaultTools,args[2]);paint.Log+=text=>File.AppendAllText(Path.Combine(args[2],"paint.log"),text+Environment.NewLine);paint.Progress+=(n,s)=>Console.WriteLine($"paint {n}% {s}");
var built=await service.BuildAsync(project,paint,args[4]);File.WriteAllText(Path.Combine(args[2],"built.json"),JsonSerializer.Serialize(built,Contract.Json));Console.WriteLine("Built "+built.Id+" at "+built.Root);
