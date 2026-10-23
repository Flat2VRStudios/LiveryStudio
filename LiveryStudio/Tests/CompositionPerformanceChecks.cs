using System.Numerics;
using System.Diagnostics;
using LiveryStudio.Core;

internal static class CompositionPerformanceChecks
{
    public static void Run(string temp,Action<bool,string> check)
    {
        var folder=Path.Combine(temp,"composition-performance");Directory.CreateDirectory(folder);
        byte[] Header(byte type,byte depth,byte flags=32){var h=new byte[18];h[2]=type;h[12]=2;h[14]=1;h[16]=depth;h[17]=flags;return h;}
        var gray=TgaDecoder.Decode([..Header(3,8),15,200]);
        check(gray.Pixels.SequenceEqual(new byte[]{15,15,15,255,200,200,200,255}),"Packed TGA decoding retains 8-bit grayscale RGBA channels");
        var grayAlpha=TgaDecoder.Decode([..Header(11,16),1,40,70,220,130]);
        check(grayAlpha.Pixels.SequenceEqual(new byte[]{40,40,40,70,220,220,220,130}),"Packed TGA decoding retains RLE grayscale alpha");
        var shortColor=TgaDecoder.Decode([..Header(2,16,33),0,124,224,131]);
        check(shortColor.Pixels.SequenceEqual(new byte[]{255,0,0,0,0,255,0,255}),"Packed TGA decoding preserves 16-bit color and attribute alpha");
        var indexedHeader=Header(1,8);indexedHeader[1]=1;indexedHeader[3]=10;indexedHeader[5]=2;indexedHeader[7]=24;
        var indexed=TgaDecoder.Decode([..indexedHeader,3,2,1,6,5,4,11,10]);
        check(indexed.Pixels.SequenceEqual(new byte[]{4,5,6,255,1,2,3,255}),"Packed indexed TGA decoding retains palette offset and BGR conversion");
        var rleIndexedHeader=(byte[])indexedHeader.Clone();rleIndexedHeader[2]=9;rleIndexedHeader[16]=16;
        check(TgaDecoder.Decode([..rleIndexedHeader,3,2,1,6,5,4,129,11,0]).Pixels.SequenceEqual(new byte[]{4,5,6,255,4,5,6,255}),"Packed RLE TGA decoding retains 16-bit palette indices");
        var bottomRight=Header(2,24,16);bottomRight[12]=1;bottomRight[14]=2;
        check(TgaDecoder.Decode([..bottomRight,3,2,1,6,5,4]).Pixels.SequenceEqual(new byte[]{4,5,6,255,1,2,3,255}),"Packed TGA decoding preserves bottom-origin row order");
        var p=new SurfaceProjection(Vector3.Zero,Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,1,1);
        var mesh=new PaintMesh([
            -.5f,.5f,0,0,0,1,0,0,1, .5f,.5f,0,0,0,1,1,0,1, -.5f,-.5f,0,0,0,1,0,1,1,
            .5f,.5f,0,0,0,1,1,0,1, .5f,-.5f,0,0,0,1,1,1,1, -.5f,-.5f,0,0,0,1,0,1,1]);
        PaintImage Solid(int size,byte r,byte a=255){var pixels=new byte[size*size*4];for(var i=0;i<pixels.Length;i+=4){pixels[i]=r;pixels[i+3]=a;}return new(size,size,pixels);}
        var source=Solid(8,210);var reads=0;PaintImage Read(string _){reads++;return source;}
        var path=Path.Combine(folder,"source.tga");FinishMaps.WriteTga(source,path);
        var cache=new DecalImageCache(read:Read);var oval=new DecalEffectsOptions{Mask=DecalMask.Oval};
        var original=cache.Get(path);var masked=cache.Get(path,oval);
        check(reads==1&&ReferenceEquals(original,cache.Get(path))&&ReferenceEquals(masked,cache.Get(path,oval with {Blend=DecalBlend.Screen})),"Composition cache reuses original and masked pixels across repeated decals and blend modes");
        var shifted=cache.Get(path,oval with {CenterX=0});
        check(!ReferenceEquals(masked,shifted)&&!masked.Pixels.SequenceEqual(shifted.Pixels),"Distinct stencil recipes keep distinct composed images");
        var stencilPath=Path.Combine(folder,"stencil.tga");FinishMaps.WriteTga(Solid(8,100),stencilPath);reads=0;
        cache=new(read:Read);cache.Get(path,new(){Mask=DecalMask.Image,StencilFile=stencilPath});cache.Get(path,new(){Mask=DecalMask.Image,StencilFile=stencilPath,CenterX=.6f});
        check(reads==2,"Different image stencil recipes share decoded original and stencil pixels");
        cache=new(budget:source.Pixels.Length*2,read:Read);reads=0;
        cache.Get(path);cache.Get(Path.Combine(folder,"b"));cache.Get(path);cache.Get(Path.Combine(folder,"c"));cache.Get(path);var before=reads;cache.Get(Path.Combine(folder,"b"));
        check(before==3&&reads==4&&cache.RetainedBytes==source.Pixels.Length*2,"Image retention stays within its pixel budget and evicts the least recently used source");
        cache=new(budget:source.Pixels.Length-1,read:Read);reads=0;cache.Get(path);cache.Get(path);
        check(reads==2&&cache.RetainedBytes==0,"Images larger than the cache budget remain usable without being retained");
        cache=new(budget:0,read:Read);cache.Get(path);check(cache.RetainedBytes==0,"A zero image budget retains no pixels");
        var diskCache=new DecalImageCache();var old=diskCache.Get(path);FinishMaps.WriteTga(Solid(8,70),path);var fresh=new DecalImageCache().Get(path);
        check(old.Pixels[0]==210&&fresh.Pixels[0]==70,"Each new composition reads changed source artwork instead of keeping a stale global cache");
        using(var canceled=new CancellationTokenSource())
        {
            canceled.Cancel();reads=0;cache=new(canceled.Token,read:Read);var untouched=Solid(8,0);var beforePixels=(byte[])untouched.Pixels.Clone();
            bool Stops(Action action){try{action();return false;}catch(OperationCanceledException x){return x.CancellationToken==canceled.Token;}}
            check(Stops(()=>cache.Get(path))&&reads==0,"Canceled compositions avoid image decoding and caching");
            var painter=new TexturePainter(untouched);check(Stops(()=>painter.Stamp(mesh,p,source,Vector3.One,1,Vector3.Zero,cancellation:canceled.Token))&&painter.Image.Pixels.SequenceEqual(beforePixels)&&!painter.HasPending,"A canceled stamp does not touch its destination or undo state");
            check(Stops(()=>MeshEditing.UvCoverage(mesh,8,8,canceled.Token)),"Canceled UV coverage stops before allocating its map");
            check(Stops(()=>MeshEditing.Bleed(untouched,new bool[64],2,canceled.Token))&&untouched.Pixels.SequenceEqual(beforePixels),"Canceled seam padding preserves an untouched image");
            check(Stops(()=>new LayerDocument().Compose(mesh,new(),canceled.Token)),"Canceled composition stops before validating or loading missing files");
            check(Stops(()=>TgaDecoder.Decode([],canceled.Token)),"Canceled TGA decoding stops before header parsing and output allocation");
        }
        using(var mid=new CancellationTokenSource())
        {
            var painter=new TexturePainter(Solid(32,0));var cancelsOnTriangle=new CancelingSet(mid);
            var stopped=false;try{painter.Stamp(mesh,p,source,Vector3.One,1,Vector3.Zero,triangles:cancelsOnTriangle,cancellation:mid.Token);}catch(OperationCanceledException){stopped=true;}
            check(stopped&&!painter.HasPending,"Cancellation arriving inside a triangle stops at the next raster row");
        }
        void MidWork(string name,Action<CancellationToken> action)
        {
            using var token=new CancellationTokenSource();token.CancelAfter(10);var watch=Stopwatch.StartNew();var stopped=false;
            try{action(token.Token);}catch(OperationCanceledException){stopped=true;}
            check(stopped&&watch.Elapsed<TimeSpan.FromSeconds(5),name+" responds to cancellation during a 4096px operation ("+watch.ElapsedMilliseconds+" ms)");
        }
        MidWork("UV coverage",token=>MeshEditing.UvCoverage(mesh,4096,4096,token));
        var large=Solid(4096,0);var coverage=new bool[4096*4096];coverage[0]=true;
        MidWork("Seam padding",token=>MeshEditing.Bleed(large,coverage,8,token));
        var largePath=Path.Combine(folder,"repeated.tga");FinishMaps.WriteTga(Solid(1024,210),largePath);
        var encoded=File.ReadAllBytes(largePath);var decodeBefore=GC.GetAllocatedBytesForCurrentThread();var decoded=TgaDecoder.Decode(encoded);var decodeBytes=GC.GetAllocatedBytesForCurrentThread()-decodeBefore;
        check(decoded.Pixels[0]==210&&decoded.Pixels.Length==1024*1024*4&&decodeBytes<5*1024*1024,"1024px TGA decoding allocates only its RGBA output rather than one object per texel ("+decodeBytes+" bytes)");
        var largeHeader=Header(2,32);largeHeader[12]=0;largeHeader[13]=16;largeHeader[14]=0;largeHeader[15]=16;var largeEncoded=new byte[18+4096*4096*4];largeHeader.CopyTo(largeEncoded,0);
        MidWork("TGA decoding",token=>TgaDecoder.Decode(largeEncoded,token));
        var basePath=Path.Combine(folder,"base.tga");FinishMaps.WriteTga(Solid(32,20),basePath);
        var layers=Enumerable.Range(0,128).Select(i=>new PaintLayer{Name="Copy "+i,Asset=largePath,Effects=oval,Samples=[p with {Center=new(4,0,0)}]}).ToList();
        var doc=new LayerDocument{Version=7,BaseAlbedo=basePath,BaseStructure=basePath,SeamPadding=0,History=[new("Repeated logos",DateTimeOffset.UtcNow,layers)]};
        var project=new LiveryProject{Albedo=basePath,Structure=basePath};
        var allocatedBefore=GC.GetAllocatedBytesForCurrentThread();var result=doc.Compose(mesh,project);var allocated=GC.GetAllocatedBytesForCurrentThread()-allocatedBefore;
        check(result.Albedo.Pixels.SequenceEqual(FinishMaps.Read(basePath).Pixels)&&allocated<40*1024*1024,"Composing 128 copies of one masked 1024px image keeps allocation bounded ("+allocated+" bytes)");
        allocatedBefore=GC.GetAllocatedBytesForCurrentThread();var preview=ReusableArtwork.Preview(layers,32);allocated=GC.GetAllocatedBytesForCurrentThread()-allocatedBefore;
        check(preview.Pixels.Any(b=>b>0)&&allocated<40*1024*1024,"Reusable group preview shares masked image pixels across 128 copies ("+allocated+" bytes)");
    }
    private sealed class CancelingSet(CancellationTokenSource cancellation):IReadOnlySet<int>
    {
        public int Count=>1;
        public bool Contains(int item){cancellation.Cancel();return true;}
        public IEnumerator<int> GetEnumerator()=>((IEnumerable<int>)new[]{0}).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
        public bool IsProperSubsetOf(IEnumerable<int> other)=>throw new NotSupportedException();
        public bool IsProperSupersetOf(IEnumerable<int> other)=>throw new NotSupportedException();
        public bool IsSubsetOf(IEnumerable<int> other)=>throw new NotSupportedException();
        public bool IsSupersetOf(IEnumerable<int> other)=>throw new NotSupportedException();
        public bool Overlaps(IEnumerable<int> other)=>throw new NotSupportedException();
        public bool SetEquals(IEnumerable<int> other)=>throw new NotSupportedException();
    }
}
