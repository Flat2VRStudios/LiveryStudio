using System.Buffers.Binary;
using System.Text;

namespace LiveryStudio.Core;

// Bounded access to retail PPKG v2 archives and small local override packages.
// Both stored and decoded CRCs must match before extracted bytes can be used.
public sealed class GamePackage
{
    public sealed record Entry(string Name,long Offset,int CompressedSize,int Size,int Compression,uint CompressedCrc,uint Crc);
    public string FileName {get;}
    public string IndexHash {get;}
    public IReadOnlyDictionary<string,Entry> Entries {get;}
    public GamePackage(string file)
    {
        FileName=Path.GetFullPath(file);using var stream=File.OpenRead(FileName);using var reader=new BinaryReader(stream,Encoding.UTF8);
        if(stream.Length<48||reader.ReadUInt32()!=0x474b5050||reader.ReadInt32()!=2)throw new InvalidDataException("Unsupported game package format.");
        var count=reader.ReadInt32();var length=reader.ReadInt32();
        if(count is <0 or >500000||length<0||length>64*1024*1024||48L+length>stream.Length)throw new InvalidDataException("Invalid game package inventory.");
        stream.Position=48;var table=reader.ReadBytes(length);IndexHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(table));using var tableStream=new MemoryStream(table);using var row=new BinaryReader(tableStream,Encoding.UTF8);
        var entries=new Dictionary<string,Entry>(StringComparer.OrdinalIgnoreCase);
        for(var i=0;i<count;i++)
        {
            var size=row.ReadInt32();if(size is <1 or >2048||size>tableStream.Length-tableStream.Position)throw new InvalidDataException("Invalid game asset name.");
            var name=Normalize(Encoding.UTF8.GetString(row.ReadBytes(size)));row.ReadInt32();var offset=row.ReadInt64();row.ReadUInt64();var packed=row.ReadInt64();var original=row.ReadInt64();var compression=row.ReadInt32();var packedCrc=row.ReadUInt32();var crc=row.ReadUInt32();
            for(var reserved=0;reserved<4;reserved++)row.ReadInt64();
            if(offset<48L+length||packed<0||original<0||packed>int.MaxValue||original>int.MaxValue||packed>stream.Length-offset-8||compression is <0 or >2||!entries.TryAdd(name,new(name,offset,(int)packed,(int)original,compression,packedCrc,crc)))throw new InvalidDataException("Invalid game package entry.");
        }
        if(tableStream.Position!=length)throw new InvalidDataException("Unexpected game package inventory data.");Entries=entries;
    }
    public static string Normalize(string name)
    {
        name=name.Replace('\\','/');if(name.Length==0||name.Any(c=>char.IsControl(c)||c==':')||name.Split('/').Any(p=>p is "" or "." or ".."))throw new InvalidDataException("Invalid game asset path.");return name.ToUpperInvariant();
    }
    public byte[] Read(string name,int maximum=32*1024*1024)
    {
        if(!Entries.TryGetValue(Normalize(name),out var entry))throw new FileNotFoundException("The game package does not contain "+name);
        if(entry.Size>maximum||entry.CompressedSize>maximum+1024*1024)throw new InvalidDataException("The game asset exceeds its size limit.");
        using var file=File.OpenRead(FileName);using var reader=new BinaryReader(file);file.Position=entry.Offset;
        if(reader.ReadUInt32()!=0x42474b50)throw new InvalidDataException("Invalid game asset start guard.");var packed=reader.ReadBytes(entry.CompressedSize);
        if(packed.Length!=entry.CompressedSize||reader.ReadUInt32()!=0x45474b50||Crc32(packed)!=entry.CompressedCrc)throw new InvalidDataException("The game asset is damaged or changed.");
        byte[] bytes;
        if(entry.Compression==0)bytes=packed;
        else if(entry.Compression==2)bytes=Lz4(packed,entry.Size);
        else throw new InvalidDataException("This game's asset compression is not supported.");
        if(bytes.Length!=entry.Size||Crc32(bytes)!=entry.Crc)throw new InvalidDataException("The extracted game asset failed its integrity check.");return bytes;
    }
    public static void Write(string destination,IReadOnlyDictionary<string,string> files)
    {
        if(files.Count is <1 or >128)throw new InvalidDataException("Invalid local car package inventory.");
        var names=files.Keys.Select(Normalize).ToArray();if(names.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=names.Length)throw new InvalidDataException("Duplicate local car assets.");
        var sizes=files.Values.Select(p=>new FileInfo(p).Length).ToArray();
        if(sizes.Any(n=>n>128*1024*1024)||sizes.Sum()>512L*1024*1024)throw new InvalidDataException("The car package is too large.");
        var records=files.Select(p=>(Name:Normalize(p.Key),Bytes:File.ReadAllBytes(p.Value))).OrderBy(p=>p.Name,StringComparer.Ordinal).ToArray();
        if(records.Any(r=>r.Bytes.Length>128*1024*1024)||records.Sum(r=>(long)r.Bytes.Length)>512L*1024*1024)throw new InvalidDataException("The car package is too large.");
        var lookup=records.Sum(r=>84+Encoding.UTF8.GetByteCount(r.Name));long offset=48+lookup;
        using var output=new BinaryWriter(new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None));
        output.Write(0x474b5050u);output.Write(2);output.Write(records.Length);output.Write(lookup);for(var i=0;i<4;i++)output.Write(0L);
        foreach(var r in records){var name=Encoding.UTF8.GetBytes(r.Name.Replace('/','\\'));var crc=Crc32(r.Bytes);output.Write(name.Length);output.Write(name);uint hash=2166136261;foreach(var b in name)hash=unchecked((hash^b)*16777619);output.Write(hash);output.Write(offset);output.Write(0UL);output.Write((long)r.Bytes.Length);output.Write((long)r.Bytes.Length);output.Write(0);output.Write(crc);output.Write(crc);for(var i=0;i<4;i++)output.Write(0L);offset+=r.Bytes.Length+8L;}
        foreach(var r in records){output.Write(0x42474b50u);output.Write(r.Bytes);output.Write(0x45474b50u);}
    }
    internal static byte[] Lz4(byte[] packed,int size)
    {
        using var stream=new MemoryStream(packed);using var reader=new BinaryReader(stream);
        if(reader.ReadUInt32()!=0x184d2204)throw new InvalidDataException("Unsupported LZ4 frame.");var flags=reader.ReadByte();var descriptor=reader.ReadByte();
        if((flags&0xc0)!=0x40||(flags&0x03)!=0||((descriptor>>4)&7) is <4 or >7)throw new InvalidDataException("Unsupported LZ4 frame options.");
        if((flags&8)!=0&&reader.ReadUInt64()!=(ulong)size)throw new InvalidDataException("Invalid LZ4 content length.");reader.ReadByte();
        var output=new byte[size];var target=0;var maxBlock=1<<((descriptor>>4)*2+8);
        while(true)
        {
            var block=reader.ReadUInt32();if(block==0)break;var length=(int)(block&0x7fffffff);
            if(length<1||length>maxBlock||length>stream.Length-stream.Position)throw new InvalidDataException("Invalid LZ4 block length.");var bytes=reader.ReadBytes(length);var start=target;
            if((block&0x80000000)!=0){if(length>output.Length-target)throw new InvalidDataException("Oversized LZ4 block.");bytes.CopyTo(output,target);target+=length;}
            else
            {
                var at=0;
                int Extra(int initial){if(initial!=15)return initial;int value=initial,next;do{if(at>=bytes.Length)throw new InvalidDataException("Truncated LZ4 length.");next=bytes[at++];value=checked(value+next);}while(next==255);return value;}
                while(at<bytes.Length)
                {
                    var token=bytes[at++];var literal=Extra(token>>4);if(literal>bytes.Length-at||literal>output.Length-target)throw new InvalidDataException("Invalid LZ4 literal.");bytes.AsSpan(at,literal).CopyTo(output.AsSpan(target));at+=literal;target+=literal;if(at==bytes.Length)break;
                    if(at+2>bytes.Length)throw new InvalidDataException("Truncated LZ4 match.");var distance=BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at));at+=2;var match=checked(Extra(token&15)+4);var minimum=(flags&32)!=0?start:0;
                    if(distance==0||distance>target-minimum||match>output.Length-target)throw new InvalidDataException("Invalid LZ4 match.");for(var i=0;i<match;i++){output[target]=output[target-distance];target++;}
                }
            }
            if(target-start>maxBlock)throw new InvalidDataException("Oversized decoded LZ4 block.");if((flags&16)!=0)reader.ReadUInt32();
        }
        if((flags&4)!=0)reader.ReadUInt32();if(target!=size||stream.Position!=stream.Length)throw new InvalidDataException("Incomplete LZ4 frame.");return output;
    }
    private static readonly uint[] CrcTable=Enumerable.Range(0,256).Select(n=>{var v=(uint)n;for(var k=0;k<8;k++)v=(v&1)!=0?0xedb88320^(v>>1):v>>1;return v;}).ToArray();
    internal static uint Crc32(ReadOnlySpan<byte> bytes){var crc=uint.MaxValue;foreach(var b in bytes)crc=CrcTable[(byte)(crc^b)]^(crc>>8);return ~crc;}
}
