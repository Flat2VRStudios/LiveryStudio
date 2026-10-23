using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.Versioning;
using System.Text.Json;
using LiveryStudio.Core;

[SupportedOSPlatform("windows")]
internal static class CompositeSaveChecks
{
    public static void Run(string temp,Action<bool,string> check)=>RunAsync(temp,check).GetAwaiter().GetResult();
    private static async Task RunAsync(string temp,Action<bool,string> check)
    {
        var root=Path.Combine(temp,"composite-save");Directory.CreateDirectory(root);
        var baseColor=Path.Combine(root,"base-color.tga");var baseSurface=Path.Combine(root,"base-surface.tga");
        FinishMaps.WriteTga(new(8,8,new byte[8*8*4]),baseColor);FinishMaps.WriteTga(new(8,8,new byte[8*8*4]),baseSurface);
        var mesh=new PaintMesh([-.5f,.5f,0,0,0,1,0,0,1,.5f,.5f,0,0,0,1,1,0,1,-.5f,-.5f,0,0,0,1,0,1,1,.5f,.5f,0,0,0,1,1,0,1,.5f,-.5f,0,0,0,1,1,1,1,-.5f,-.5f,0,0,0,1,0,1,1]);
        var project=new LiveryProject{Albedo=baseColor,Structure=baseSurface};
        (LayerDocument Document,CompositeMaps Maps) State(string color)
        {
            var document=new LayerDocument{BaseAlbedo=baseColor,BaseStructure=baseSurface,SeamPadding=0};
            document.Commit(color,layers=>layers.Add(new(){Kind=LayerKind.Color,Color=color,Samples=[new(Vector3.Zero,Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,1.2f,1.2f)]}));
            return(document,document.Compose(mesh,project));
        }
        var original=State("#12A430");var a=State("#CF2418");var b=State("#163DD9");
        var colorPath=Path.Combine(root,"albedo.png");var surfacePath=Path.Combine(root,"surface.tga");var editorPath=Path.Combine(root,"editor.json");
        async Task<StagedCompositeSave> Stage((LayerDocument Document,CompositeMaps Maps) state,Func<PaintImage,string,CancellationToken,Task>? writer=null,CancellationToken token=default)
            =>await StagedCompositeSave.StageAsync(state.Document,state.Maps,colorPath,surfacePath,editorPath,writer??WritePng,token);
        bool Exact((LayerDocument Document,CompositeMaps Maps) expected)
            =>FinishMaps.Read(colorPath).Pixels.SequenceEqual(expected.Maps.Albedo.Pixels)&&FinishMaps.Read(surfacePath).Pixels.SequenceEqual(expected.Maps.Structure.Pixels)&&JsonSerializer.Serialize(LayerDocument.Read(editorPath),LayerDocument.Json)==JsonSerializer.Serialize(expected.Document,LayerDocument.Json);
        bool Clean()=>!Directory.EnumerateFiles(root).Any(path=>path.EndsWith(".pending",StringComparison.Ordinal)||path.EndsWith(".rollback",StringComparison.Ordinal));
        Dictionary<string,byte[]> Active()=>new[]{colorPath,surfacePath,editorPath,editorPath+".previous"}.Where(File.Exists).ToDictionary(path=>path,File.ReadAllBytes);
        bool Unchanged(Dictionary<string,byte[]> files)=>files.All(entry=>File.Exists(entry.Key)&&File.ReadAllBytes(entry.Key).SequenceEqual(entry.Value));

        using(var first=await Stage(original))first.Publish();
        check(Exact(original)&&Clean(),"First composite publication saves exact map pixels and editor history without stage files");
        var before=Active();using(var prepared=await Stage(a))check(Unchanged(before),"Encoding a composite changes none of the active map or editor files");
        check(Unchanged(before)&&Clean(),"Discarding a prepared composite preserves active files and removes all stages");

        using(var canceled=new CancellationTokenSource())
        {
            var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var stagingA=Stage(a,async(image,path,_)=>{await WritePng(image,path,default);entered.SetResult();await release.Task;},canceled.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));canceled.Cancel();
            using(var preparedB=await Stage(b))preparedB.Publish();release.SetResult();
            var stopped=false;try{using var unexpected=await stagingA;}catch(OperationCanceledException){stopped=true;}
            check(stopped&&Exact(b)&&Clean(),"Canceled older PNG staging cannot overwrite a newer published composite and cleans its temporary files");
            check(File.ReadAllBytes(editorPath+".previous").SequenceEqual(before[editorPath]),"Successful publication retains the prior editor document in .previous");
        }

