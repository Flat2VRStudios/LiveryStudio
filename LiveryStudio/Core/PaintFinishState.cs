namespace LiveryStudio.Core;

public sealed record PaintFinishState(double OuterGloss,double InnerGloss,double Metalness,double ClearCoat,bool OverrideArtworkFinish,double ArtworkGloss,double ArtworkMetalness,double DamageGloss,double DamageMetalness)
{
    public static PaintFinishState From(LiveryProject p)=>new(p.OuterGloss,p.InnerGloss,p.Metalness,p.ClearCoat,p.OverrideArtworkFinish,p.ArtworkGloss,p.ArtworkMetalness,p.DamageGloss,p.DamageMetalness);
    public LiveryProject Apply(LiveryProject p)=>p with {OuterGloss=OuterGloss,InnerGloss=InnerGloss,Metalness=Metalness,ClearCoat=ClearCoat,OverrideArtworkFinish=OverrideArtworkFinish,ArtworkGloss=ArtworkGloss,ArtworkMetalness=ArtworkMetalness,DamageGloss=DamageGloss,DamageMetalness=DamageMetalness};
    public void Validate(){if(new[]{OuterGloss,InnerGloss,Metalness,ClearCoat,ArtworkGloss,ArtworkMetalness,DamageGloss,DamageMetalness}.Any(v=>!double.IsFinite(v)||v<0||v>1))throw new InvalidDataException("Invalid saved paint finish.");}
}
