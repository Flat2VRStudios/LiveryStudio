using System.Numerics;
using System.Text.Json;
using LiveryStudio.Core;

internal static class ArtworkBrowsingChecks
{
    public static void Run(string temp,Action<bool,string> check,Action<Action,string> reject)
    {
        var workspace=Path.Combine(temp,"artwork-browser");var folder=ArtworkLibrary.Folder(workspace);Directory.CreateDirectory(folder);
        var files=Enumerable.Range(0,60).Select(i=>Path.Combine(folder,$"Team badge {i:00}-ABCDEF12.tga")).ToArray();
        foreach(var file in files)FinishMaps.WriteTga(new(1,1,[60,120,200,128]),file);
        var sourceBytes=files.ToDictionary(p=>p,File.ReadAllBytes);
        var group=ReusableArtwork.Save("Two-piece badge",[
            new(){Kind=LayerKind.Sticker,Name="Badge",Asset=files[0],Samples=[new(Vector3.Zero,Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,1,1)]},
            new(){Kind=LayerKind.Color,Name="Circle",Color="#FF3300",Samples=[new(new(.25f,0,0),Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,.3f,.3f)]}],workspace);
        var groupBytes=File.ReadAllBytes(group);var catalog=ArtworkBrowsing.Catalog(workspace);var state=ArtworkBrowsing.Read(workspace);
        check(catalog.Count==61&&catalog.Count(x=>x.IsGroup)==1,"Artwork browser combines all decals and editable groups without a hidden count limit");
        check(catalog.Single(x=>x.IsGroup).Name=="Two-piece badge"&&catalog[0].Name.Contains("Team badge"),"Artwork browser keeps readable artwork and group names");
        check(state.Favorites.Count==0&&state.Recent.Count==0,"A new artwork collection starts without favorites or fake recent uses");
        ArtworkBrowsing.Favorite(workspace,files[3],true);ArtworkBrowsing.Favorite(workspace,group,true);ArtworkBrowsing.Favorite(workspace,files[3],true);
        state=ArtworkBrowsing.Read(workspace);
        check(state.Favorites.Count==2,"Pinning a decal twice and a group retains two independent favorites");
        check(ArtworkBrowsing.Query(catalog,state,"",ArtworkCollection.Favorites,ArtworkCategory.All).Count==2,"Pinned view includes both image and editable artwork");
        check(ArtworkBrowsing.Query(catalog,state,"",ArtworkCollection.Favorites,ArtworkCategory.Groups).Single().Path==group,"Pinned and type filters combine for editable groups");
        check(ArtworkBrowsing.Query(catalog,state,"TEAM 03",ArtworkCollection.All,ArtworkCategory.Decals).Single().Path==files[3],"Artwork search combines case-insensitive words across readable names");
        check(ArtworkBrowsing.Query(catalog,state,"editable",ArtworkCollection.All,ArtworkCategory.All).Single().Path==group,"Artwork search includes type descriptions");
        check(ArtworkBrowsing.Query(catalog,state,"not present",ArtworkCollection.All,ArtworkCategory.All).Count==0,"Artwork search has an honest empty result");
        ArtworkBrowsing.Use(workspace,files[3]);ArtworkBrowsing.Use(workspace,group);ArtworkBrowsing.Use(workspace,files[3]);state=ArtworkBrowsing.Read(workspace);
        check(state.Recent.Count==2&&ArtworkBrowsing.Query(catalog,state,"",ArtworkCollection.Recent,ArtworkCategory.All)[0].Path==files[3],"Recent artwork records successful choices in recency order without duplicates");
        ArtworkBrowsing.Favorite(workspace,files[3],false);state=ArtworkBrowsing.Read(workspace);
        check(state.Favorites.Count==1&&state.Recent.Count==2&&ArtworkBrowsing.Query(catalog,state,"",ArtworkCollection.All,ArtworkCategory.All).Count==61,"Unpinning keeps artwork and recent uses available");
        foreach(var file in files)ArtworkBrowsing.Use(workspace,file);state=ArtworkBrowsing.Read(workspace);
        check(state.Recent.Count==48&&state.Recent[0]==ArtworkBrowsing.Key(workspace,files[^1])&&!state.Recent.Contains(ArtworkBrowsing.Key(workspace,files[0])),"Recent list stays bounded at 48 most recent unique choices");
        Parallel.ForEach(files.Take(16),file=>ArtworkBrowsing.Favorite(workspace,file,true));state=ArtworkBrowsing.Read(workspace);
        check(state.Favorites.Count==17,"Concurrent editor pin operations preserve every favorite");
        check(state.Favorites.All(k=>!Path.IsPathRooted(k)&&!k.Contains('\\')),"Artwork choices use portable workspace-relative identifiers");
        var relocated=Path.Combine(temp,"relocated-browser");Directory.CreateDirectory(relocated);File.Copy(Path.Combine(workspace,"artwork-browsing.json"),Path.Combine(relocated,"artwork-browsing.json"));
        var relocatedCatalog=catalog.Select(a=>a with {Path=Path.Combine(relocated,a.Key.Replace('/',Path.DirectorySeparatorChar))}).ToArray();
        check(ArtworkBrowsing.Query(relocatedCatalog,ArtworkBrowsing.Read(relocated),"",ArtworkCollection.Favorites,ArtworkCategory.All).Count==17,"Artwork preferences match the same relative catalog after moving a workspace");
        foreach(var key in new[]{"../escape.png","artwork/../escape.png","artwork/..","artwork/x.svg","projects/a.tga","artwork/x.tga:stream","artwork\\x.tga","/artwork/x.tga"})
            reject(()=>new ArtworkBrowsingState{Favorites=[key]}.Validate(),"Reject unsafe or unsupported artwork favorite key: "+key);
        reject(()=>new ArtworkBrowsingState{Recent=["artwork/A.tga","artwork/a.tga"]}.Validate(),"Reject duplicate saved artwork choices regardless of case");
        reject(()=>ArtworkBrowsing.Favorite(workspace,Path.Combine(temp,"outside.tga"),true),"Artwork preferences cannot refer outside library folders");
        reject(()=>ArtworkBrowsing.Use(workspace,Path.Combine(folder,"missing.tga")),"Missing artwork does not become a recent choice");
        var statePath=Path.Combine(workspace,"artwork-browsing.json");var valid=File.ReadAllBytes(statePath);File.WriteAllText(statePath,"{broken");
        reject(()=>ArtworkBrowsing.Favorite(workspace,files[1],true),"Malformed saved library choices are reported instead of replaced");
        check(File.ReadAllText(statePath)=="{broken","A failed favorite operation preserves corrupt choices for recovery");File.WriteAllBytes(statePath,valid);
        File.WriteAllText(files[0]+".text.json","{broken");
        check(ArtworkBrowsing.Catalog(workspace).Single(x=>x.Path==files[0]).Description.Contains("unavailable"),"Invalid source metadata does not hide an otherwise available decal");
        var beforeProjects=Directory.Exists(Path.Combine(workspace,"projects"))?Directory.GetDirectories(Path.Combine(workspace,"projects")).Length:0;
        var thumbnail=ReusableArtwork.PreviewFile(group);var layers=ReusableArtwork.Open(group,Path.Combine(temp,"browser-preview-reference"));var expected=ReusableArtwork.Preview(layers,128);
        check(thumbnail.Width==expected.Width&&thumbnail.Height==expected.Height&&thumbnail.Pixels.SequenceEqual(expected.Pixels),"Group shelf thumbnail matches the existing editable-group preview pixels");
        check((Directory.Exists(Path.Combine(workspace,"projects"))?Directory.GetDirectories(Path.Combine(workspace,"projects")).Length:0)==beforeProjects,"Generating group thumbnails creates no project or draft in the user's workspace");
        var previewRoot=Path.Combine(Path.GetTempPath(),"LiveryStudioArtworkPreview");var before=Directory.GetDirectories(previewRoot).Order().ToArray();var bad=Path.Combine(temp,"bad.flart");File.WriteAllText(bad,"not a zip");
        reject(()=>ReusableArtwork.PreviewFile(bad),"A broken group thumbnail is reported");
        check(Directory.GetDirectories(previewRoot).Order().SequenceEqual(before),"Failed group thumbnails clean up their own temporary extraction");
        check(sourceBytes.All(kv=>File.ReadAllBytes(kv.Key).SequenceEqual(kv.Value))&&File.ReadAllBytes(group).SequenceEqual(groupBytes),"Browse pin query and thumbnail operations preserve every artwork and group source byte");
        check(!Directory.Exists(Path.Combine(workspace,"projects")),"Artwork browsing never creates editor history or a project");
    }
}
