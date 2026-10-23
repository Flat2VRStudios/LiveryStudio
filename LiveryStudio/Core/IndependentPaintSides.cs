using System.Numerics;
using System.Text.Json;

namespace LiveryStudio.Core;

public static class IndependentPaintSides
{
    // Duplicating pixels retains the factory appearance and each side's texel
    // density. The second UV channel selects a different half of this image.
    public static PaintImage Atlas(PaintImage source)
    {
        source.Validate();
        if(source.Width>4096||source.Height>8192)throw new InvalidDataException("The source texture is too large for independent sides.");
        var pixels=new byte[checked(source.Pixels.Length*2)];var row=source.Width*4;
        for(var y=0;y<source.Height;y++){source.Pixels.AsSpan(y*row,row).CopyTo(pixels.AsSpan(y*row*2,row));source.Pixels.AsSpan(y*row,row).CopyTo(pixels.AsSpan(y*row*2+row,row));}
        return new(source.Width*2,source.Height,pixels);
    }
    public static void RetargetMasks(LayerDocument document,PaintMesh source,PaintMesh target)
    {
        static string Key(PaintMesh mesh,int t)=>string.Join(';',mesh.Vertices.Skip(t*3).Take(3).Select(v=>$"{Math.Round(v.Position.X*1000)},{Math.Round(v.Position.Y*1000)},{Math.Round(v.Position.Z*1000)}").Order(StringComparer.Ordinal));
        var targets=Enumerable.Range(0,target.Vertices.Length/3).GroupBy(t=>Key(target,t)).ToDictionary(g=>g.Key,g=>g.ToArray());var map=new Dictionary<int,int[]>();
        foreach(var layer in document.History.SelectMany(s=>s.Layers))
        {
            foreach(var triangle in layer.Triangles.Distinct())if(!map.ContainsKey(triangle)){
                if(triangle>=source.Vertices.Length/3||!targets.TryGetValue(Key(source,triangle),out var matches))throw new InvalidDataException("A saved surface mask could not be matched to the corrected model. Your original project is unchanged.");map.Add(triangle,matches);
            }
            layer.Triangles=layer.Triangles.SelectMany(t=>map[t]).Distinct().Order().ToArray();
        }
    }
}

public sealed partial class VehicleAssemblyService
{
    public async Task<VehicleAssemblyProject> CreateIndependentSidesAsync(LiveryProject source,CancellationToken cancellation=default)
    {
        if(source.IndependentPaintSides)throw new InvalidOperationException("This project already has independent sides. Open its saved car project in Car Builder.");
        source.Validate();ValidateTools();var donor=Donor(source.CarIndex);ValidateDonor(donor);
        var folder=Path.Combine(Workspace,"projects",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        // Isolate every original map, artwork asset and undo checkpoint first.
        // Nothing in the current draft is rewritten during the conversion.
        var original=Path.Combine(folder,"Original design.flproject");ProjectBundle.Save(source,original);
        var paint=ProjectBundle.Open(original,Path.Combine(folder,"paint"));paint.IndependentPaintSides=true;paint.FlatExteriorNormals=false;
        var exterior=Path.Combine(folder,"Factory.FBX");File.Copy(Path.Combine(DonorFolder(donor),"Body.FBX"),exterior);
        var project=new VehicleAssemblyProject{Name=source.Name+" · independent sides",DonorIndex=source.CarIndex,ExteriorFile=exterior,ExteriorHash=WorkshopService.Hash(exterior),Fit=new(){AutoFit=false},Paint=paint};
        if(project.Name.Length>100)project.Name=project.Name[..100];
        await PrepareAsync(project,cancellation);
        var oldMesh=PaintMesh.Read(Path.Combine(ModelsRoot,donor.Index.ToString("00")+".flsm"));var model=PaintMesh.Read(project.PreviewFile);
        var document=paint.EditorFile.Length>0?LayerDocument.Read(paint.EditorFile):LayerDocument.Create(paint,Path.Combine(folder,"base"));
        IndependentPaintSides.RetargetMasks(document,oldMesh,model);
        string Duplicate(string input,string name){cancellation.ThrowIfCancellationRequested();var path=Path.Combine(folder,name+".tga");FinishMaps.WriteTga(IndependentPaintSides.Atlas(FinishMaps.Read(input,cancellation)),path);return path;}
        document.BaseAlbedo=Duplicate(document.BaseAlbedo,"original-color");document.BaseStructure=Duplicate(document.BaseStructure,"original-surface");
        var maps=document.Compose(model,paint,cancellation);paint.Albedo=Path.Combine(folder,"color.tga");paint.Structure=Path.Combine(folder,"surface.tga");FinishMaps.WriteTga(maps.Albedo,paint.Albedo);FinishMaps.WriteTga(maps.Structure,paint.Structure);paint.EditorFile=Path.Combine(folder,"editor.json");document.Save(paint.EditorFile);
        project.Paint=paint;RememberPrepared(project);return project;
    }
}
