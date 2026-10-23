using System.Numerics;
using System.Text.Json;
using LiveryStudio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace LiveryStudio;

public sealed partial class CarPaintView
{
    private bool chooseArtworkAtPoint;
    private Flyout? artworkPointFlyout;
    private CancellationTokenSource? artworkPointCancellation;
    private long artworkPointRevision;
    private readonly SemaphoreSlim artworkPointGate=new(1,1);
    private void CancelArtworkPointPick(bool keepArmed=false)
    {
        chooseArtworkAtPoint=keepArmed&&chooseArtworkAtPoint;artworkPointRevision++;artworkPointCancellation?.Cancel();artworkPointCancellation=null;
        artworkPointFlyout?.Hide();artworkPointFlyout=null;
    }

    private void BeginArtworkPointPick(object sender,RoutedEventArgs e)
    {
        Select(6);chooseArtworkAtPoint=true;HideInspector();SelectTool.Focus(FocusState.Programmatic);
        CanvasStatus.Text="Click the car to choose between overlapping decals. Escape cancels. You can also Shift-click in Select.";
    }

    private async void ChooseArtworkAtPoint(SurfaceHit? hit,Vector2 at)
    {
        CancelArtworkPointPick();
        if(document is null||mesh is null||painter is null)return;
        using var cancellation=new CancellationTokenSource();artworkPointCancellation=cancellation;
        var request=artworkPointRevision;var token=cancellation.Token;
        var phase="finding decals";
        try
        {
            FinishDirectEdit(cancel:true);transformTimer?.Stop();CommitTransform();artworkPointFlyout?.Hide();
            if(hit is not {} point){CanvasStatus.Text="Click a painted body panel to find its decals.";return;}
            var current=document;var version=generation;var cursor=current.Cursor;var camera=CameraPose;var aspect=CameraAspect;
            var before=JsonSerializer.Serialize(current.Layers,LayerDocument.Json);
            var model=mesh;var textureWidth=painter.Image.Width;var textureHeight=painter.Image.Height;
            var wholeGroup=!IsDown(VirtualKey.Menu);var addSelection=IsDown(VirtualKey.Control);
            bool Unchanged()=>request==artworkPointRevision&&ReferenceEquals(document,current)&&generation==version&&mode==6&&!dragging&&!directDragging&&CameraPose==camera&&CameraAspect==aspect&&current.Cursor==cursor&&JsonSerializer.Serialize(current.Layers,LayerDocument.Json)==before;
            CanvasStatus.Text="Finding decals at this point…";
            IReadOnlyList<DecalSelection> choices;await artworkPointGate.WaitAsync(token);
            try
            {
                choices=await Task.Run(()=>
                {
                    var layers=JsonSerializer.Deserialize<PaintLayer[]>(before,LayerDocument.Json)!;
                    return DecalEditing.PickAll(model,layers,point,textureWidth,textureHeight,path=>FinishMaps.Read(path,token),includeLocked:true,cancellation:token);
                },token);
            }
            finally{artworkPointGate.Release();}
            token.ThrowIfCancellationRequested();if(!Unchanged())return;
            if(choices.Count==0){CanvasStatus.Text="No decal pixels at this point. Try another spot, or find paint and material strokes in Layers.";return;}
            var existing=SelectedLayers.Select(l=>l.Id).ToArray();
            phase="preparing choices";
            var together=new CheckBox{Content="Select the whole group",IsChecked=wholeGroup};
            var add=new CheckBox{Content="Add to my selection",IsChecked=addSelection};
            var width=Math.Clamp(XamlRoot.Size.Width-100,180,340);
            var body=new StackPanel{Spacing=8,Width=width};
            body.KeyDown+=(_,args)=>{if(args.Key==VirtualKey.Escape){CancelArtworkPointPick();CanvasStatus.Text="Decal choice canceled · your selection is unchanged.";args.Handled=true;}};
            body.Children.Add(new TextBlock{Text="Decals at this point",FontSize=18,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold});
            body.Children.Add(new TextBlock{Text=$"{choices.Count} matching layers · topmost first",FontSize=12,TextWrapping=TextWrapping.Wrap});
            body.Children.Add(together);body.Children.Add(add);
            var rows=new StackPanel{Spacing=4};var flyout=new Flyout{XamlRoot=XamlRoot,Content=body};artworkPointFlyout=flyout;
            foreach(var choice in choices)
            {
                phase="preparing a decal thumbnail";
                var layer=current.Layers.Single(l=>l.Id==choice.LayerId);
                var grid=new Grid{ColumnSpacing=8};grid.ColumnDefinitions.Add(new(){Width=new GridLength(40)});grid.ColumnDefinitions.Add(new());
                var preview=new Image{Width=40,Height=40,Stretch=Stretch.Uniform,DataContext=layer};preview.Loaded+=LayerPreviewLoaded;preview.Unloaded+=LayerPreviewUnloaded;grid.Children.Add(preview);
                var caption=new StackPanel{Spacing=2,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(caption,1);
                caption.Children.Add(new TextBlock{Text=layer.Name,TextTrimming=TextTrimming.CharacterEllipsis});
                var detail=string.Join(" · ",new[]{layer.GroupId.HasValue?layer.GroupName:"",layer.Locked?"Locked":"",choice.SharedUv?"Shared UVs":choice.Mirrored?"Paired side":"",layer.Samples.Count>1?"Multiple placements":""}.Where(s=>s.Length>0));
                if(detail.Length>0)caption.Children.Add(new TextBlock{Text=detail,FontSize=11,Opacity=.75,TextWrapping=TextWrapping.Wrap});grid.Children.Add(caption);
                var button=new Button{Content=grid,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(8,6,8,6)};
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button,"Select "+layer.Name+(layer.Locked?" · locked":""));ToolTipService.SetToolTip(button,layer.Name);
                button.Click+=(_,_)=>
                {
                    flyout.Hide();
                    if(!Unchanged())
                    {CanvasStatus.Text="The design changed. Click the car again to refresh its decals.";return;}
                    var ids=together.IsChecked==true?LayerSets.ExpandGroups(current.Layers,[choice.LayerId]):[choice.LayerId];
                    directSelection=choice;SetLayerSelection(add.IsChecked==true?existing.Concat(ids).Distinct():ids,choice.LayerId);
                    InspectorSection.SelectedIndex=1;ShowLayerWorkspace(1);HideInspector();SelectTool.Focus(FocusState.Programmatic);UploadSelection();Render();
                    CanvasStatus.Text=SelectedLayers.Any(l=>l.Locked)?"Selected "+layer.Name+" · unlock this selection in Edit to change it.":choice.SharedUv?"Selected "+layer.Name+" · edits affect every panel using these UVs.":"Selected "+layer.Name+" · use its handles or open Edit.";
                };
                rows.Children.Add(button);
            }
            body.Children.Add(new ScrollViewer{Content=rows,MaxHeight=Math.Clamp(XamlRoot.Size.Height-280,80,360),HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
            var close=new Button{Content="Cancel",HorizontalAlignment=HorizontalAlignment.Stretch};close.Click+=(_,_)=>flyout.Hide();body.Children.Add(close);
            flyout.Closed+=(_,_)=>{if(ReferenceEquals(artworkPointFlyout,flyout))artworkPointFlyout=null;};
            phase="opening choices";
            flyout.ShowAt(Viewport,new FlyoutShowOptions{Position=new(at.X,at.Y)});
            CanvasStatus.Text="Choose a decal from the list · covered layers stay in their original order.";
        }
        catch(OperationCanceledException){}
        catch(Exception x){if(request==artworkPointRevision)Fail(new InvalidOperationException("Could not show decal choices while "+phase+": "+x.Message,x));}
        finally{if(ReferenceEquals(artworkPointCancellation,cancellation))artworkPointCancellation=null;}
    }
}
