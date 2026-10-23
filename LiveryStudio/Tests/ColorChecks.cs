using System.Numerics;
using LiveryStudio.Core;

internal static class ColorChecks
{
    public static void Run(string temp,Action<bool,string> check,Action<Action,string> reject)
    {
        var workspace=Path.Combine(temp,"color-library");check(ColorLibrary.Read(workspace).Recent.Count==0,"New color library starts without fabricated recent choices");
        ColorLibrary.Use(workspace,"#aabbcc","#112233","#AABBCC");var state=ColorLibrary.Read(workspace);check(state.Recent.SequenceEqual(new[]{"#AABBCC","#112233"}),"Recent colors normalize case, deduplicate, and keep usage order");
        ColorLibrary.Favorite(workspace,"#aabbcc"," Team mint ");ColorLibrary.Favorite(workspace,"#AABBCC","Mint badge");check(ColorLibrary.Read(workspace).Favorites.Single()==new SavedColor("#AABBCC","Mint badge"),"Naming a favorite updates the same color instead of making duplicates");
        ColorLibrary.Use(workspace,Enumerable.Range(0,40).Select(i=>$"#{i:X6}").ToArray());state=ColorLibrary.Read(workspace);check(state.Recent.Count==24&&state.Recent[0]=="#000027"&&state.Favorites.Single().Name=="Mint badge","Recent history stays bounded without evicting named favorites");
        ColorLibrary.RemoveFavorite(workspace,"#aabbcc");check(ColorLibrary.Read(workspace).Favorites.Count==0&&ColorLibrary.Read(workspace).Recent.Count==24,"Removing a favorite preserves the independent recent history");
        var parallel=Path.Combine(temp,"parallel-colors");Task.WaitAll(Enumerable.Range(1,12).Select(i=>Task.Run(()=>ColorLibrary.Use(parallel,$"#{i:X6}"))).ToArray());check(ColorLibrary.Read(parallel).Recent.Count==12,"Concurrent color updates preserve every writer's distinct choice");
        var full=Path.Combine(temp,"full-colors");for(var i=0;i<32;i++)ColorLibrary.Favorite(full,$"#{i:X6}","Color "+i);reject(()=>ColorLibrary.Favorite(full,"#FFFFFF","Overflow"),"Favorite capacity rejects additions without evicting saved colors");check(ColorLibrary.Read(full).Favorites.Count==32,"Rejected favorite changes leave the existing library intact");
        reject(()=>ColorLibrary.Use(workspace,"bad"),"Reject malformed saved color values");reject(()=>ColorLibrary.Favorite(workspace,"#FFFFFF",new string('x',49)),"Reject excessive favorite names");reject(()=>ColorLibrary.Favorite(workspace,"#FFFFFF","line\nbreak"),"Reject control characters in favorite names");
        var future=Path.Combine(temp,"future-colors");Directory.CreateDirectory(future);var futureFile=Path.Combine(future,"colors.json");File.WriteAllText(futureFile,"{\"Version\":2,\"Recent\":[],\"Favorites\":[]}");var before=File.ReadAllBytes(futureFile);reject(()=>ColorLibrary.Use(future,"#FFFFFF"),"Reject unknown future color libraries");check(File.ReadAllBytes(futureFile).SequenceEqual(before),"Future library data is not overwritten by an older editor");
        var invalid=Path.Combine(temp,"invalid-colors");Directory.CreateDirectory(invalid);File.WriteAllText(Path.Combine(invalid,"colors.json"),"{\"Version\":1,\"Recent\":[\"#ffffff\",\"#FFFFFF\"],\"Favorites\":[]}");reject(()=>ColorLibrary.Read(invalid),"Reject duplicate colors in a corrupted library");
        var point=new SurfaceHit(Vector3.Zero,Vector3.UnitZ,new(.5f,.5f),0,1);var project=new LiveryProject{UseSrgbPaintColors=true,OuterColor="#F56824",InnerColor="#F56824"};
        PaintImage Pixel(byte r,byte g,byte b,byte a)=>new(1,1,[r,g,b,a]);
        check(DesignColor.Sample(project,Pixel(17,200,83,255),point,Vector3.UnitZ)=="#11C853","Eyedropper returns opaque artwork pigment exactly");
        check(DesignColor.Sample(project,Pixel(0,0,0,0),point,Vector3.UnitZ)=="#F56824","Eyedropper uses base paint beneath the alpha-black mask");
        check(DesignColor.Sample(project with {OuterColor="#000000",InnerColor="#000000"},Pixel(255,255,255,128),point,Vector3.UnitZ)=="#BCBCBC","Eyedropper blends partial artwork alpha over paint in linear light");
        var twoTone=project with {OuterColor="#FF0000",InnerColor="#0000FF"};check(DesignColor.Sample(twoTone,Pixel(0,0,0,0),point,Vector3.UnitX)=="#FF0000"&&DesignColor.Sample(twoTone,Pixel(0,0,0,0),point,Vector3.UnitZ)=="#0000FF","Two-tone sampling follows the body normal and viewing direction");
        check(DesignColor.Sample(twoTone,Pixel(0,0,0,0),point,new(MathF.Sqrt(3),0,1))=="#BC00BC","Two-tone midpoint is encoded from its linear paint blend");
        var atlas=new PaintImage(2,1,[255,0,0,255,0,0,255,255]);check(DesignColor.Sample(project,atlas,point,Vector3.UnitZ)=="#800080","Eyedropper matches clamped bilinear texture sampling at a texel boundary");check(DesignColor.Sample(project,atlas,point with {UV=new(-2,3)},Vector3.UnitZ)=="#FF0000","Eyedropper clamps UV coordinates like the native sampler");
        check(DesignColor.Sample(project with {OuterGloss=0,InnerGloss=0,Metalness=1,ClearCoat=1},Pixel(17,200,83,255),point,Vector3.UnitZ)=="#11C853","Gloss, metalness, and clear coat do not contaminate sampled pigment");
        var legacy=project with {UseSrgbPaintColors=false,OuterColor="#404040",InnerColor="#404040"};check(DesignColor.Sample(legacy,Pixel(0,0,0,0),point,Vector3.UnitZ)=="#898989","Legacy paint samples its actual design color rather than its raw uniform bytes");check(DesignColor.PaintPickerHex(legacy,"#404040")=="#898989"&&DesignColor.PaintFieldHex(legacy,"#898989")=="#404040","Shared picker adapts legacy paint fields while preserving their interpretation");
        check(DesignColor.PaintPickerHex(project,"#aabbcc")=="#AABBCC"&&DesignColor.PaintFieldHex(project,"#aabbcc")=="#AABBCC","Modern paint fields retain the chosen sRGB swatch directly");
        reject(()=>DesignColor.Sample(project,Pixel(0,0,0,0),point with {UV=new(float.NaN,0)},Vector3.UnitZ),"Reject non-finite eyedropper coordinates");reject(()=>DesignColor.Sample(project,Pixel(0,0,0,0),point,Vector3.Zero),"Reject an undefined eyedropper viewing direction");
    }
}
