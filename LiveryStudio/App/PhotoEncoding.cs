using System.Text;
using LiveryStudio.Core;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace LiveryStudio;

internal static class PhotoEncoding
{
    public static async Task WriteAsync(string path,bool animated,int frames,Func<int,Task<PaintImage>> capture,CancellationToken cancellation=default,IProgress<int>? progress=null)
    {
        if(frames<1||frames>120||(!animated&&frames!=1)||(animated&&frames<2))throw new ArgumentException("Choose one PNG image or 2–120 animation frames.");
        var full=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(full)!);cancellation.ThrowIfCancellationRequested();var temp=full+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            File.WriteAllBytes(temp,[]);var file=await StorageFile.GetFileFromPathAsync(temp);
            using(var stream=await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                var encoder=await BitmapEncoder.CreateAsync(animated?BitmapEncoder.GifEncoderId:BitmapEncoder.PngEncoderId,stream);
                if(animated)await encoder.BitmapContainerProperties.SetPropertiesAsync(new BitmapPropertySet{
                    ["/appext/Application"]=new(Encoding.ASCII.GetBytes("NETSCAPE2.0"),PropertyType.UInt8Array),
                    ["/appext/Data"]=new(new byte[]{3,1,0,0,0},PropertyType.UInt8Array)});
                int width=0,height=0;
                for(var i=0;i<frames;i++)
                {
                    cancellation.ThrowIfCancellationRequested();var image=await capture(i);image.Validate();cancellation.ThrowIfCancellationRequested();
                    if(i==0){width=image.Width;height=image.Height;}else if(width!=image.Width||height!=image.Height)throw new InvalidDataException("All turntable frames must have the same size.");
                    if(animated&&(image.Width>1280||image.Height>1280))throw new InvalidDataException("Choose a turntable of at most 1,280 pixels per edge.");
                    encoder.SetPixelData(BitmapPixelFormat.Rgba8,BitmapAlphaMode.Ignore,(uint)image.Width,(uint)image.Height,96,96,image.Pixels);
                    if(animated)
                    {
                        var delay=(ushort)(Math.Round((i+1)*600d/frames)-Math.Round(i*600d/frames));
                        await encoder.BitmapProperties.SetPropertiesAsync(new BitmapPropertySet{
                            ["/grctlext/Delay"]=new(delay,PropertyType.UInt16),
                            ["/grctlext/Disposal"]=new((byte)1,PropertyType.UInt8),
                            ["/grctlext/TransparencyFlag"]=new(false,PropertyType.Boolean)});
                    }
                    cancellation.ThrowIfCancellationRequested();progress?.Report(i+1);
                    if(i<frames-1)await encoder.GoToNextFrameAsync();
                }
                await encoder.FlushAsync();await stream.FlushAsync();
            }
            cancellation.ThrowIfCancellationRequested();File.Move(temp,full,true);
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
