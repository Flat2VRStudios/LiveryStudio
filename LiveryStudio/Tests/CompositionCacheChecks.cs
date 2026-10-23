using System.Numerics;
using LiveryStudio.Core;

internal static class CompositionCacheChecks
{
    public static void Run(string temp,Action<bool,string> check)
    {
        var folder=Path.Combine(temp,"undo-cache");Directory.CreateDirectory(folder);
        var image=new PaintImage(48,48,new byte[48*48*4]);var color=Path.Combine(folder,"color.tga");var surface=Path.Combine(folder,"surface.tga");FinishMaps.WriteTga(image,color);FinishMaps.WriteTga(image,surface);
        float[] vertices=[-.5f,.5f,0,0,0,1,0,0,1, .5f,.5f,0,0,0,1,1,0,1, -.5f,-.5f,0,0,0,1,0,1,1, .5f,.5f,0,0,0,1,1,0,1, .5f,-.5f,0,0,0,1,1,1,1, -.5f,-.5f,0,0,0,1,0,1,1];
        var mesh=new PaintMesh(vertices);var project=new LiveryProject{Albedo=color,Structure=surface};var document=new LayerDocument{BaseAlbedo=color,BaseStructure=surface};
        var cache=new CompositionCache(256*1024);var initial=cache.Compose(document,mesh,project);
        var stamp=new PaintLayer{Kind=LayerKind.Color,Color="#F14030",Samples=[new(Vector3.Zero,Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,.7f,.5f)]};
        document.Commit("Red stroke",layers=>layers.Add(stamp));var red=cache.Compose(document,mesh,project);
        document.Undo();var undo=cache.Compose(document,mesh,project);document.Redo();var redo=cache.Compose(document,mesh,project);
        bool Equal(CompositeMaps a,CompositeMaps b)=>a.Albedo.Pixels.SequenceEqual(b.Albedo.Pixels)&&a.Structure.Pixels.SequenceEqual(b.Structure.Pixels);
        check(Equal(initial,undo)&&Equal(red,redo)&&cache.Hits==2,"Adjacent undo and redo restore byte-identical cached maps");
        Array.Fill(redo.Albedo.Pixels,(byte)44);Array.Fill(redo.Structure.Pixels,(byte)55);
        check(Equal(red,cache.Compose(document,mesh,project)),"Painting or editing restored maps cannot mutate retained undo pixels");
        document.Undo();document.Commit("New blue branch",layers=>layers.Add(stamp with {Color="#1234FF"}));var branch=cache.Compose(document,mesh,project);
        check(!Equal(branch,red)&&Equal(branch,document.Compose(mesh,project))&&!document.CanRedo,"A new branch after undo never reuses a different checkpoint's pixels");
        var finish=project with {OverrideArtworkFinish=true,ArtworkGloss=.91,ArtworkMetalness=.6,InnerColor="#D9B122",UseSrgbPaintColors=true};
        check(Equal(cache.Compose(document,mesh,finish),document.Compose(mesh,finish)),"Cached composition tracks paint colors and artwork finish inputs");
        document.SeamPadding=0;check(Equal(cache.Compose(document,mesh,finish),document.Compose(mesh,finish)),"Changing seam settings invalidates composed undo maps");
        var moved=new PaintMesh(vertices.Select((v,i)=>i%9==0?v+2:v).ToArray());check(Equal(cache.Compose(document,moved,project),document.Compose(moved,project)),"Changed geometry uses distinct texture composition and coverage");
        var source=new PaintImage(48,48,Enumerable.Repeat((byte)90,48*48*4).ToArray());FinishMaps.WriteTga(source,color);File.SetLastWriteTimeUtc(color,DateTime.UtcNow.AddSeconds(2));
        check(Equal(cache.Compose(document,mesh,project),document.Compose(mesh,project)),"Replacing a base texture invalidates decoded bases and undo pixels");
        var logo=Path.Combine(folder,"logo.tga");FinishMaps.WriteTga(new(1,1,[255,0,0,255]),logo);document.Commit("Logo",layers=>layers.Add(stamp with {Id=Guid.NewGuid(),Kind=LayerKind.Sticker,Asset=logo}));cache.Compose(document,mesh,project);
        FinishMaps.WriteTga(new(1,1,[0,255,0,255]),logo);File.SetLastWriteTimeUtc(logo,DateTime.UtcNow.AddSeconds(3));
        check(Equal(cache.Compose(document,mesh,project),document.Compose(mesh,project)),"Replacing source artwork invalidates retained checkpoint maps");
        for(var i=0;i<15;i++){document.Commit("Change",layers=>layers[0].Opacity=.1f+i*.05f);cache.Compose(document,mesh,project);}
        check(cache.RetainedBytes<=cache.Budget,"Many history states stay within the editor's fixed cache memory budget");
        var tiny=new CompositionCache(1);check(Equal(tiny.Compose(document,mesh,project),document.Compose(mesh,project))&&tiny.RetainedBytes==0,"Oversized compositions remain correct without being retained");
        using var canceled=new CancellationTokenSource();canceled.Cancel();var stopped=false;try{cache.Compose(document,mesh,project,canceled.Token);}catch(OperationCanceledException){stopped=true;}
        check(stopped,"Cancellation is honored even when an undo state is already cached");cache.Clear();check(cache.RetainedBytes==0,"Switching projects can release all retained undo and base images");
        foreach(var surfaceSize in new[]{24,48})
        {
            var pairFolder=Path.Combine(folder,"retention-"+surfaceSize);Directory.CreateDirectory(pairFolder);var pairColor=Path.Combine(pairFolder,"color.tga");var pairSurface=Path.Combine(pairFolder,"surface.tga");FinishMaps.WriteTga(new(48,48,new byte[48*48*4]),pairColor);FinishMaps.WriteTga(new(surfaceSize,surfaceSize,new byte[surfaceSize*surfaceSize*4]),pairSurface);
            var pairProject=new LiveryProject{Albedo=pairColor,Structure=pairSurface};var pairDoc=new LayerDocument{BaseAlbedo=pairColor,BaseStructure=pairSurface};var pairBytes=48*48*4+surfaceSize*surfaceSize*4;var pairCache=new CompositionCache(pairBytes*2);
            pairCache.Compose(pairDoc,mesh,pairProject);pairDoc.Commit("First",layers=>layers.Add(stamp));pairCache.Compose(pairDoc,mesh,pairProject);
            pairDoc.Undo();pairCache.Compose(pairDoc,mesh,pairProject);pairDoc.Redo();pairCache.Touch(pairDoc,mesh,pairProject);pairDoc.Commit("Next",layers=>layers[0].Color="#1234FF");pairCache.Compose(pairDoc,mesh,pairProject);
            pairDoc.Undo();var hits=pairCache.Hits;var prior=pairCache.Compose(pairDoc,mesh,pairProject);
            check(pairCache.Hits==hits+1&&Equal(prior,pairDoc.Compose(mesh,pairProject))&&pairCache.RetainedBytes<=pairCache.Budget,"Warming and a new edit retain the immediate Undo target within a two-pair budget at "+(surfaceSize==24?"4K/2K":"4K/4K")+" proportions");
        }
    }
}