        using(var preparedA=await Stage(a))
        {
            var requestedRevision=1L;var aRevision=requestedRevision;
            using(var preparedB=await Stage(b)){requestedRevision++;preparedB.Publish();}
            if(aRevision==requestedRevision)preparedA.Publish();
        }
        check(Exact(b)&&Clean(),"An obsolete prepared revision discarded by its caller never replaces a newer saved design");

        var coherent=Active();using(var preparedA=await Stage(a))
        {
            // Read sharing permits backup copying, but denies replacement. The
            // color replacement succeeds before the surface replacement fails.
            using var held=new FileStream(surfacePath,FileMode.Open,FileAccess.Read,FileShare.Read);
            var failed=false;try{preparedA.Publish();}catch(Exception error) when(error is IOException or UnauthorizedAccessException){failed=true;}
            check(failed&&Unchanged(coherent),"A failure after replacing the color map rolls back to the complete prior map/editor/recovery set");
        }
        check(Clean()&&Exact(b),"Failed composite publication removes stages and rollback files after restoring the prior design");

        coherent=Active();using(var preparedA=await Stage(a))
        {
            // Failing the final .previous replacement exercises rollback of all
            // three newly installed active files, including editor.json.
            using var held=new FileStream(editorPath+".previous",FileMode.Open,FileAccess.Read,FileShare.Read);
            var failed=false;try{preparedA.Publish();}catch(Exception error) when(error is IOException or UnauthorizedAccessException){failed=true;}
            check(failed&&Unchanged(coherent),"Failure replacing editor recovery rolls back all three active files and preserves the previous recovery document");
        }
        check(Clean(),"Late publication failure leaves no staged or rollback files");

        var empty=Path.Combine(root,"first-save-failure");Directory.CreateDirectory(empty);var newColor=Path.Combine(empty,"albedo.png");var newSurface=Path.Combine(empty,"surface.tga");var newEditor=Path.Combine(empty,"editor.json");Directory.CreateDirectory(newSurface);
        using(var first=await StagedCompositeSave.StageAsync(a.Document,a.Maps,newColor,newSurface,newEditor,WritePng))
        {
            var failed=false;try{first.Publish();}catch(Exception error) when(error is IOException or UnauthorizedAccessException){failed=true;}
            check(failed&&!File.Exists(newColor)&&!File.Exists(newEditor)&&Directory.Exists(newSurface)&&!Directory.EnumerateFiles(empty).Any(),"A failed first save removes newly installed files when no prior composite exists");
        }

        var encoderFailed=false;try{using var unexpected=await Stage(a,async(image,path,token)=>{await WritePng(image,path,token);throw new IOException("Encoder failed after writing its stage.");});}catch(IOException){encoderFailed=true;}
        check(encoderFailed&&Unchanged(coherent)&&Clean(),"An encoder failure removes its partial stage and preserves the prior composite");

        var neverStarted=false;using(var canceled=new CancellationTokenSource())
        {
            canceled.Cancel();var stopped=false;try{using var unexpected=await Stage(a,(_,_,_)=>{neverStarted=true;return Task.CompletedTask;},canceled.Token);}catch(OperationCanceledException){stopped=true;}
            check(stopped&&!neverStarted&&Unchanged(coherent)&&Clean(),"Already canceled saves invoke no encoder and preserve the active composite");
        }
    }
    private static Task WritePng(PaintImage image,string path,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();using var bitmap=new Bitmap(image.Width,image.Height,PixelFormat.Format32bppArgb);
        for(var y=0;y<image.Height;y++)for(var x=0;x<image.Width;x++){var k=(y*image.Width+x)*4;bitmap.SetPixel(x,y,Color.FromArgb(image.Pixels[k+3],image.Pixels[k],image.Pixels[k+1],image.Pixels[k+2]));}
        bitmap.Save(path,ImageFormat.Png);return Task.CompletedTask;
    }
}
