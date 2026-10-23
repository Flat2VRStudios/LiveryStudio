namespace LiveryStudio.Core;

public record TgaFrame(int Width,int Height,byte[] Pixels);
public static class TgaDecoder
{
	// Decode into top-left RGBA. Supports indexed, true-color, grayscale and RLE TGAs.
	public static TgaFrame Decode(byte[] data,CancellationToken cancellation=default)
	{
		cancellation.ThrowIfCancellationRequested();
		if(data.Length<18)throw new InvalidDataException("Incomplete TGA header.");
		int U16(int p)=>data[p]|data[p+1]<<8;
		int type=data[2],w=U16(12),h=U16(14),depth=data[16],flags=data[17];
		if(w<1||h<1||(long)w*h>67108864||!new[]{1,2,3,9,10,11}.Contains(type))throw new InvalidDataException("Unsupported TGA dimensions or image type.");
		int position=18+data[0],paletteFirst=U16(3),paletteCount=U16(5),paletteDepth=data[7];
		byte Next(){if(position>=data.Length)throw new InvalidDataException("Truncated TGA pixel data.");return data[position++];}
		static uint Rgba(byte r,byte g,byte b,byte a)=>r|((uint)g<<8)|((uint)b<<16)|((uint)a<<24);
		uint Color(int bits,bool gray=false)
		{
			if(gray){byte value=Next();return Rgba(value,value,value,bits==16?Next():(byte)255);}
			if(bits==24||bits==32){byte b=Next(),g=Next(),r=Next();return Rgba(r,g,b,bits==32?Next():(byte)255);}
			if(bits==15||bits==16){int v=Next()|Next()<<8;return Rgba((byte)(((v>>10)&31)*255/31),(byte)(((v>>5)&31)*255/31),(byte)((v&31)*255/31),(byte)(bits==16&&(flags&15)>0&&(v&32768)==0?0:255));}
			throw new InvalidDataException("Unsupported TGA pixel depth.");
		}
		var palette=new Dictionary<int,uint>();
		if(data[1]!=0)for(int i=0;i<paletteCount;i++)palette[paletteFirst+i]=Color(paletteDepth);
		bool indexed=type==1||type==9,gray=type==3||type==11,rle=type>=9;
		uint Pixel(){if(indexed){int ix=Next();if(depth==16)ix|=Next()<<8;if(!palette.TryGetValue(ix,out var c))throw new InvalidDataException("Invalid TGA palette index.");return c;}return Color(depth,gray);}
		var output=new byte[checked(w*h*4)];int count=w*h,cursor=0;
		void Put(uint c){if(cursor>=count)throw new InvalidDataException("TGA packet exceeds image dimensions.");if((cursor&1023)==0)cancellation.ThrowIfCancellationRequested();int x=cursor%w,y=cursor/w;if((flags&32)==0)y=h-1-y;if((flags&16)!=0)x=w-1-x;System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan((y*w+x)*4,4),c);cursor++;}
		while(cursor<count){int packet=rle?Next():0,n=rle?(packet&127)+1:1;if(rle&&(packet&128)!=0){var c=Pixel();for(int i=0;i<n;i++)Put(c);}else for(int i=0;i<n;i++)Put(Pixel());}
		return new(w,h,output);
	}
}
