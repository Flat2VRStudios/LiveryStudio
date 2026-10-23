using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LiveryStudio.Core;

public sealed record EditorSession(LiveryProject Project,string Tools,string Game,string PreviousDraft="",int? Workspace=null,string? SessionOwner=null,bool AdvancedMode=false);
public sealed record SavedEditorDraft(string File,EditorSession Session,DateTimeOffset SavedAt);

// Every window keeps its own form state. The shared file only chooses the draft
// to resume; a closing window may update it only while it is still the owner.
public sealed class EditorSessionStore
{
    public string Owner {get;}=Guid.NewGuid().ToString("N");
    public string FilePath {get;}
    public string OwnFilePath {get;}
    private readonly string mutexName;

    public EditorSessionStore(string file)
    {
        FilePath=Path.GetFullPath(file);
        OwnFilePath=Path.Combine(Path.GetDirectoryName(FilePath)!,"sessions",Owner+".json");
        var key=OperatingSystem.IsWindows()?FilePath.ToUpperInvariant():FilePath;
        mutexName=(OperatingSystem.IsWindows()?"Local\\":"")+"LiveryStudio.Session."+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }

    public EditorSession? Read()=>System.IO.File.Exists(FilePath)?ReadFile(FilePath):null;

    public bool Save(EditorSession session,bool claim=true)
    {
        ArgumentNullException.ThrowIfNull(session.Project);
        var bytes=JsonSerializer.SerializeToUtf8Bytes(session with {SessionOwner=Owner},Contract.Json);
        using var gate=new Mutex(false,mutexName);var held=false;
        try
        {
            try{held=gate.WaitOne(TimeSpan.FromSeconds(10));}catch(AbandonedMutexException){held=true;}
            if(!held)throw new IOException("Another editor is still saving its draft. Please try again.");
            AtomicFiles.Write(OwnFilePath,bytes);
            // An old-format or externally replaced shared file is another owner too.
            // Preserve it, while retaining this window's fully flushed state above.
            if(!claim&&CurrentOwner()!=Owner)return false;
            AtomicFiles.Write(FilePath,bytes);return true;
        }
        finally{if(held)gate.ReleaseMutex();}
    }

    private string? CurrentOwner()
    {
        try{return Read()?.SessionOwner;}
        catch(Exception e) when(e is JsonException or InvalidDataException){return null;}
    }

    public IReadOnlyList<SavedEditorDraft> OtherDrafts()
    {
        var folder=Path.GetDirectoryName(OwnFilePath)!;if(!Directory.Exists(folder))return [];
        var drafts=new List<SavedEditorDraft>();
        foreach(var file in Directory.EnumerateFiles(folder,"*.json"))
        {
            if(string.Equals(file,OwnFilePath,StringComparison.OrdinalIgnoreCase))continue;
            try
            {
                var session=ReadFile(file);
                if(session.Project.CarIndex is >=0 and <27)drafts.Add(new(file,session,System.IO.File.GetLastWriteTimeUtc(file)));
            }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException){ }
        }
        return drafts.OrderByDescending(d=>d.SavedAt).ToArray();
    }

    private static EditorSession ReadFile(string file)
    {
        var state=JsonSerializer.Deserialize<EditorSession>(System.IO.File.ReadAllText(file),Contract.Json)??throw new InvalidDataException("The saved session is empty.");
        return state.Project is not null?state:throw new InvalidDataException("The saved session has no design.");
    }
}

// The WinUI Closing event is synchronous: cancel it until the pending editor
// work and session have actually finished, then request the final close.
public sealed class EditorShutdown
{
    public bool IsClosing {get;private set;}
    public bool Ready {get;private set;}
    public async Task CloseAsync(Func<Task> flush,Action save,Action close)
    {
        if(IsClosing||Ready)return;
        IsClosing=true;
        try{await flush();save();Ready=true;close();}
        catch{IsClosing=false;Ready=false;throw;}
    }
}
