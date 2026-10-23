using LiveryStudio.Core;
using System.Text.Json;

internal static class SessionChecks
{
    internal static void Run(string root,Action<bool,string> check)
    {
        var folder=Path.Combine(root,"sessions");Directory.CreateDirectory(folder);var path=Path.Combine(folder,"session.json");
        var first=new EditorSessionStore(path);var second=new EditorSessionStore(path);
        var old=new EditorSession(new(){Name="Earlier design",Description="Earlier window fields"},"tools","game","previous",3);
        var latest=old with{Project=old.Project with{Name="Latest design",ArtworkGloss=.17},Workspace=0};
        check(first.Read() is null&&first.OtherDrafts().Count==0,"No saved session or recovery drafts has an empty state");
        var legacy=JsonSerializer.SerializeToNode(old,Contract.Json)!.AsObject();legacy.Remove("AdvancedMode");legacy.Remove("SessionOwner");
        File.WriteAllText(path,legacy.ToJsonString(Contract.Json));
        check(first.Read()?.AdvancedMode==false,"Existing sessions default to the beginner interface");
        first.Save(old with {AdvancedMode=true});
        check(first.Read()?.AdvancedMode==true,"Advanced mode preference survives session save and reopen");
        File.WriteAllText(path,JsonSerializer.Serialize(old,Contract.Json));
        check(first.Read()?.Project.Name==old.Project.Name&&first.Read()?.SessionOwner is null,"Original session schema restores without an ownership field");
        check(first.Save(old)&&first.Read()?.SessionOwner==first.Owner,"Normal saving claims the startup session for this window");
        check(first.Save(old with{Workspace=2},claim:false)&&first.Read()?.Workspace==2,"The owning window may finish saving when it closes");
        check(second.Save(latest),"A newer working window can become the session owner");var expected=File.ReadAllBytes(path);
        var finalOld=old with{Project=old.Project with{Name="Earlier finished draft",Description="Pending fields flushed at close"}};
        check(!first.Save(finalOld,claim:false)&&File.ReadAllBytes(path).SequenceEqual(expected),"Closing an older window leaves the newer startup session byte-for-byte unchanged");
        var retained=JsonSerializer.Deserialize<EditorSession>(File.ReadAllText(first.OwnFilePath),Contract.Json)!;
        check(retained.Project.Name==finalOld.Project.Name&&retained.Project.Description==finalOld.Project.Description,"The denied shared write still preserves the closing window's finished form fields");
        var other=second.OtherDrafts();check(other.Count==1&&other[0].Session.Project.Name==finalOld.Project.Name&&other[0].File==first.OwnFilePath,"Recovery lists another window's last draft and excludes the current window");
        check(first.Save(old)&&first.Read()?.SessionOwner==first.Owner,"Editing an earlier window can deliberately reclaim the startup session");
        check(second.Save(old)&&!first.Save(old,claim:false)&&first.Read()?.SessionOwner==second.Owner,"Identical project content in two windows does not imply shared ownership");
        File.WriteAllText(path,JsonSerializer.Serialize(latest,Contract.Json));expected=File.ReadAllBytes(path);
        check(!first.Save(finalOld,claim:false)&&File.ReadAllBytes(path).SequenceEqual(expected),"A legacy writer replacing the shared session is respected on close");
        File.WriteAllText(path,"{broken");check(!first.Save(finalOld,claim:false)&&File.ReadAllText(path)=="{broken","An unreadable shared session is preserved and the closing draft remains recoverable");
        File.Delete(path);check(!first.Save(finalOld,claim:false)&&!File.Exists(path),"Closing a stale window does not recreate an externally removed startup session");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(first.OwnFilePath)!,"broken.json"),"{broken");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(first.OwnFilePath)!,"empty.json"),"{}");
        check(second.OtherDrafts().Count==1,"Malformed recovery entries do not hide the other recoverable draft");
        var incomplete=old with{Project=old.Project with{Name="",Albedo="missing.png",OuterColor="#"}};first.Save(incomplete);
        check(second.OtherDrafts().Single().Session.Project.OuterColor=="#","Unfinished names, colors and missing assets are retained for recovery");
        var racePassed=true;
        for(var i=0;i<30;i++)
        {
            first.Save(old);
            Task.WaitAll(Task.Run(()=>first.Save(finalOld,claim:false)),Task.Run(()=>second.Save(latest)));
            racePassed&=first.Read()?.SessionOwner==second.Owner&&first.Read()?.Project.Name==latest.Project.Name;
        }
        check(racePassed,"Racing old-window closes and new-window saves always retain the new owner's complete session");

        var shutdown=new EditorShutdown();var held=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var saves=0;var closes=0;
        var pending=shutdown.CloseAsync(()=>held.Task,()=>saves++,()=>closes++);
        check(shutdown.IsClosing&&!shutdown.Ready&&saves==0&&closes==0,"Closing waits for pending editor work before saving or closing the window");
        shutdown.CloseAsync(()=>throw new Exception("Unexpected second flush"),()=>saves++,()=>closes++).GetAwaiter().GetResult();
        check(saves==0&&closes==0,"Repeated close requests do not flush or close twice");held.SetResult();pending.GetAwaiter().GetResult();
        check(shutdown.Ready&&saves==1&&closes==1,"A completed flush saves once and requests the final close once");
        shutdown=new();var failed=false;
        try{shutdown.CloseAsync(()=>Task.FromException(new IOException("Save failed")),()=>saves++,()=>closes++).GetAwaiter().GetResult();}catch(IOException){failed=true;}
        check(failed&&!shutdown.IsClosing&&!shutdown.Ready&&closes==1,"A failed editor flush leaves the window open for repair and retry");
        failed=false;try{shutdown.CloseAsync(()=>Task.CompletedTask,()=>throw new IOException("Session failed"),()=>closes++).GetAwaiter().GetResult();}catch(IOException){failed=true;}
        check(failed&&!shutdown.Ready&&closes==1,"A failed session save cannot close the window");
        shutdown.CloseAsync(()=>Task.CompletedTask,()=>saves++,()=>closes++).GetAwaiter().GetResult();
        check(shutdown.Ready&&closes==2,"Closing can be retried after a save failure");
        first.Save(old);second.Save(latest);expected=File.ReadAllBytes(path);shutdown=new();
        shutdown.CloseAsync(()=>{first.Save(finalOld,claim:!shutdown.IsClosing);return Task.CompletedTask;},()=>first.Save(finalOld,claim:!shutdown.IsClosing),()=>{}).GetAwaiter().GetResult();
        check(File.ReadAllBytes(path).SequenceEqual(expected),"Save callbacks during close-time flush cannot reclaim a newer session");
    }
}
