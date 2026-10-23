using LiveryStudio.Core;
using Windows.Graphics.Imaging;
using Windows.Storage;
namespace LiveryStudio;
internal static class ImageFiles
{
    public static async Task<PaintImage> ReadAsync(string path)
    {
        if(Path.GetExtension(path).Equals(".tga",StringComparison.OrdinalIgnoreCase)){var t=TgaDecoder.Decode(File.ReadAllBytes(path));return new(t.Width,t.Height,t.Pixels);}
        var file=await StorageFile.GetFileFromPathAsync(path);using var stream=await file.OpenReadAsync();var decoder=await BitmapDecoder.CreateAsync(stream);
        if(decoder.PixelWidth>8192 || decoder.PixelHeight>8192)throw new InvalidDataException("Choose an image of at most 8192 × 8192.");
        var data=await decoder.GetPixelDataAsync(BitmapPixelFormat.Rgba8,BitmapAlphaMode.Straight,new BitmapTransform(),ExifOrientationMode.IgnoreExifOrientation,ColorManagementMode.DoNotColorManage);
        return new((int)decoder.PixelWidth,(int)decoder.PixelHeight,data.DetachPixelData());
    }
    public static async Task WriteAsync(PaintImage image,string path,bool overwrite=true)
    {
        image.Validate();if(!overwrite&&File.Exists(path))return;
        var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllBytes(temp,[]);
        try
        {
            var file=await StorageFile.GetFileFromPathAsync(temp);
            using(var stream=await file.OpenAsync(FileAccessMode.ReadWrite)){var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream);encoder.SetPixelData(BitmapPixelFormat.Rgba8,BitmapAlphaMode.Straight,(uint)image.Width,(uint)image.Height,96,96,image.Pixels);await encoder.FlushAsync();}
            // Content-addressed cache entries are immutable; another producer may have won the race.
            try{File.Move(temp,path,overwrite);}catch(IOException) when(!overwrite&&File.Exists(path)){}
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
