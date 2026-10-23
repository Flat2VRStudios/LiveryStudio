using System.Text;
using LiveryStudio;
using LiveryStudio.Core;
using Windows.Graphics.Imaging;
using Windows.Storage;

internal static class PhotoEncodingChecks
{
    public static async Task Run(string root,Action<bool,string> check)
    {
        PaintImage Frame(int i)=>new(64,64,Enumerable.Range(0,64*64).SelectMany(p=>new byte[]{(byte)(p%64*4),(byte)(i*28),(byte)(p/64*4),255}).ToArray());
        var png=Path.Combine(root,"photo.png");await PhotoEncoding.WriteAsync(png,false,1,_=>Task.FromResult(Frame(2)));
        check((await ImageFiles.ReadAsync(png)).Pixels.SequenceEqual(Frame(2).Pixels),"photo PNG retains exact full-color GPU-style pixels");
        var gif=Path.Combine(root,"turntable.gif");await PhotoEncoding.WriteAsync(gif,true,8,i=>Task.FromResult(Frame(i)));
        using(var stream=await (await StorageFile.GetFileFromPathAsync(gif)).OpenReadAsync())
        {
            var decoder=await BitmapDecoder.CreateAsync(stream);check(decoder.FrameCount==8,"turntable GIF contains every expected frame without a trailing empty frame");
            var loop=await decoder.BitmapContainerProperties.GetPropertiesAsync(["/appext/Application","/appext/Data"]);
            Console.WriteLine("GIF loop metadata: "+Encoding.ASCII.GetString((byte[])loop["/appext/Application"].Value)+" / "+Convert.ToHexString((byte[])loop["/appext/Data"].Value));
            var loopExtension=new byte[]{0x21,0xff,0x0b}.Concat(Encoding.ASCII.GetBytes("NETSCAPE2.0")).Concat(new byte[]{3,1,0,0,0}).ToArray();
            check(Encoding.ASCII.GetString((byte[])loop["/appext/Application"].Value)=="NETSCAPE2.0"&&File.ReadAllBytes(gif).AsSpan().IndexOf(loopExtension)>=0,"turntable GIF has a valid infinite-loop application extension on disk");
            var duration=0;var hashes=new HashSet<string>();
            for(uint i=0;i<decoder.FrameCount;i++)
            {
                var frame=await decoder.GetFrameAsync(i);var properties=await frame.BitmapProperties.GetPropertiesAsync(["/grctlext/Delay"]);duration+=Convert.ToInt32(properties["/grctlext/Delay"].Value);
                var pixels=(await frame.GetPixelDataAsync()).DetachPixelData();hashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels)));
            }
            check(duration==600&&hashes.Count==8,"turntable GIF has six seconds of distinct timed frames");
        }
        var target=Path.Combine(root,"protected.gif");var original=new byte[]{17,42,93};File.WriteAllBytes(target,original);
        using var cancellation=new CancellationTokenSource();var canceled=false;
        try{await PhotoEncoding.WriteAsync(target,true,8,i=>{if(i==2)cancellation.Cancel();return Task.FromResult(Frame(i));},cancellation.Token);}catch(OperationCanceledException){canceled=true;}
        check(canceled&&File.ReadAllBytes(target).SequenceEqual(original),"canceling an export preserves an existing destination byte for byte");
        var failed=false;try{await PhotoEncoding.WriteAsync(target,true,8,i=>i==2?throw new InvalidDataException("lost frame"):Task.FromResult(Frame(i)));}catch(InvalidDataException){failed=true;}
        check(failed&&File.ReadAllBytes(target).SequenceEqual(original),"failed frame generation preserves the existing destination");
        failed=false;try{await PhotoEncoding.WriteAsync(target,true,8,i=>Task.FromResult(i==2?new PaintImage(1,1,[0,0,0,255]):Frame(i)));}catch(InvalidDataException){failed=true;}
        check(failed&&File.ReadAllBytes(target).SequenceEqual(original),"inconsistent animation dimensions cannot replace the destination");
        check(Directory.GetFiles(root,"*.tmp").Length==0,"photo success cancellation and failure all remove only their own staging files");
    }
}
