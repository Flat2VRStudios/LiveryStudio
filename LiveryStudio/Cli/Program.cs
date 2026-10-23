using LiveryStudio.Core;
using System.Text.Json;

try
{
	if(args.Length<1)throw new ArgumentException("Commands: sample <project.json> | build <project.json> [workspace] | import <livery.json> <game-data> | export <livery.json> <zip> | publish <livery.json> [3844750|402130]");
	var service=new WorkshopService(Environment.GetEnvironmentVariable("LIVERY_STUDIO_TOOLS") ?? WorkshopService.DefaultTools,args.Length>2 && args[0]=="build" ? args[2] : WorkshopService.DefaultWorkspace);
	service.Log+=Console.WriteLine;service.Progress+=(p,s)=>Console.WriteLine($"[{p}%] {s}");
	if(args[0]=="sample") { var p=new LiveryProject();var textures=service.Sample(p.SelectedCar);p.Albedo=textures.Albedo;p.Structure=textures.Structure;p.Banner=Path.Combine(service.ToolsRoot,"Doc","Derby","Grosse_Pointe_Diff.png");File.WriteAllText(args[1],JsonSerializer.Serialize(p,Contract.Json)); }
	else if(args[0]=="build") { var result=await service.BuildAsync(LiveryProject.Read(args[1])); Console.WriteLine("RESULT="+Path.Combine(result.BuildRoot,"livery.json")); }
	else { var livery=JsonSerializer.Deserialize<BuiltLivery>(File.ReadAllText(args[1]),Contract.Json)!; if(args[0]=="import")Console.WriteLine(JsonSerializer.Serialize(service.Import(livery,args[2]),Contract.Json));else if(args[0]=="export")WorkshopService.Export(livery,args[2]);else if(args[0]=="publish")Console.WriteLine(JsonSerializer.Serialize(await service.PublishAsync(livery,appId:args.Length>2?uint.Parse(args[2]):SteamWorkshop.FlatOutVR),Contract.Json));else throw new ArgumentException("Unknown command"); }
}
catch(Exception e){Console.Error.WriteLine(e.ToString());Environment.ExitCode=1;}
