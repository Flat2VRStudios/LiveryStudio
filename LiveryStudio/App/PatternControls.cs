using LiveryStudio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;

namespace LiveryStudio;
public sealed partial class CarPaintView
{
    private PatternArtworkOptions? pendingPattern;
    private Guid? editingPatternLayer;
    private bool updatingPattern,patternPreviewBusy;
    private long patternRevision;
    private CancellationTokenSource? patternCancellation;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? patternPreviewTimer;
    private PatternArtworkOptions CurrentPattern()=>new()
    {
        Kind=(PatternKind)PatternSelector.SelectedIndex,ColorA=PatternColorA.Text,ColorB=PatternColorB.Text,AlphaA=(float)PatternAlphaA.Value,AlphaB=PatternTransparent.IsChecked==true?0:(float)PatternAlphaB.Value,Angle=(float)PatternAngle.Value,
        Repeats=(int)Math.Round(PatternRepeats.Value),Fill=(float)PatternFill.Value,Phase=(float)PatternPhase.Value,Transition=(float)PatternTransition.Value,CenterX=(float)PatternCenterX.Value,CenterY=(float)PatternCenterY.Value,Radius=(float)PatternRadius.Value,
        EdgeFade=(float)PatternEdgeFade.Value,Aspect=(float)PatternAspect.Value,Resolution=PatternResolution.SelectedIndex switch{0=>512,2=>2048,_=>1024}
    };
    private async void InitializePatternControls()
    {
        PatternRepeats.NumberFormatter=new Windows.Globalization.NumberFormatting.DecimalFormatter{IntegerDigits=1,FractionDigits=0,NumberRounder=new Windows.Globalization.NumberFormatting.IncrementNumberRounder{Increment=1}};
        PatternAspect.NumberFormatter=new Windows.Globalization.NumberFormatting.DecimalFormatter{IntegerDigits=1,FractionDigits=2,NumberRounder=new Windows.Globalization.NumberFormatting.IncrementNumberRounder{Increment=.01}};
        patternPreviewTimer=DispatcherQueue.CreateTimer();patternPreviewTimer.Interval=TimeSpan.FromMilliseconds(180);patternPreviewTimer.IsRepeating=true;patternPreviewTimer.Tick+=async(_,_)=>await RefreshPatternPreview();SchedulePatternPreview();
        try{var pixels=new byte[256*128*4];for(var y=0;y<128;y++)for(var x=0;x<256;x++){var k=(y*256+x)*4;var value=((x/12+y/12)&1)==0?(byte)40:(byte)65;pixels[k]=value;pixels[k+1]=(byte)(value+5);pixels[k+2]=(byte)(value+12);pixels[k+3]=255;}var path=Path.Combine(WorkshopService.DefaultWorkspace,"pattern-checkerboard.png");await ImageFiles.WriteAsync(new(256,128,pixels),path);PatternCheckerboard.Source=new BitmapImage(new Uri(path));}catch(Exception x){PatternStatus.Text=x.Message;}
    }
    private void SchedulePatternPreview()
    {
        if(updatingPattern||patternPreviewTimer is null)return;patternRevision++;patternCancellation?.Cancel();patternCancellation?.Dispose();patternCancellation=new();patternPreviewTimer.Start();SyncPatternOptions();
    }
    private void SyncPatternOptions()
    {
        var kind=(PatternKind)PatternSelector.SelectedIndex;var gradient=kind is PatternKind.LinearGradient or PatternKind.RadialGradient;PatternRepeats.IsEnabled=PatternPhase.IsEnabled=!gradient;PatternFill.IsEnabled=kind is PatternKind.Stripes or PatternKind.Dots;
        PatternTransition.Visibility=gradient?Visibility.Visible:Visibility.Collapsed;PatternRadialFields.Visibility=kind==PatternKind.RadialGradient?Visibility.Visible:Visibility.Collapsed;PatternAngle.IsEnabled=kind!=PatternKind.RadialGradient;PatternAlphaB.IsEnabled=PatternTransparent.IsChecked!=true;
    }
    private void PatternTextChanged(object sender,TextChangedEventArgs e)=>SchedulePatternPreview();
    private void PatternChoiceChanged(object sender,SelectionChangedEventArgs e)=>SchedulePatternPreview();
    private void PatternSliderChanged(object sender,RangeBaseValueChangedEventArgs e)=>SchedulePatternPreview();
    private void PatternNumberChanged(NumberBox sender,NumberBoxValueChangedEventArgs e)=>SchedulePatternPreview();
    private void PatternToggleChanged(object sender,RoutedEventArgs e)=>SchedulePatternPreview();
    private async Task RefreshPatternPreview()
    {
        if(patternPreviewBusy||patternCancellation is null)return;patternPreviewBusy=true;var revision=patternRevision;var token=patternCancellation.Token;
        try{var options=CurrentPattern();var image=await Task.Run(()=>PatternArtwork.Render(options,256,token),token);if(revision!=patternRevision)return;var path=Path.Combine(WorkshopService.DefaultWorkspace,"pattern-preview.png");await ImageFiles.WriteAsync(image,path);if(revision!=patternRevision)return;PatternPreview.Source=new BitmapImage{CreateOptions=BitmapCreateOptions.IgnoreImageCache,UriSource=new Uri(path)};PatternStatus.Text=PatternArtwork.Name(options.Kind)+" · checkerboard shows transparency";patternPreviewTimer?.Stop();}
        catch(OperationCanceledException){}catch(Exception x){if(revision==patternRevision){PatternStatus.Text=x.Message;patternPreviewTimer?.Stop();}}finally{patternPreviewBusy=false;}
    }
    private async void PickPatternColor(object sender,RoutedEventArgs e)
    {
        if(sender is not FrameworkElement {Tag:string tag})return;var field=tag=="A"?PatternColorA:PatternColorB;
        try{await PickArtworkColor(field,tag=="A"?"Pattern first color":"Pattern second color");}
        catch(Exception x){PatternStatus.Text=x.Message;}
    }
    private async void UsePattern(object sender,RoutedEventArgs e)
    {
        UsePatternButton.IsEnabled=false;var request=++stickerRequestRevision;stickerLoading=true;
        try{var options=CurrentPattern();options.Validate();var path=await Task.Run(()=>PatternArtwork.Generate(options,WorkshopService.DefaultWorkspace));if(request!=stickerRequestRevision||!await UseSticker(path,request:request))return;RememberColors(options.ColorB,options.ColorA);ArtworkSearch.Text="";RefreshArtwork();HideInspector();CanvasStatus.Text="Pattern ready · hover over a panel, then click to place it.";}
        catch(Exception x){PatternStatus.Text=x.Message;}finally{if(request==stickerRequestRevision)stickerLoading=false;UsePatternButton.IsEnabled=true;}
    }
    private void LoadPatternFields(PatternArtworkOptions options)
    {
        updatingPattern=true;
        try{PatternSelector.SelectedIndex=(int)options.Kind;PatternColorA.Text=options.ColorA;PatternColorB.Text=options.ColorB;PatternAlphaA.Value=options.AlphaA;PatternAlphaB.Value=options.AlphaB==0?1:options.AlphaB;PatternTransparent.IsChecked=options.AlphaB==0;PatternAngle.Value=options.Angle;PatternRepeats.Value=options.Repeats;PatternFill.Value=options.Fill;PatternPhase.Value=options.Phase;PatternTransition.Value=options.Transition;PatternCenterX.Value=options.CenterX;PatternCenterY.Value=options.CenterY;PatternRadius.Value=options.Radius;PatternEdgeFade.Value=options.EdgeFade;PatternAspect.Value=options.Aspect;PatternResolution.SelectedIndex=options.Resolution==512?0:options.Resolution==2048?2:1;}finally{updatingPattern=false;}
        SchedulePatternPreview();
    }
    private void EditPattern(object sender,RoutedEventArgs e)
    {
        if(SelectedLayers.Count!=1||SelectedLayer is not {Locked:false,PatternArtwork:{} options} layer)return;editingPatternLayer=layer.Id;LoadPatternFields(options);PatternEditStatus.Text="Editing: "+layer.Name;UpdatePatternButton.Visibility=Visibility.Visible;UsePatternButton.Content="Use as a new pattern";InspectorSection.SelectedIndex=4;TextExpander.IsExpanded=false;PatternDesigner.IsExpanded=true;if(floatingInspector)ShowInspector();DispatcherQueue.TryEnqueue(()=>{PatternDesigner.StartBringIntoView(new BringIntoViewOptions{VerticalAlignmentRatio=0,AnimationDesired=false});PatternSelector.Focus(FocusState.Programmatic);});
    }
    private async void UpdatePattern(object sender,RoutedEventArgs e)
    {
        if(document is null||editingPatternLayer is not {} id)return;var current=document;UsePatternButton.IsEnabled=UpdatePatternButton.IsEnabled=false;
        try{await FlushAsync();var options=CurrentPattern();options.Validate();var path=await Task.Run(()=>PatternArtwork.Generate(options,WorkshopService.DefaultWorkspace));if(!ReferenceEquals(document,current)||document.Layers.All(l=>l.Id!=id))return;document.Commit("Edited pattern",layers=>{var layer=layers.Single(l=>l.Id==id);if(layer.Locked)throw new InvalidOperationException("Unlock this pattern before editing it.");layer.PatternArtwork=options with {};layer.Asset=path;layer.Name=PatternArtwork.Name(options.Kind);});selectedLayer=id;RememberColors(options.ColorB,options.ColorA);QueueComposite();RefreshArtwork();PatternEditStatus.Text="Pattern updated · placement and finish preserved.";}
        catch(Exception x){PatternStatus.Text=x.Message;}finally{UsePatternButton.IsEnabled=UpdatePatternButton.IsEnabled=true;}
    }
    private void SyncPatternHistory(){if(editingPatternLayer is not {} id)return;if(document?.Layers.FirstOrDefault(l=>l.Id==id)?.PatternArtwork is {} options){LoadPatternFields(options);PatternEditStatus.Text="Editing: "+PatternArtwork.Name(options.Kind);}else ResetPatternEditing();}
    private void ResetPatternEditing(){editingPatternLayer=null;if(UpdatePatternButton is not null){UpdatePatternButton.Visibility=Visibility.Collapsed;PatternEditStatus.Text="";UsePatternButton.Content="Create & use pattern";}}
    private void DisposePatternPreview(){patternRevision++;patternPreviewTimer?.Stop();patternCancellation?.Cancel();patternCancellation?.Dispose();}
}
