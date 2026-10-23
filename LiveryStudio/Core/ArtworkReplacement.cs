namespace LiveryStudio.Core;

public enum ArtworkReplacementFit { ExistingBox, PreserveProportions }

public static class ArtworkReplacement
{
    public static string Import(string source,string workspace)
    {
        FinishMaps.Read(source).Validate();
        var text=TextArtwork.ReadMetadata(source);var pattern=PatternArtwork.ReadMetadata(source);
        var vector=VectorArtwork.ReadMetadata(source);var raster=ArtworkColors.ReadMetadata(source);
        var recipes=new object?[]{text,pattern,vector,raster}.Count(value=>value is not null);
        if(recipes>1)throw new InvalidDataException("The replacement has conflicting editable artwork recipes.");
        if(recipes==0)return ArtworkLibrary.Add(source,workspace);
        var folder=ArtworkLibrary.Folder(workspace);Directory.CreateDirectory(folder);
        var key=Guid.NewGuid().ToString("N");var target=Path.Combine(folder,Path.GetFileNameWithoutExtension(source)[..Math.Min(70,Path.GetFileNameWithoutExtension(source).Length)]+"-"+key+Path.GetExtension(source));
        // Capture referenced originals before publishing the replacement image.
        // Each imported recipe has a unique immutable name, even if two recipes
        // happen to generate the same pixel bytes.
        var retained=Path.Combine(workspace,"retained-artwork",key);
        if(vector is not null)
        {
            if(new FileInfo(vector.SourceFile).Length>VectorArtwork.MaximumBytes)throw new InvalidDataException("The original SVG is too large.");
            var bytes=File.ReadAllBytes(vector.SourceFile);_=VectorArtwork.Validate(bytes);
            Directory.CreateDirectory(retained);var original=Path.Combine(retained,Path.GetFileName(vector.SourceFile));AtomicFiles.Write(original,bytes);vector=vector with {SourceFile=original};
        }
        if(raster is not null)
        {
            FinishMaps.Read(raster.SourceFile).Validate();Directory.CreateDirectory(retained);
            var original=Path.Combine(retained,"original"+Path.GetExtension(raster.SourceFile));File.Copy(raster.SourceFile,original);raster=raster with {SourceFile=original};
        }
        void Metadata(string suffix,object value)=>AtomicFiles.Write(target+suffix,System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value,Contract.Json));
        if(text is not null)Metadata(".text.json",text);if(pattern is not null)Metadata(".pattern.json",pattern);
        if(vector is not null)Metadata(".vector.json",vector);if(raster is not null)Metadata(".raster.json",raster);
        File.Copy(source,target);return target;
    }

    public static PaintLayer Create(PaintLayer layer,string asset,ArtworkReplacementFit fit=ArtworkReplacementFit.ExistingBox)
    {
        if(layer.Kind!=LayerKind.Sticker)throw new InvalidOperationException("Select a decal to replace its artwork.");
        LayerSets.RequireEditable([layer]);
        if(!Enum.IsDefined(fit))throw new ArgumentOutOfRangeException(nameof(fit));
        var image=FinishMaps.Read(asset);image.Validate();
        var replacement=layer with
        {
            Asset=asset,TextArtwork=TextArtwork.ReadMetadata(asset),PatternArtwork=PatternArtwork.ReadMetadata(asset),
            VectorArtwork=VectorArtwork.ReadMetadata(asset),RasterArtwork=ArtworkColors.ReadMetadata(asset),
            ColorLinks=[],Samples=layer.Samples.ToList(),Triangles=layer.Triangles.ToArray()
        };
        if(fit==ArtworkReplacementFit.PreserveProportions)
        {
            // Keep every mapped center, angle and available box. Fit each stamp's
            // new image inside that box, rather than guessing from source pixels.
            replacement=LayerSets.Canonical(replacement);
            var aspect=(float)image.Width/image.Height;
            replacement.Samples=replacement.Samples.Select(p=>p with
            {
                Width=MathF.Min(p.Width,p.Height*aspect),Height=MathF.Min(p.Height,p.Width/aspect)
            }).ToList();
        }
        replacement.Validate();return replacement;
    }

    public static void Apply(LayerDocument document,Guid id,string asset,ArtworkReplacementFit fit=ArtworkReplacementFit.ExistingBox)
    {
        var source=document.Layers.SingleOrDefault(l=>l.Id==id)??throw new InvalidOperationException("The selected decal is no longer in this design.");
        var replacement=Create(source,asset,fit);
        // Old links describe the former artwork. Only matching fields and colors
        // from the new retained recipe may join existing design colors.
        DesignPalette.AttachUniqueLinks(replacement,document.History[document.Cursor].Palette);
        document.Commit("Replaced artwork",layers=>layers[layers.FindIndex(l=>l.Id==id)]=replacement);
    }
}
