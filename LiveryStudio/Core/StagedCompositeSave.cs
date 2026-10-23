namespace LiveryStudio.Core;

// Preparation never changes active files. The caller holds its save gate and
// checks the current revision immediately before calling synchronous Publish.
public sealed class StagedCompositeSave : IDisposable
{
    private sealed record Replacement(string Stage,string Target,string Backup)
    {
        public bool Existed {get;set;}
    }
    private readonly Replacement[] mapsAndEditor;
    private readonly Replacement previousEditor;
    private readonly HashSet<string> retainedRecoveryFiles=[];
    private bool consumed,disposed;

    private StagedCompositeSave(string colorPath,string surfacePath,string editorPath)
    {
        var paths=new[]{colorPath,surfacePath,editorPath,editorPath+".previous"}.Select(Path.GetFullPath).ToArray();
        if(paths.Distinct(OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal).Count()!=paths.Length)
            throw new ArgumentException("Composite output paths must be distinct.");
        var suffix="."+Guid.NewGuid().ToString("N");
        Replacement Entry(string path)=>new(path+suffix+".pending",path,path+suffix+".rollback");
        mapsAndEditor=paths.Take(3).Select(Entry).ToArray();previousEditor=Entry(paths[3]);
    }

    public static async Task<StagedCompositeSave> StageAsync(LayerDocument document,CompositeMaps maps,string colorPath,string surfacePath,string editorPath,Func<PaintImage,string,CancellationToken,Task> writeColor,CancellationToken cancellation=default)
    {
        ArgumentNullException.ThrowIfNull(document);ArgumentNullException.ThrowIfNull(maps);ArgumentNullException.ThrowIfNull(writeColor);
        cancellation.ThrowIfCancellationRequested();var prepared=new StagedCompositeSave(colorPath,surfacePath,editorPath);
        try
        {
            await Task.Run(async()=>
            {
                cancellation.ThrowIfCancellationRequested();
                foreach(var file in prepared.mapsAndEditor)Directory.CreateDirectory(Path.GetDirectoryName(file.Stage)!);
                await writeColor(maps.Albedo,prepared.mapsAndEditor[0].Stage,cancellation).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();FinishMaps.WriteTga(maps.Structure,prepared.mapsAndEditor[1].Stage);
                cancellation.ThrowIfCancellationRequested();document.Save(prepared.mapsAndEditor[2].Stage);
                cancellation.ThrowIfCancellationRequested();
            },cancellation).ConfigureAwait(false);
            return prepared;
        }
        catch{prepared.Dispose();throw;}
    }

    public void Publish()
    {
        ObjectDisposedException.ThrowIf(disposed,this);if(consumed)throw new InvalidOperationException("This composite save has already been published or discarded.");consumed=true;
        var replacements=mapsAndEditor.ToList();var installed=new List<Replacement>();
        try
        {
            // Back up every target before replacing any target. Stage the prior
            // editor as .previous only at publish time, not when encoding began.
            foreach(var file in mapsAndEditor)
            {
                if(!File.Exists(file.Stage))throw new FileNotFoundException("A prepared composite file is missing.",file.Stage);
                Backup(file);
            }
            if(mapsAndEditor[2].Existed)
            {
                File.Copy(mapsAndEditor[2].Backup,previousEditor.Stage,false);Backup(previousEditor);replacements.Add(previousEditor);
            }
            foreach(var file in replacements){File.Move(file.Stage,file.Target,true);installed.Add(file);}
        }
        catch(Exception publishError)
        {
            var failures=new List<Exception>{publishError};
            foreach(var file in installed.AsEnumerable().Reverse())
            {
                try{if(file.Existed)File.Move(file.Backup,file.Target,true);else File.Delete(file.Target);}
                catch(Exception rollbackError)
                {
                    if(file.Existed)retainedRecoveryFiles.Add(file.Backup);
                    failures.Add(new IOException(file.Existed?"Could not restore '"+file.Target+"'. Its prior file is retained at '"+file.Backup+"'.":"Could not remove the newly created file '"+file.Target+"' during rollback.",rollbackError));
                }
            }
            if(failures.Count>1)throw new AggregateException("Composite publication failed and some prior files require recovery.",failures);
            throw;
        }
        finally{Dispose();}
    }

    private static void Backup(Replacement file)
    {
        file.Existed=File.Exists(file.Target);if(file.Existed)File.Copy(file.Target,file.Backup,false);
    }

    public void Dispose()
    {
        if(disposed)return;disposed=true;
        foreach(var file in mapsAndEditor.Append(previousEditor))
        {
            TryDelete(file.Stage);if(!retainedRecoveryFiles.Contains(file.Backup))TryDelete(file.Backup);
        }
    }
    private static void TryDelete(string path)
    {
        try{File.Delete(path);}catch(IOException){}catch(UnauthorizedAccessException){}
    }
}
