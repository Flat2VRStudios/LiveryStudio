using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LiveryStudio.Core;

public static class PhotoSheet
{
    [SupportedOSPlatform("windows")]
    public static PaintImage Render(string title,string car,int width,int height,IReadOnlyList<PhotoTile> tiles,Func<PhotoTile,PaintImage> capture,CancellationToken cancellation=default)
    {
        PhotoLayout.ValidateSize(width,height);if(tiles.Count!=4)throw new ArgumentException("A four-angle sheet needs four views.");
        using var bitmap=new Bitmap(width,height,PixelFormat.Format32bppArgb);using var graphics=Graphics.FromImage(bitmap);
        graphics.Clear(Color.FromArgb(15,18,24));graphics.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;graphics.SmoothingMode=SmoothingMode.AntiAlias;
        var margin=Math.Max(8,width/80);using var ink=new SolidBrush(Color.FromArgb(239,241,245));using var muted=new SolidBrush(Color.FromArgb(165,176,193));using var amber=new SolidBrush(Color.FromArgb(255,185,0));
        using var titleFont=new Font("Segoe UI",Math.Max(12,height/34f),FontStyle.Bold,GraphicsUnit.Pixel);using var captionFont=new Font("Segoe UI",Math.Max(9,height/55f),FontStyle.Regular,GraphicsUnit.Pixel);
        using var format=new StringFormat{Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap};
        graphics.FillRectangle(amber,margin,margin,Math.Max(3,width/500),Math.Max(24,height/22));
        graphics.DrawString(title,titleFont,ink,new RectangleF(margin*2,margin-2,width-margin*3,height/22f),format);
        graphics.DrawString(car+"  /  FOUR ANGLES",captionFont,muted,new RectangleF(margin*2,margin+height/24f,width-margin*3,height/28f),format);
        foreach(var tile in tiles)
        {
            cancellation.ThrowIfCancellationRequested();
            if(tile.X<0||tile.Y<0||tile.Width<1||tile.Height<1||tile.X+tile.Width>width||tile.Y+tile.Height>height)throw new ArgumentException("A photo tile is outside the sheet.");
            var image=capture(tile);image.Validate();if(image.Width!=tile.Width||image.Height!=tile.Height)throw new ArgumentException("The photo size does not match its tile.");
            using var view=new Bitmap(image.Width,image.Height,PixelFormat.Format32bppArgb);var data=view.LockBits(new(0,0,view.Width,view.Height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
            try
            {
                var row=new byte[image.Width*4];for(var y=0;y<image.Height;y++)
                {
                    if((y&31)==0)cancellation.ThrowIfCancellationRequested();var at=y*image.Width*4;
                    for(var x=0;x<image.Width;x++){var k=x*4;row[k]=image.Pixels[at+k+2];row[k+1]=image.Pixels[at+k+1];row[k+2]=image.Pixels[at+k];row[k+3]=image.Pixels[at+k+3];}
                    Marshal.Copy(row,0,data.Scan0+y*data.Stride,row.Length);
                }
            }
            finally{view.UnlockBits(data);}
            graphics.DrawImageUnscaled(view,tile.X,tile.Y);
            graphics.DrawString(tile.Label,captionFont,muted,new RectangleF(tile.X,tile.Y+tile.Height+2,tile.Width,Math.Max(18,height/40)),format);
        }
        cancellation.ThrowIfCancellationRequested();var output=new byte[checked(width*height*4)];var read=bitmap.LockBits(new(0,0,width,height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        try
        {
            var row=new byte[width*4];for(var y=0;y<height;y++){if((y&31)==0)cancellation.ThrowIfCancellationRequested();Marshal.Copy(read.Scan0+y*read.Stride,row,0,row.Length);for(var x=0;x<width;x++){var at=(y*width+x)*4;var k=x*4;output[at]=row[k+2];output[at+1]=row[k+1];output[at+2]=row[k];output[at+3]=row[k+3];}}
        }
        finally{bitmap.UnlockBits(read);}return new(width,height,output);
    }
}
