cbuffer Scene : register(b0) {
 row_major float4x4 ViewProjection;
 float4 Eye;
 float4 OuterColor;
 float4 InnerColor; // RGB face paint; W indicates an already initialized material atlas
 float4 CenterWidth;
 float4 RightHeight;
 float4 UpOpacity;
 float4 NormalMode;
 float4 BrushColor;
 float4 Material; // metalness, clear coat, face gloss, artwork override
 float4 Artwork; // artwork gloss, artwork metalness, exposure stops, environment
 float4 Lighting; // light angle, normal strength, dirt, damage
 float4 Appearance; // damage gloss, damage metalness, normal enabled, actual cube enabled
 float4 ProbeSettings; // probe strength, sun strength, spare
 float4 EditorPreview; // material channel, brush gloss, brush metalness, mirror
 float4 ProjectionSettings; // minimum facing, relative depth, horizontal flip, vertical flip
};
Texture2D Albedo : register(t0);
Texture2D Sticker : register(t1);
Texture2D StampOverlay : register(t2);
Texture2D StructureMap : register(t3);
Texture2D NormalMap : register(t4);
Texture2D DirtMap : register(t5);
Texture2D DirtSurface : register(t6);
Texture2D DamageMap : register(t7);
Texture2D DamageNormal : register(t8);
TextureCube ReflectionProbe : register(t9);
TextureCube IrradianceProbe : register(t10);
StructuredBuffer<uint> PaintSelection : register(t11);
Texture2D PreviousStampOverlay : register(t12);
Texture2D SurfaceOverlay : register(t13);
Texture2D PreviousSurfaceOverlay : register(t14);
Texture2D<float> BasePaintMask : register(t15);
SamplerState LinearSampler : register(s0);
struct Input { float3 position:POSITION;float3 normal:NORMAL;float2 uv:TEXCOORD0;float kind:TEXCOORD1; };
struct Pixel { float4 position:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;float2 uv:TEXCOORD2;float kind:TEXCOORD3; };
Pixel VS(Input i) {Pixel o;o.position=mul(float4(i.position,1),ViewProjection);o.world=i.position;o.normal=i.normal;o.uv=i.uv;o.kind=i.kind;return o;}
Pixel VSStamp(Input i) {Pixel o=VS(i);o.position=float4(i.uv.x*2-1,1-i.uv.y*2,0,1);return o;}
float4 VSAtlas(uint vertex:SV_VertexID):SV_POSITION {float2 p=float2((vertex<<1)&2,vertex&2);return float4(p*float2(2,-2)+float2(-1,1),0,1);}
float4 PSInitializeSurface(float4 position:SV_POSITION):SV_TARGET {
 uint cw,ch,sw,sh;StructureMap.GetDimensions(sw,sh);uint2 texel=uint2(position.xy);float mask;
 if(InnerColor.w>=2){BasePaintMask.GetDimensions(cw,ch);mask=BasePaintMask.Load(int3(texel*uint2(cw,ch)/uint2(sw,sh),0));}
 else{Albedo.GetDimensions(cw,ch);mask=Albedo.Load(int3(texel*uint2(cw,ch)/uint2(sw,sh),0)).a;}
 float4 surface=StructureMap.Load(int3(texel,0));
 surface.rg=lerp(float2(Material.z,Material.x),surface.rg,mask);return surface;
}
struct StampPixel {
 float4 position:SV_POSITION;
 nointerpolation float3 a:TEXCOORD0;
 nointerpolation float3 b:TEXCOORD1;
 nointerpolation float3 c:TEXCOORD2;
 nointerpolation float3 na:TEXCOORD3;
 nointerpolation float3 nb:TEXCOORD4;
 nointerpolation float3 nc:TEXCOORD5;
 nointerpolation float2 aa:TEXCOORD6;
 nointerpolation float2 bb:TEXCOORD7;
 nointerpolation float2 cc:TEXCOORD8;
 nointerpolation uint primitive:SV_PRIMITIVEID;
};
// Rasterize the same integer bounding rectangle as TexturePainter, then test
// inclusive triangle edges in PSStamp. Ordinary triangle rasterization assigns
// shared edges by the top-left rule and can skip the CPU's first covered triangle.
[maxvertexcount(4)]
void GSStamp(triangle Pixel input[3],uint primitive:SV_PRIMITIVEID,inout TriangleStream<StampPixel> output) {
 if(input[0].kind!=1||(ProbeSettings.z>.5&&PaintSelection[primitive]==0))return;
 float depth=max(CenterWidth.w,RightHeight.w)*ProjectionSettings.y;
 float radius=sqrt((CenterWidth.w*CenterWidth.w+RightHeight.w*RightHeight.w)*.25+depth*depth);
 float3 lower=CenterWidth.xyz-radius,upper=CenterWidth.xyz+radius;
 float3 minimum=min(input[0].world,min(input[1].world,input[2].world));
 float3 maximum=max(input[0].world,max(input[1].world,input[2].world));
 if(any(minimum>upper)||any(maximum<lower))return;
 uint width,height;if(Eye.w>=2)StructureMap.GetDimensions(width,height);else Albedo.GetDimensions(width,height);float2 size=float2(width,height);
 StampPixel o;o.a=input[0].world;o.b=input[1].world;o.c=input[2].world;
 o.na=normalize(input[0].normal);o.nb=normalize(input[1].normal);o.nc=normalize(input[2].normal);
 o.aa=input[0].uv*size;o.bb=input[1].uv*size;o.cc=input[2].uv*size;o.primitive=primitive;
 float2 lo=clamp(floor(min(o.aa,min(o.bb,o.cc))),0,size-1);
 float2 hi=clamp(ceil(max(o.aa,max(o.bb,o.cc))),0,size-1)+1;
 [unroll]for(uint corner=0;corner<4;corner++){
  float2 p=float2((corner&1)?hi.x:lo.x,(corner&2)?hi.y:lo.y)/size;
  o.position=float4(p.x*2-1,1-p.y*2,0,1);output.Append(o);
 }
}
float3 ToSrgb(float3 c);
float3 BlendPigment(float3 below,float3 above);
float4 PSStamp(StampPixel i):SV_TARGET {
 if(ProbeSettings.z>.5 && PaintSelection[i.primitive]==0)discard;
 // precise keeps the CPU's separate multiplies/subtracts at shared edges.
 precise float2 e1=i.bb-i.aa,e2=i.cc-i.aa,d=i.position.xy-i.aa;
 precise float det=e1.x*e2.y-e1.y*e2.x;if(abs(det)<.00001)discard;
 precise float u=(d.x*e2.y-d.y*e2.x)/det,v=(e1.x*d.y-e1.y*d.x)/det;
 if(u<0||v<0||u+v>1)discard;
 precise float3 world=i.a*(1-u-v)+i.b*u+i.c*v;
 float3 normal=normalize(i.na*(1-u-v)+i.nb*u+i.nc*v);
 float3 delta=world-CenterWidth.xyz;
 float2 uv=float2(dot(delta,RightHeight.xyz)/CenterWidth.w+.5,.5-dot(delta,UpOpacity.xyz)/RightHeight.w);
 if(any(uv<0)||any(uv>1)||abs(dot(delta,NormalMode.xyz))>max(CenterWidth.w,RightHeight.w)*ProjectionSettings.y||dot(normal,NormalMode.xyz)<=ProjectionSettings.x)discard;
 if(ProjectionSettings.z>.5)uv.x=1-uv.x;if(ProjectionSettings.w>.5)uv.y=1-uv.y;
 float4 decal=float4(BrushColor.rgb,saturate((1-length((uv-.5)*2))/max(.02,1-ProbeSettings.w)));
 if(NormalMode.w>1.5&&NormalMode.w<3.5){uint w,h;Sticker.GetDimensions(w,h);decal=Sticker.Load(int3(min(int2(uv*float2(w,h)),int2(w-1,h-1)),0));}
 float coverage=decal.a*UpOpacity.w;if(coverage<=.001)discard;
 int3 texel=int3(int2(i.position.xy),0);
 if(Eye.w>=2){
  float4 surface=PreviousSurfaceOverlay.Load(texel);
  if(EditorPreview.x<.5||EditorPreview.x>1.5)surface.r=lerp(surface.r,EditorPreview.y,coverage);
  if(EditorPreview.x>.5)surface.g=lerp(surface.g,EditorPreview.z,coverage);return surface;
 }
 // Match TexturePainter: blend at car texel centers, promote painted texels
 // to artwork alpha, then let the completed atlas generate its mip levels.
 float4 below=Eye.w>.5?PreviousStampOverlay.Load(texel):Albedo.Load(texel);
 float3 pigment=lerp(ToSrgb(InnerColor.rgb),below.rgb,below.a);
 return float4(NormalMode.w>3.5?pigment:lerp(pigment,BlendPigment(pigment,decal.rgb),coverage),1);
}
float3 ToLinear(float3 c){return float3(c.x<=.04045?c.x/12.92:pow((c.x+.055)/1.055,2.4),c.y<=.04045?c.y/12.92:pow((c.y+.055)/1.055,2.4),c.z<=.04045?c.z/12.92:pow((c.z+.055)/1.055,2.4));}
float3 ToSrgb(float3 c){return float3(c.x<=.0031308?c.x*12.92:1.055*pow(c.x,1/2.4)-.055,c.y<=.0031308?c.y*12.92:1.055*pow(c.y,1/2.4)-.055,c.z<=.0031308?c.z*12.92:1.055*pow(c.z,1/2.4)-.055);}
float3 BlendPigment(float3 below,float3 above){
 if(BrushColor.w<.5)return above;
 if(BrushColor.w<1.5)return below*above;
 if(BrushColor.w<2.5)return 1-(1-below)*(1-above);
 return float3(below.x<=.5?2*below.x*above.x:1-2*(1-below.x)*(1-above.x),below.y<=.5?2*below.y*above.y:1-2*(1-below.y)*(1-above.y),below.z<=.5?2*below.z*above.z:1-2*(1-below.z)*(1-above.z));
}
float3 Environment(float3 r,float gloss){
 if(Appearance.w>.5)return ReflectionProbe.SampleLevel(LinearSampler,r,(1-gloss)*8).rgb*ProbeSettings.x;
 float3 dark=Artwork.w<.5?float3(.06,.055,.045):float3(.1,.14,.21);
 float sky=saturate(r.y*.5+.5);float3 base=lerp(dark,Artwork.w<.5?float3(.85,.78,.65):float3(.55,.7,.9),sky);
 float bands=pow(saturate(1-abs(r.x*.7+r.z*.3)),lerp(1,40,gloss))*smoothstep(-.15,.15,r.y);
 return base*.35+bands*lerp(.2,1.5,gloss);
}
float3 Illuminate(float3 color,float gloss,float metal,float coat,float3 n,float3 view){
 float a=Lighting.x;float3 light=normalize(float3(-cos(a)*.65,1,sin(a)*.65));float3 h=normalize(light+view);
 float nv=max(.001,saturate(dot(n,view))),nl=saturate(dot(n,light)),nh=saturate(dot(n,h)),vh=saturate(dot(view,h));
 float rough=max(.04,1-gloss),alpha=rough*rough,alpha2=alpha*alpha;
 float d=alpha2/(3.141593*pow(nh*nh*(alpha2-1)+1,2));float k=pow(rough+1,2)/8;
 float g=(nv/(nv*(1-k)+k))*(nl/(nl*(1-k)+k));
 float3 f0=lerp(float3(.04,.04,.04),color,metal);float3 f=f0+(1-f0)*pow(1-vh,5);
 float3 ambient=Appearance.w>.5?IrradianceProbe.SampleLevel(LinearSampler,n,0).rgb*ProbeSettings.x:float3(.38,.38,.38);
 float3 diffuse=color*(1-metal)*(ambient+.72*nl*max(.1,ProbeSettings.y));
 float3 spec=f*d*g/(4*nv*max(nl,.001))*nl*.65;
 float3 reflected=Environment(reflect(-view,n),gloss)*(f0+(1-f0)*pow(1-nv,5));
 float3 coating=(pow(nh,180)*1.3+Environment(reflect(-view,n),.96)*(.04+.12*pow(1-nv,5)))*coat;
 return diffuse+spec+reflected+coating;
}
float3 SurfaceNormal(Pixel i,float3 sampled){
 float2 xy=(sampled.xy*2-1)*Lighting.y;float3 map=float3(xy,sqrt(saturate(1-dot(xy,xy))));float3 n=normalize(i.normal);
 float3 dp1=ddx(i.world),dp2=ddy(i.world);float2 duv1=ddx(i.uv),duv2=ddy(i.uv);float det=duv1.x*duv2.y-duv1.y*duv2.x;
 if(abs(det)<.000001)return n;float3 tangent=normalize((dp1*duv2.y-dp2*duv1.y)/det);tangent=normalize(tangent-n*dot(n,tangent));float3 bitangent=normalize(cross(n,tangent))*sign(det);
 return normalize(map.x*tangent+map.y*bitangent+map.z*n);
}
float4 PS(Pixel i):SV_TARGET {
 if(i.kind>5.5){
  float2 g=abs(frac(i.world.xz)-.5)/max(fwidth(i.world.xz),.001);float grid=1-saturate(min(g.x,g.y));
  float fade=exp(-length(i.world.xz)*.15);float shadow=exp(-dot(i.world.xz/float2(1.1,2.7),i.world.xz/float2(1.1,2.7))*1.5);
  return float4(lerp(float3(.085,.11,.145),float3(.022,.032,.05),shadow*.85)+grid*.028*fade,1);
 }
 float3 n=normalize(i.normal);if(i.kind>.5&&i.kind<1.5&&Appearance.z>.5)n=SurfaceNormal(i,NormalMap.Sample(LinearSampler,i.uv).rgb);float3 view=normalize(Eye.xyz-i.world);if(dot(n,view)<0)n=-n;
 float3 color=float3(.15,.18,.22);float gloss=.35,metal=0,coat=0;
 if(i.kind>.5 && i.kind<1.5){
  float4 t=NormalMode.w>0?StampOverlay.Sample(LinearSampler,i.uv):Albedo.Sample(LinearSampler,i.uv);float facing=saturate(dot(n,view));
  // Same material equations as CarPaint.cg; texture RGB is sRGB, uniform paint is linear.
  float3 paint=lerp(OuterColor.rgb,InnerColor.rgb,facing);
  color=lerp(paint,ToLinear(t.rgb),t.a);
  float3 structure=NormalMode.w>3.5?SurfaceOverlay.Sample(LinearSampler,i.uv).rgb:StructureMap.Sample(LinearSampler,i.uv).rgb;
  if(Material.w>.5&&NormalMode.w<3.5)structure.rg=Artwork.xy;
  structure*=1-structure.b;
  gloss=lerp(lerp(OuterColor.w,Material.z,facing),structure.r,t.a);
  metal=lerp(Material.x,structure.g,t.a);coat=Material.y*(1-t.a);
  float4 damaged=DamageMap.Sample(LinearSampler,i.uv);float damageMask=damaged.a*saturate(Lighting.w*4);color=lerp(color,ToLinear(damaged.rgb),damageMask);gloss=lerp(gloss,Appearance.x,damageMask);metal=lerp(metal,Appearance.y,damageMask);coat*=1-damageMask;
  if(Appearance.z>.5&&Lighting.w>0)n=normalize(lerp(n,SurfaceNormal(i,DamageNormal.Sample(LinearSampler,i.uv).rgb),Lighting.w));
  float4 dirt=DirtMap.Sample(LinearSampler,i.uv);float dirtMask=saturate(dirt.a-1+Lighting.z);float2 dirtFinish=DirtSurface.Sample(LinearSampler,i.uv).rg;color=lerp(color,ToLinear(dirt.rgb),dirtMask);gloss=lerp(gloss,dirtFinish.x,dirtMask);metal=lerp(metal,dirtFinish.y,dirtMask);coat*=1-dirtMask;
 } else if(i.kind>1.5 && i.kind<2.5) color=lerp(float3(.018,.045,.065),float3(.32,.48,.57),pow(1-saturate(dot(n,view)),3));
 else if(i.kind>2.5 && i.kind<3.5) color=float3(.045,.048,.052);
 else if(i.kind>3.5 && i.kind<4.5){color=float3(.42,.35,.22);metal=.85;gloss=.7;}
 if(i.kind>1.5&&i.kind<2.5){gloss=.98;coat=.8;}
 color=Artwork.w>1.5?color:Illuminate(color,gloss,metal,coat,n,view);
 return float4(pow(saturate(color*exp2(Artwork.z)),1/2.2),1);
}
