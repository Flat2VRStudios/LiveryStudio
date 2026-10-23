using LiveryStudio.Core;
using System.Numerics;
using System.Xml.Linq;

internal static class IndependentSidesChecks
{
    public static void Run(string repo,string temp,Action<bool,string> check)
    {
        var source=new PaintImage(2,1,[10,20,30,40,50,60,70,80]);var atlas=IndependentPaintSides.Atlas(source);
        check(atlas.Width==4&&atlas.Height==1&&atlas.Pixels.SequenceEqual(source.Pixels.Concat(source.Pixels)),"Independent paint preserves source pixels, alpha and texel density on both sides");
        float[] Triangle(float x)=>[x-.15f,-.15f,0,0,0,1,.1f,.1f,1,x+.15f,-.15f,0,0,0,1,.9f,.1f,1,x,.15f,0,0,0,1,.5f,.9f,1];
        var stock=new PaintMesh([..Triangle(-.8f),..Triangle(.8f)]);var values=(float[])stock.NativeVertices.Clone();for(var i=0;i<6;i++)values[i*9+6]=(values[i*9+6]+(i>=3?1:0))*.5f;var mesh=new PaintMesh(values);
        check(mesh.Vertices.Take(3).All(v=>v.UV.X<.5f)&&mesh.Vertices.Skip(3).All(v=>v.UV.X>.5f)&&mesh.Vertices.Zip(stock.Vertices).All(p=>p.First.Position==p.Second.Position&&p.First.Normal==p.Second.Normal),"Separate paint UVs retain every factory vertex and normal");
        var file=Path.Combine(temp,"independent-base.tga");FinishMaps.WriteTga(new(32,32,new byte[32*32*4]),file);
        var wide=Path.Combine(temp,"independent-atlas.tga");FinishMaps.WriteTga(IndependentPaintSides.Atlas(FinishMaps.Read(file)),wide);
        var project=new LiveryProject{Albedo=wide,Structure=wide,IndependentPaintSides=true};
        var document=new LayerDocument{Version=9,BaseAlbedo=wide,BaseStructure=wide,SeamPadding=0};
        foreach(var side in new[]{-1,1})
        {
            var hit=mesh.Raycast(new(side*.8f,-.05f,1),-Vector3.UnitZ)!.Value;
            var layer=SimplePlacement.OnSurface(new(){Kind=LayerKind.Color,Color="#FF0000",Samples=[SurfaceProjection.At(hit,Vector3.UnitX,.6f,1,0)]});
            var copy=document.Snapshot("Independent bumper",[layer]);var painted=copy.Compose(mesh,project).Albedo;
            var other=mesh.Raycast(new(-side*.8f,-.05f,1),-Vector3.UnitZ)!.Value;
            int Pixel(SurfaceHit at)=>((int)(at.UV.Y*painted.Height)*painted.Width+(int)(at.UV.X*painted.Width))*4;
            check(painted.Pixels[Pixel(hit)]>200&&painted.Pixels[Pixel(other)]==0,"Painting the "+(side<0?"left":"right")+" side leaves the opposite side's paint untouched");
        }
        var material=XDocument.Parse(WorkshopService.MaterialXml(project,"BODY_test"));
        check(material.Descendants("material").Single().Attribute("effect")!.Value.EndsWith("BODY_test_SIDES.PFX.XML")&&material.Descendants("trigger").Single(t=>t.Attribute("name")!.Value=="Use NormalMap").Attribute("value")!.Value=="true","Independent paint uses its dedicated effect and retains factory normal maps");
        var portable=Path.Combine(temp,"independent.flproject");ProjectBundle.Save(project,portable);var round=ProjectBundle.Open(portable,Path.Combine(temp,"independent-open"));check(round.IndependentPaintSides,"Portable paint retains its matching-model requirement");
        var assembly=new VehicleAssemblyService(Path.Combine(repo,"LiveryStudio/artifacts/vehicle-runtime"),Path.Combine(temp,"native-independent"),Path.Combine(repo,"LiveryStudio/artifacts/models"));
        var car=assembly.CreateIndependentSidesAsync(project with {Albedo=file,Structure=file,IndependentPaintSides=false}).GetAwaiter().GetResult();
        var carFile=Path.Combine(temp,"independent.flcar");VehicleProjectBundle.Save(car,carFile);var reopened=VehicleProjectBundle.Open(carFile,Path.Combine(temp,"car-roundtrip"));
        check(reopened.Prepared&&reopened.Paint.IndependentPaintSides&&!reopened.Paint.FlatExteriorNormals,"Native corrected car round-trips with matching geometry and original surface normals");
        var receiptId="CAR_"+Guid.NewGuid().ToString("N");var paintId="BODY_"+Guid.NewGuid().ToString("N");var donor=assembly.Donor(0);var prefix="GRAPH/CARS/DERBY/BUICK/";
        var data=Path.Combine(temp,"receipt-game");Directory.CreateDirectory(data);var receiptFolder=Path.Combine(assembly.Workspace,"vehicle-imports",receiptId);Directory.CreateDirectory(receiptFolder);
        var names=new[]{"WIN32/GRAPH/CARS/LIVERY_STUDIO/"+receiptId[4..20]+"/BODY.P3D","WIN32/"+donor.BodyResource[6..],donor.PrefabPath,"Common/TUNING/LIVERY_STUDIO/"+receiptId+"/BREAKABLES.CFG","WIN32/"+prefix+paintId+".PMA","WIN32/"+prefix+"TEXTURES/"+paintId+"_DIFF.PTX","WIN32/"+prefix+"TEXTURES/"+paintId+"_IOR.PTX","WIN32/"+prefix+paintId+"_SIDES.PFX","WIN32/PACLOG/CUSTOMPAC/LIVERY_STUDIO/"+paintId+".PACLOG","Common/Tuning/Factories/CarsSkins.cfg","Common/Tuning/Factories/Rewards.cfg"};
        var entries=new List<VehicleInstalledFile>();var hash=WorkshopService.Hash(file);
        foreach(var name in names){var config=name.EndsWith(".cfg",StringComparison.OrdinalIgnoreCase)&&name.Contains("Factories");var backup=config?entries.Count+".before":"";if(config)File.Copy(file,Path.Combine(receiptFolder,backup));entries.Add(new(name,hash,config?hash:null,backup,config?"\r\n-- LIVERY_STUDIO_BEGIN "+paintId+"\r\n-- LIVERY_STUDIO_END "+paintId+"\r\n":""));}
        var receipt=new VehicleImportReceipt(3,receiptId,0,"Independent bumper",data,"Installed",paintId,entries.ToArray(),DateTimeOffset.UtcNow);var receiptFile=Path.Combine(receiptFolder,"receipt.json");File.WriteAllText(receiptFile,System.Text.Json.JsonSerializer.Serialize(receipt,Contract.Json));var manager=new VehicleImportManager(assembly);
        check(manager.Read(receiptFile,data).Receipt.Files.Length==11,"Corrected car receipt owns its geometry, paint effect and settings together");
        var changed=receipt.Files.ToArray();changed[7]=changed[7] with {Relative="WIN32/EFFECTS/UNRELATED.PFX"};File.WriteAllText(receiptFile,System.Text.Json.JsonSerializer.Serialize(receipt with {Files=changed},Contract.Json));var rejected=false;try{manager.Read(receiptFile,data);}catch(InvalidDataException){rejected=true;}check(rejected,"Corrected car receipt rejects ownership of unrelated game effects");
        var corrected=PaintMesh.Read(reopened.PreviewFile);var safe=SimplePlacement.UnsharedExterior(corrected);
        foreach(var x in new[]{-.8f,.8f}){var hit=corrected.Raycast(new(x,-.3f,5),-Vector3.UnitZ)!.Value;check(safe.Contains(hit.Triangle)&&MeshEditing.SharedUvCount(corrected,hit)==0,"Reported "+(x<0?"left":"right")+" bumper corner is independently paintable with protection enabled");}
    }
}
