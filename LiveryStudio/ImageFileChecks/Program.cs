using System.Security.Cryptography;
using LiveryStudio;
using LiveryStudio.Core;

var root=Path.Combine(AppContext.BaseDirectory,"results",Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var count=0;
void Check(bool result,string label){if(!result)throw new Exception(label);Console.WriteLine("PASS "+label);count++;}
PaintImage Color(byte red,byte green)=>new(64,64,Enumerable.Range(0,64*64).SelectMany(_=>new byte[]{red,green,30,255}).ToArray());
var original=Color(180,20);
var replacement=Color(20,180);
var path=Path.Combine(root,"same-key.png");
await Task.WhenAll(Enumerable.Range(0,24).Select(_=>ImageFiles.WriteAsync(original,path,overwrite:false)));
var image=await ImageFiles.ReadAsync(path);
Check(image.Width==64&&image.Height==64&&image.Pixels.SequenceEqual(original.Pixels),"concurrent same-key producers leave one complete decodable PNG");
var hash=SHA256.HashData(File.ReadAllBytes(path));
using(var reader=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
{
    var reproduced=false;
    try{await ImageFiles.WriteAsync(replacement,path);}catch(Exception e) when(e is IOException or UnauthorizedAccessException){reproduced=true;Console.WriteLine("Expected locked-file overwrite failure: "+e.GetType().Name);}
    Check(reproduced,"locked displayed PNG reproduces former overwrite failure");
    await Task.WhenAll(Enumerable.Range(0,12).Select(_=>ImageFiles.WriteAsync(replacement,path,overwrite:false)));
    Check(SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(hash),"cache creation keeps displayed existing PNG unchanged");
}
await ImageFiles.WriteAsync(replacement,path);
image=await ImageFiles.ReadAsync(path);
Check(image.Pixels.SequenceEqual(replacement.Pixels),"normal image exports still replace an existing file");
var directoryTarget=Path.Combine(root,"directory-target");Directory.CreateDirectory(directoryTarget);
var rejected=false;
try{await ImageFiles.WriteAsync(original,directoryTarget,overwrite:false);}catch(Exception e) when(e is IOException or UnauthorizedAccessException){rejected=true;}
Check(rejected,"non-race destination errors remain visible to caller");
Check(Directory.GetFiles(root,"*.tmp").Length==0,"successful and failed writes remove staging files");
Console.WriteLine($"{count} image file checks passed; fixtures: {root}");
await PhotoEncodingChecks.Run(root,Check);
Console.WriteLine($"{count} total image and photo encoding checks passed; fixtures: {root}");
