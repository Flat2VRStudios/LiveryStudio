using System.Text.Json;
using LiveryStudio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;

namespace LiveryStudio;
public sealed partial class CarPaintView
{
    private async void EditDecalEffects(object sender,RoutedEventArgs e)
    {
        if(document is null||SelectedLayers.Count!=1||SelectedLayer is not {Kind:LayerKind.Sticker,Locked:false} layer)return;
        try
        {
            await FlushAsync();var target=document;var project=draft;var before=JsonSerializer.Serialize(target,LayerDocument.Json);var source=await ImageFiles.ReadAsync(layer.Asset);
            var initial=layer.Effects??new();var stencil=initial.StencilFile;var active=true;var updating=false;long revision=0;
            using var gate=new SemaphoreSlim(1,1);CancellationTokenSource? previewCancellation=null;
            var folder=Path.Combine(WorkshopService.DefaultWorkspace,"effect-previews");Directory.CreateDirectory(folder);var previewPath=Path.Combine(folder,Guid.NewGuid().ToString("N")+".png");
            var blend=new DialogComboBox{Header="Blend with the design",ItemsSource=new[]{"Normal · original colors","Multiply · darken","Screen · brighten","Overlay · contrast"},SelectedIndex=(int)initial.Blend,HorizontalAlignment=HorizontalAlignment.Stretch};
            var blendHelp=new TextBlock{FontSize=11,TextWrapping=TextWrapping.Wrap};
            var mask=new DialogComboBox{Header="Shape & fade",ItemsSource=new[]{"Original shape","Oval stencil","Box stencil","Fade across","Fade from center","Image stencil"},SelectedIndex=(int)initial.Mask,HorizontalAlignment=HorizontalAlignment.Stretch};
            var channels=new DialogComboBox{Header="Reveal artwork using",ItemsSource=new[]{"Automatic · transparency or brightness","Transparency","Brightness · white reveals"},SelectedIndex=(int)initial.Channel,HorizontalAlignment=HorizontalAlignment.Stretch};
            NumberBox Number(string label,double value,double min,double max)=>new(){Header=label,Value=value,Minimum=min,Maximum=max,SmallChange=1,LargeChange=10,SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Compact,NumberFormatter=new Windows.Globalization.NumberFormatting.DecimalFormatter{FractionDigits=1,NumberRounder=new Windows.Globalization.NumberFormatting.IncrementNumberRounder{Increment=.1}}};
            var cx=Number("Horizontal center · %",initial.CenterX*100,-200,300);var cy=Number("Vertical center · %",initial.CenterY*100,-200,300);
            var width=Number("Stencil width · %",initial.Width*100,2,500);var height=Number("Stencil height · %",initial.Height*100,2,500);var angle=Number("Stencil rotation · °",initial.Angle,-360,360);
            var feather=new Slider{Minimum=0,Maximum=1,Value=initial.Feather,StepFrequency=.01};AutomationProperties.SetName(feather,"Stencil edge softness");
            var invert=new CheckBox{Content="Invert visible area",IsChecked=initial.Invert};
            var choose=new Button{Content="Choose PNG or TGA stencil",HorizontalAlignment=HorizontalAlignment.Stretch};
            var stencilName=new TextBlock{Text=stencil.Length>0?Path.GetFileName(stencil):"No image stencil selected.",TextWrapping=TextWrapping.Wrap,FontSize=12};
            var preview=new Image{Height=XamlRoot.Size.Height<700?88:160,Stretch=Stretch.Uniform};AutomationProperties.SetName(preview,"Masked decal preview");
            var status=new TextBlock{FontSize=12,TextWrapping=TextWrapping.Wrap};
            var dimensions=new Grid{ColumnSpacing=8,RowSpacing=8};dimensions.ColumnDefinitions.Add(new());dimensions.ColumnDefinitions.Add(new());
            var numbers=new[]{cx,cy,width,height,angle};for(var i=0;i<numbers.Length;i++){if(i%2==0)dimensions.RowDefinitions.Add(new(){Height=GridLength.Auto});Grid.SetRow(numbers[i],i/2);Grid.SetColumn(numbers[i],i%2);dimensions.Children.Add(numbers[i]);}
            var positionBody=new StackPanel{Spacing=10};positionBody.Children.Add(dimensions);positionBody.Children.Add(new TextBlock{Text="50% centers the stencil. 100% matches the artwork's size. Moving or rotating the decal carries its stencil with it.",TextWrapping=TextWrapping.Wrap,FontSize=11});
            var position=new Expander{Header="Stencil position & size",Content=positionBody,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch};
            var softness=new StackPanel{Spacing=4};softness.Children.Add(new TextBlock{Text="Edge softness",FontSize=12});softness.Children.Add(feather);
            var imageOptions=new StackPanel{Spacing=8};imageOptions.Children.Add(choose);imageOptions.Children.Add(stencilName);imageOptions.Children.Add(channels);
            var reset=new Button{Content="Reset effects",HorizontalAlignment=HorizontalAlignment.Left};
            var body=new StackPanel{Spacing=12,Width=Math.Min(380,Math.Max(240,XamlRoot.Size.Width-110))};
            body.Children.Add(new TextBlock{Text=layer.Name,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
            body.Children.Add(new TextBlock{Text="Shape and blend this decal. Your original artwork stays editable.",TextWrapping=TextWrapping.Wrap,FontSize=12});
            body.Children.Add(new Border{Background=new SolidColorBrush(Windows.UI.Color.FromArgb(255,55,60,67)),CornerRadius=new(6),Child=preview,Padding=new(8)});
            body.Children.Add(status);body.Children.Add(mask);body.Children.Add(imageOptions);body.Children.Add(softness);body.Children.Add(invert);body.Children.Add(position);body.Children.Add(blend);body.Children.Add(blendHelp);body.Children.Add(reset);
            body.Children.Add(new TextBlock{Text="Apply updates the car. One Undo restores the previous effects.",TextWrapping=TextWrapping.Wrap,FontSize=11});
            var dialog=new ContentDialog{Title="Decal effects",Content=new ScrollViewer{Content=body,MaxHeight=Math.Clamp(XamlRoot.Size.Height-190,140,680)},XamlRoot=XamlRoot,PrimaryButtonText="Apply effects",CloseButtonText="Cancel",DefaultButton=ContentDialogButton.Primary};
            static float Value(NumberBox field,float fallback)=>double.IsFinite(field.Value)?(float)field.Value:fallback;
            DecalEffectsOptions Options()=>new(){Blend=(DecalBlend)Math.Max(0,blend.SelectedIndex),Mask=(DecalMask)Math.Max(0,mask.SelectedIndex),CenterX=Value(cx,initial.CenterX*100)/100,CenterY=Value(cy,initial.CenterY*100)/100,Width=Value(width,initial.Width*100)/100,Height=Value(height,initial.Height*100)/100,Angle=Value(angle,initial.Angle),Feather=(float)feather.Value,Invert=invert.IsChecked==true,StencilFile=stencil,Channel=(StencilChannel)Math.Max(0,channels.SelectedIndex)};
            async Task Refresh()
            {
                if(!active||updating)return;var version=++revision;previewCancellation?.Cancel();previewCancellation?.Dispose();previewCancellation=new();var token=previewCancellation.Token;var options=Options();
                position.Visibility=invert.Visibility=options.Mask==DecalMask.None?Visibility.Collapsed:Visibility.Visible;
                softness.Visibility=options.Mask is DecalMask.Oval or DecalMask.Box?Visibility.Visible:Visibility.Collapsed;
                imageOptions.Visibility=options.Mask==DecalMask.Image?Visibility.Visible:Visibility.Collapsed;
                blendHelp.Text=options.Blend switch{DecalBlend.Multiply=>"Darkens the design underneath. Apply to see the blend on the car.",DecalBlend.Screen=>"Brightens the design underneath. Apply to see the blend on the car.",DecalBlend.Overlay=>"Adds contrast to the design underneath. Apply to see the blend on the car.",_=>"Keeps the decal's original colors. This preview shows its shape and transparency."};
                dialog.IsPrimaryButtonEnabled=options.Mask!=DecalMask.Image||stencil.Length>0;
                try
                {
                    if(!dialog.IsPrimaryButtonEnabled){preview.Source=null;status.Text="Choose an image stencil to preview and apply it.";return;}
                    status.Text="Updating preview…";await gate.WaitAsync(token);
                    try{var image=await Task.Run(()=>DecalEffects.Apply(source,options,token),token);if(!active||version!=revision)return;await ImageFiles.WriteAsync(image,previewPath);if(!active||version!=revision)return;preview.Source=new BitmapImage{CreateOptions=BitmapCreateOptions.IgnoreImageCache,UriSource=new Uri(previewPath)};status.Text=options.Mask==DecalMask.None?"Original shape":"Shape preview · transparent areas reveal the car";}
                    finally{gate.Release();}
                }
                catch(OperationCanceledException){}
                catch(Exception x){if(active&&version==revision){status.Text=x.Message;dialog.IsPrimaryButtonEnabled=false;}}
            }
            foreach(var field in numbers)field.ValueChanged+=async(_,_)=>await Refresh();
            blend.SelectionChanged+=async(_,_)=>await Refresh();mask.SelectionChanged+=async(_,_)=>await Refresh();channels.SelectionChanged+=async(_,_)=>await Refresh();feather.ValueChanged+=async(_,_)=>await Refresh();invert.Click+=async(_,_)=>await Refresh();
            reset.Click+=async(_,_)=>
            {
                updating=true;
                try{mask.SelectedIndex=(int)DecalMask.None;blend.SelectedIndex=(int)DecalBlend.Normal;cx.Value=cy.Value=50;width.Value=height.Value=100;angle.Value=0;feather.Value=.05;invert.IsChecked=false;channels.SelectedIndex=(int)StencilChannel.Automatic;stencil="";stencilName.Text="No image stencil selected.";position.IsExpanded=false;}
                finally{updating=false;}
                await Refresh();
            };
            choose.Click+=async(_,_)=>
            {
                try{var picker=new FileOpenPicker();WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(HostWindow!));picker.FileTypeFilter.Add(".png");picker.FileTypeFilter.Add(".tga");if(await picker.PickSingleFileAsync() is not {} file)return;stencil=await Task.Run(()=>ArtworkLibrary.Add(file.Path,WorkshopService.DefaultWorkspace));stencilName.Text=Path.GetFileName(file.Path);mask.SelectedIndex=(int)DecalMask.Image;await Refresh();}catch(Exception x){status.Text=x.Message;}
            };
            await Refresh();
            ContentDialogResult result;
            try{result=await dialog.ShowAsync();}
            finally{active=false;previewCancellation?.Cancel();await gate.WaitAsync();gate.Release();previewCancellation?.Dispose();}
            if(result!=ContentDialogResult.Primary)return;
            DecalEffectsOptions? effects=Options();effects.Validate(files:true);
            if(!ReferenceEquals(document,target)||draft!=project||JsonSerializer.Serialize(target,LayerDocument.Json)!=before){CanvasStatus.Text="The design changed while effects were open. Select the decal and try again.";return;}
            if(effects.IsNeutral)effects=null;
            if(JsonSerializer.Serialize(layer.Effects)==JsonSerializer.Serialize(effects))return;
            document.Commit("Decal effects · "+layer.Name,layers=>layers.Single(l=>l.Id==layer.Id).Effects=effects);ClearDirectPreview();QueueComposite();
        }
        catch(Exception x){Fail(x);}
    }
}
