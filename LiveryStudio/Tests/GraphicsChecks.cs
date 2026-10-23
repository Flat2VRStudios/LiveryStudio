using System.Numerics;
using System.Runtime.InteropServices;
using LiveryStudio.Core;

internal static class GraphicsChecks
{
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint Create();
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Mesh(nint r,float[] p,uint count);
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Texture(nint r,byte[] p,uint w,uint h,int slot);
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Render(nint r,float[] scene,uint w,uint h);
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int RenderExtended(nint r,float[] scene,uint floats,uint w,uint h);
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Snapshot(nint r,byte[] pixels,uint capacity);
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Destroy(nint r);
	[UnmanagedFunctionPointer(CallingConvention.Cdecl,CharSet=CharSet.Unicode)] private delegate int Environment(nint r,string specular,string irradiance);
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint Error();
	public static void Run(string repo,string scratch,Action<bool,string> check)
	{
		var library=NativeLibrary.Load(Path.Combine(repo,"LiveryStudio/artifacts/native/LiveryPreview.dll"));
		try
		{
			T Get<T>(string name)where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library,name));
			// This entry point renders to a GPU buffer, without a desktop window or input automation.
			var create=Get<Create>("ls_create_offscreen");var renderer=create();
			check(renderer!=0,"Create an offscreen Direct3D renderer for pixel-level regression checks");
			try
			{
				var mesh=Get<Mesh>("ls_mesh");var texture=Get<Texture>("ls_texture");var render=Get<Render>("ls_render");var snapshot=Get<Snapshot>("ls_snapshot");
				float[] vertices=[-1,-1,0,0,0,1,0,1,1,1,-1,0,0,0,1,1,1,1,1,1,0,0,0,1,1,0,1,-1,-1,0,0,0,1,0,1,1,1,1,0,0,0,1,1,0,1,-1,1,0,0,0,1,0,0,1];
				check(mesh(renderer,vertices,6)==0,"Upload regression geometry with known UVs");
				texture(renderer,[80,140,220,255],1,1,0);texture(renderer,[180,210,0,255],1,1,2);
				texture(renderer,[128,128,255,255],1,1,3);texture(renderer,[180,140,100,255],1,1,4);texture(renderer,[20,0,0,255],1,1,5);
				texture(renderer,[60,60,60,255],1,1,6);texture(renderer,[128,128,255,255],1,1,7);
				var matrix=Matrix4x4.CreateLookAt(new(0,0,3),Vector3.Zero,Vector3.UnitY)*Matrix4x4.CreatePerspectiveFieldOfView(.8f,1,.03f,100);
				float[] scene=[matrix.M11,matrix.M12,matrix.M13,matrix.M14,matrix.M21,matrix.M22,matrix.M23,matrix.M24,matrix.M31,matrix.M32,matrix.M33,matrix.M34,matrix.M41,matrix.M42,matrix.M43,matrix.M44,0,0,3,0,.1f,.2f,.3f,.7f,.1f,.2f,.3f,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,1,0,0,0,.5f,.5f,.7f,0,0,0,0,0,1.2f,1,0,0,.2f,.9f,1,0,0,0,0,0,0,0,0,0];
				byte[] Capture(){check(render(renderer,scene,96,96)==0,"Render preview regression frame");var pixels=new byte[96*96*4];check(snapshot(renderer,pixels,(uint)pixels.Length)==0,"Read preview regression pixels from the GPU");return pixels;}
				var clean=Capture();check(clean.Where((_,i)=>i%4!=3).Distinct().Count()>20,"GPU preview contains a shaded car surface");
				texture(renderer,[230,128,200,255],1,1,3);var normal=Capture();check(!normal.SequenceEqual(clean),"Real normal-map pixels change the rendered highlights");
				texture(renderer,[128,128,255,255],1,1,3);scene[58]=1;var dirt=Capture();check(!dirt.SequenceEqual(clean),"Game dirt color and surface maps change preview pixels");
				scene[58]=0;scene[59]=.5f;var damage=Capture();check(!damage.SequenceEqual(clean),"Damage masks and exposed-metal material change preview pixels");
				var specular=Path.Combine(repo,"LiveryStudio/artifacts/preview-assets/probe-00.dds");var irradiance=Path.Combine(repo,"LiveryStudio/artifacts/preview-assets/probe-00-irr.dds");
				var environmentResult=Get<Environment>("ls_environment")(renderer,specular,irradiance);check(environmentResult==0,"Load original HDR cube maps: "+(environmentResult==0?"GPU upload succeeded":Marshal.PtrToStringAnsi(Get<Error>("ls_error")())));
				scene[59]=0;scene[63]=1;scene[64]=.6f;scene[65]=1;var environment=Capture();check(!environment.SequenceEqual(clean),"Actual game HDR probes replace the representative reflections");
				var badProbe=Path.Combine(scratch,"bad.dds");File.WriteAllBytes(badProbe,[1,2,3]);check(Get<Environment>("ls_environment")(renderer,badProbe,irradiance)!=0,"Reject a truncated reflection probe without accepting invalid GPU data");
				var extended=Get<RenderExtended>("ls_render_ex");var decalScene=new float[76];Array.Copy(scene,decalScene,72);decalScene[55]=2;decalScene[58]=decalScene[59]=decalScene[62]=decalScene[63]=decalScene[66]=decalScene[71]=0;
				Array.Clear(decalScene,28,20);decalScene[31]=2;decalScene[32]=1;decalScene[35]=2;decalScene[37]=1;decalScene[39]=1;decalScene[42]=1;decalScene[43]=2;decalScene[72]=.25f;decalScene[73]=.35f;
				texture(renderer,new byte[32*32*4],32,32,0);texture(renderer,[255,0,0,255,0,0,255,255,0,255,0,255,255,255,255,255],2,2,1);
				byte[] DecalFrame(){check(extended(renderer,decalScene,76,96,96)==0,"Render flexible decal projection");var pixels=new byte[96*96*4];check(snapshot(renderer,pixels,(uint)pixels.Length)==0,"Read flexible decal projection pixels");return pixels;}
				var normalDecal=DecalFrame();decalScene[74]=1;var flippedDecal=DecalFrame();check(!normalDecal.SequenceEqual(flippedDecal),"Horizontal artwork flip changes actual GPU hover-preview pixels");
				decalScene[75]=1;check(!flippedDecal.SequenceEqual(DecalFrame()),"Vertical artwork flip changes actual GPU hover-preview pixels");
				decalScene[30]=.4f;decalScene[73]=.05f;var shallow=DecalFrame();decalScene[73]=.6f;var deep=DecalFrame();check(!shallow.SequenceEqual(deep),"GPU hover preview obeys the decal wrap-depth limit");
				var bentVertices=(float[])vertices.Clone();for(var i=0;i<bentVertices.Length;i+=9){bentVertices[i+3]=1;bentVertices[i+5]=.12f;}mesh(renderer,bentVertices,6);var limited=DecalFrame();decalScene[72]=.05f;check(!limited.SequenceEqual(DecalFrame()),"GPU hover preview obeys the angle limit on a bent body surface");
				mesh(renderer,vertices,6);var plain=new SurfaceProjection(Vector3.Zero,Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,2,2);var rotatedLayer=new PaintLayer{Kind=LayerKind.Sticker,Rotation=37,Samples=[plain with {Width=1.5f,Height=1}]};var stretched=LayerSets.Fit([new(){Kind=LayerKind.Color,Samples=[plain],Opacity=0},rotatedLayer],plain with {Width=1.8f,Height=.8f})[1].Samples[0];
				void SceneVector(int offset,Vector3 v){decalScene[offset]=v.X;decalScene[offset+1]=v.Y;decalScene[offset+2]=v.Z;}SceneVector(28,stretched.Center);decalScene[31]=stretched.Width;SceneVector(32,stretched.Right);decalScene[35]=stretched.Height;SceneVector(36,stretched.Up);SceneVector(40,stretched.Normal);decalScene[72]=.25f;decalScene[73]=.35f;decalScene[74]=decalScene[75]=0;var stretchedPixels=DecalFrame();
				foreach(var entry in new[]{(new Vector2(-.25f,.25f),0,"red"),(new Vector2(.25f,.25f),2,"blue"),(new Vector2(-.25f,-.25f),1,"green")}){var point=LayerSets.Corner(stretched,entry.Item1);var projected=DecalEditing.Project(point,matrix,96,96,out var screen);var at=(Math.Clamp((int)screen.Y,0,95)*96+Math.Clamp((int)screen.X,0,95))*4;check(projected&&Enumerable.Range(0,3).Where(c=>c!=entry.Item2).All(c=>stretchedPixels[at+entry.Item2]>stretchedPixels[at+c]+20),"GPU stretched rotated artwork preserves its "+entry.Item3+" texture quadrant");}
				check(extended(renderer,decalScene,72,96,96)!=0,"Reject incompatible native scene buffers before reading their pixels");
				// Compare actual GPU hover blending with a CPU-baked texture under the same
				// unlit material. Constant pigments avoid interpolation at sticker boundaries.
				SceneVector(28,Vector3.Zero);SceneVector(32,Vector3.UnitX);SceneVector(36,Vector3.UnitY);SceneVector(40,Vector3.UnitZ);decalScene[31]=decalScene[35]=2;decalScene[39]=.5f;decalScene[43]=2;
				var basePixels=new byte[32*32*4];for(var k=0;k<basePixels.Length;k+=4){basePixels[k]=80;basePixels[k+1]=140;basePixels[k+2]=220;basePixels[k+3]=255;}
				var blendVertices=(float[])vertices.Clone();for(var i=0;i<blendVertices.Length;i+=9){blendVertices[i+3]=.8660254f;blendVertices[i+5]=.5f;}mesh(renderer,blendVertices,6);
				decalScene[20]=.8f;decalScene[21]=.1f;decalScene[22]=.05f;
				var baseImage=new PaintImage(32,32,basePixels);var constant=new PaintImage(1,1,[200,100,50,128]);var regressionMesh=new PaintMesh(blendVertices);
				foreach(var paintMask in new byte[]{0,128,255})foreach(var blend in Enum.GetValues<DecalBlend>())
				{
					for(var k=3;k<basePixels.Length;k+=4)basePixels[k]=paintMask;
					texture(renderer,basePixels,32,32,0);texture(renderer,constant.Pixels,1,1,1);decalScene[43]=2;decalScene[47]=(float)blend;var hoverBlend=DecalFrame();
					var baked=new TexturePainter(baseImage);baked.Stamp(regressionMesh,plain,constant,Vector3.One,.5f,PaintFinish.ToSrgb(new Vector3(.1f,.2f,.3f)),blend:blend);texture(renderer,baked.Image.Pixels,32,32,0);decalScene[43]=0;var bakedBlend=DecalFrame();var at=(48*96+48)*4;
					check(Enumerable.Range(0,3).All(c=>Math.Abs(hoverBlend[at+c]-bakedBlend[at+c])<=2),"Actual GPU "+blend+" hover pigment matches baked output over paint mask "+paintMask);
				}
				var maskedSource=new byte[32*32*4];for(var k=0;k<maskedSource.Length;k+=4){maskedSource[k]=255;maskedSource[k+3]=255;}
				var maskedArtwork=DecalEffects.Apply(new(32,32,maskedSource),new(){Mask=DecalMask.Oval,Width=.7f,Height=.7f,Feather=.1f});texture(renderer,basePixels,32,32,0);texture(renderer,maskedArtwork.Pixels,32,32,1);decalScene[47]=0;decalScene[43]=0;var belowMask=DecalFrame();decalScene[43]=2;decalScene[39]=1;var maskedFrame=DecalFrame();
				DecalEditing.Project(new(-.8f,.8f,0),matrix,96,96,out var outsideMask);var outsideAt=((int)outsideMask.Y*96+(int)outsideMask.X)*4;var centerAt=(48*96+48)*4;
				check(Enumerable.Range(0,3).All(c=>maskedFrame[outsideAt+c]==belowMask[outsideAt+c])&&maskedFrame[centerAt]>belowMask[centerAt],"GPU masked hover reveals unchanged underlying pixels outside the stencil and paints its center");
                var brushSource=new PaintImage(32,32,maskedSource);var brush=new DecalMaskPainting(32,32,new(){Mask=DecalMask.Oval,Width=.7f,Height=.7f,Feather=.1f});brush.AttachArtwork(brushSource);
                foreach(var restore in new[]{false,true})
                {
                    brush.Dab(new(.5f),new(.2f),1,1,restore);texture(renderer,basePixels,32,32,0);texture(renderer,brush.Artwork!.Pixels,32,32,1);decalScene[43]=2;var liveBrush=DecalFrame();
                    var bakedBrush=new TexturePainter(baseImage);bakedBrush.Stamp(regressionMesh,plain,brush.Artwork,Vector3.One,1,PaintFinish.ToSrgb(new Vector3(.1f,.2f,.3f)));texture(renderer,bakedBrush.Image.Pixels,32,32,0);decalScene[43]=0;var bakedBrushFrame=DecalFrame();
                    check(Enumerable.Range(0,3).All(c=>Math.Abs(liveBrush[centerAt+c]-bakedBrushFrame[centerAt+c])<=2),"Actual GPU freehand "+(restore?"Restore":"Erase")+" preview matches baked center pixels");
                }
                // A tiny decal exposes resampling and alpha-boundary differences
                // that opaque center-pixel comparisons cannot detect.
                mesh(renderer,vertices,6);var edgePixels=new byte[8*8*4];for(var y=0;y<8;y++)for(var x=0;x<8;x++){var k=(y*8+x)*4;edgePixels[k]=(byte)(30+x*25);edgePixels[k+1]=(byte)(40+y*24);edgePixels[k+2]=190;edgePixels[k+3]=(byte)(x>=2&&x<=5&&y>=2&&y<=5?0:255);}
                var edgeArtwork=new PaintImage(8,8,edgePixels);
                foreach(var paintMask in new byte[]{0,128,255})foreach(var blend in Enum.GetValues<DecalBlend>())
                {
                    for(var k=3;k<basePixels.Length;k+=4)basePixels[k]=paintMask;
                    texture(renderer,basePixels,32,32,0);texture(renderer,edgePixels,8,8,1);decalScene[43]=2;decalScene[39]=.65f;decalScene[47]=(float)blend;var liveEdges=DecalFrame();
                    var bakedEdges=new TexturePainter(baseImage);bakedEdges.Stamp(new PaintMesh(vertices),plain,edgeArtwork,Vector3.One,.65f,PaintFinish.ToSrgb(new Vector3(.1f,.2f,.3f)),blend:blend);texture(renderer,bakedEdges.Image.Pixels,32,32,0);decalScene[43]=0;var bakedEdgeFrame=DecalFrame();
                    var differences=(from y in Enumerable.Range(16,64) from x in Enumerable.Range(16,64) from channel in Enumerable.Range(0,3) let k=(y*96+x)*4+channel select Math.Abs(liveEdges[k]-bakedEdgeFrame[k])).ToArray();
                    Console.WriteLine($"EDGE PARITY paint={paintMask} blend={blend} max={differences.Max()} mean={differences.Average():F4} differing={differences.Count(d=>d>2)}");
                    check(differences.Max()<=2,"GPU "+blend+" preview matches baked decal color and erased edges over paint mask "+paintMask);
                }
                void CheckAtlas(PaintMesh model,PaintImage below,PaintImage? artwork,bool mirror,string label)
                {
                    mesh(renderer,model.NativeVertices,(uint)model.Vertices.Length);texture(renderer,below.Pixels,(uint)below.Width,(uint)below.Height,0);if(artwork is not null)texture(renderer,artwork.Pixels,(uint)artwork.Width,(uint)artwork.Height,1);
                    decalScene[43]=artwork is null?1:2;decalScene[39]=.65f;decalScene[47]=0;decalScene[71]=mirror?1:0;decalScene[44]=.7f;decalScene[45]=.3f;decalScene[46]=.9f;decalScene[67]=.65f;var live=DecalFrame();
                    var baked=new TexturePainter(below);baked.Stamp(model,plain,artwork,new(.7f,.3f,.9f),.65f,PaintFinish.ToSrgb(new Vector3(.1f,.2f,.3f)),hardness:.65f);if(mirror)baked.Stamp(model,PaintLayer.Reflected(plain),artwork,new(.7f,.3f,.9f),.65f,PaintFinish.ToSrgb(new Vector3(.1f,.2f,.3f)),hardness:.65f);
                    texture(renderer,baked.Image.Pixels,(uint)below.Width,(uint)below.Height,0);decalScene[43]=0;decalScene[71]=0;var reference=DecalFrame();
                    var differences=(from y in Enumerable.Range(16,64) from x in Enumerable.Range(16,64) from channel in Enumerable.Range(0,3) let k=(y*96+x)*4+channel select Math.Abs(live[k]-reference[k])).ToArray();
                    Console.WriteLine($"ATLAS PARITY {label} max={differences.Max()} mean={differences.Average():F4}");check(differences.Max()<=2,"Actual GPU atlas matches baked output: "+label);
                }
                CheckAtlas(new(vertices),baseImage,edgeArtwork,true,"paired semi-transparent decals");
                var overlappingVertices=vertices.Concat(vertices.Select((value,index)=>index%9==0?value+.3f:value)).ToArray();CheckAtlas(new(overlappingVertices),baseImage,edgeArtwork,false,"first covered shared UVs and transparent fallback");
                var seamVertices=(float[])vertices.Clone();for(var i=27;i<seamVertices.Length;i+=9)seamVertices[i]+=.3f;
                CheckAtlas(new(seamVertices),baseImage,edgeArtwork,false,"adjacent UV seam with first triangle priority");
                CheckAtlas(new(seamVertices.Skip(27).Concat(seamVertices.Take(27)).ToArray()),baseImage,edgeArtwork,false,"adjacent UV seam with reversed triangle priority");
                var minifiedPixels=new byte[128*128*4];for(var k=0;k<minifiedPixels.Length;k+=4){minifiedPixels[k]=80;minifiedPixels[k+1]=140;minifiedPixels[k+2]=220;minifiedPixels[k+3]=128;}CheckAtlas(new(vertices),new(128,128,minifiedPixels),edgeArtwork,false,"minified decal mip levels");
                CheckAtlas(new(vertices),baseImage,null,true,"paired soft color brush");
                void CheckMaterial(LayerKind kind,byte paintMask,bool mirror,int surfaceSize,bool existingMaterial,bool priorPigment=false,bool overrideFinish=false,bool variedMask=false)
                {
                    var project=new LiveryProject{InnerColor="#38649A",OuterColor="#AB6234",UseSrgbPaintColors=true,OuterGloss=.85,InnerGloss=.62,Metalness=.35,ClearCoat=.75,OverrideArtworkFinish=overrideFinish,ArtworkGloss=.82,ArtworkMetalness=.1};
                    var pixels=(byte[])basePixels.Clone();for(var k=3;k<pixels.Length;k+=4)pixels[k]=variedMask?(byte)((k/4*37)%256):paintMask;
                    var surfacePixels=new byte[surfaceSize*surfaceSize*4];for(var k=0;k<surfacePixels.Length;k+=4){surfacePixels[k]=180;surfacePixels[k+1]=210;surfacePixels[k+2]=40;surfacePixels[k+3]=255;}
                    var colorFile=Path.Combine(scratch,"material-color.tga");var surfaceFile=Path.Combine(scratch,"material-surface.tga");FinishMaps.WriteTga(new(32,32,pixels),colorFile);FinishMaps.WriteTga(new(surfaceSize,surfaceSize,surfacePixels),surfaceFile);
                    var layer=new PaintLayer{Kind=kind,Gloss=.2f,Metalness=.9f,Opacity=.65f,Hardness=.65f,Mirror=mirror,Samples=[plain]};
                    var document=new LayerDocument{BaseAlbedo=colorFile,BaseStructure=surfaceFile,SeamPadding=0,History=[new("Material regression",DateTimeOffset.UtcNow,[])]};
                    if(priorPigment)document.Commit("Earlier pigment",layers=>layers.Add(new(){Kind=LayerKind.Color,Color="#EF7020",Opacity=.5f,Samples=[plain with {Center=new(-.4f,.2f,0),Width=2.8f,Height=2.8f}]}));
                    if(existingMaterial)document.Commit("Existing finish",layers=>layers.Add(layer with {Id=Guid.NewGuid(),Opacity=.3f,Mirror=false}));
                    var below=document.Compose(new(vertices),project);texture(renderer,below.Albedo.Pixels,32,32,0);texture(renderer,below.Structure.Pixels,(uint)surfaceSize,(uint)surfaceSize,2);check(texture(renderer,pixels,32,32,8)==0,"Upload immutable original paint mask for material preview");mesh(renderer,vertices,6);
                    SceneVector(20,PaintFinish.PaintColor(project,project.OuterColor));decalScene[23]=(float)project.OuterGloss;SceneVector(24,PaintFinish.PaintColor(project,project.InnerColor));decalScene[27]=existingMaterial?1:0;
                    decalScene[48]=(float)project.Metalness;decalScene[49]=(float)project.ClearCoat;decalScene[50]=(float)project.InnerGloss;decalScene[51]=overrideFinish&&!existingMaterial?1:0;decalScene[52]=(float)project.ArtworkGloss;decalScene[53]=(float)project.ArtworkMetalness;decalScene[55]=decalScene[62]=decalScene[63]=0;
                    decalScene[43]=4;decalScene[39]=layer.Opacity;decalScene[67]=layer.Hardness;decalScene[68]=kind==LayerKind.Gloss?0:kind==LayerKind.Metalness?1:2;decalScene[69]=layer.Gloss;decalScene[70]=layer.Metalness;decalScene[71]=mirror?1:0;var live=DecalFrame();
                    document.Commit("New finish",layers=>layers.Add(layer));var baked=document.Compose(new(vertices),project);texture(renderer,baked.Albedo.Pixels,32,32,0);texture(renderer,baked.Structure.Pixels,(uint)surfaceSize,(uint)surfaceSize,2);decalScene[43]=0;decalScene[71]=0;decalScene[27]=1;decalScene[51]=0;var reference=DecalFrame();
                    var differences=(from y in Enumerable.Range(16,64) from x in Enumerable.Range(16,64) from channel in Enumerable.Range(0,3) let k=(y*96+x)*4+channel select Math.Abs(live[k]-reference[k])).ToArray();
                    var label=$"{kind} mask={paintMask} mirror={mirror} surface={surfaceSize} existing={existingMaterial} pigment={priorPigment} override={overrideFinish} varied={variedMask}";Console.WriteLine($"MATERIAL PARITY {label} max={differences.Max()} mean={differences.Average():F4}");
                    FinishMaps.WriteTga(new(96,96,live),Path.Combine(scratch,"material-live.tga"));FinishMaps.WriteTga(new(96,96,reference),Path.Combine(scratch,"material-baked.tga"));check(differences.Max()<=2,"Actual GPU finish matches baked output: "+label);
                }
                foreach(var kind in new[]{LayerKind.Gloss,LayerKind.Metalness,LayerKind.Surface})foreach(var paintMask in new byte[]{0,128,255})foreach(var mirror in new[]{false,true})foreach(var size in new[]{16,32,48})CheckMaterial(kind,paintMask,mirror,size,false);
                foreach(var kind in new[]{LayerKind.Gloss,LayerKind.Metalness,LayerKind.Surface})foreach(var existing in new[]{false,true})foreach(var finishOverride in new[]{false,true})CheckMaterial(kind,0,true,48,existing,true,finishOverride,true);
				FinishMaps.WriteTga(new(96,96,clean),Path.Combine(scratch,"gpu-clean.tga"));FinishMaps.WriteTga(new(96,96,normal),Path.Combine(scratch,"gpu-normal.tga"));
			}
			finally{Get<Destroy>("ls_destroy")(renderer);}
		}
		finally{NativeLibrary.Free(library);}
	}
}
