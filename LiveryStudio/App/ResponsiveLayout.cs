using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LiveryStudio;

public partial class MainWindow
{
	private bool singleColumnDesign;
    private void ApplyMinimumWindowSize()
    {
        if(AppWindow.Presenter is not Microsoft.UI.Windowing.OverlappedPresenter presenter)return;
        var scale=Root.XamlRoot?.RasterizationScale??1;
        var area=Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id,Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
        presenter.PreferredMinimumWidth=Math.Min(area.Width,(int)Math.Ceiling(640*scale));
        presenter.PreferredMinimumHeight=Math.Min(area.Height,(int)Math.Ceiling(480*scale));
    }
	private void ChooseWindowLayout(object sender,RoutedEventArgs e)
	{
		ApplyWindowLayout((sender as MenuFlyoutItem)?.Tag as string??"comfort");
	}
	private void ApplyWindowLayout(string choice)
	{
		if(AppWindow.Presenter is not Microsoft.UI.Windowing.OverlappedPresenter presenter)return;
		if(choice=="max"){presenter.Maximize();return;}
		presenter.Restore();var area=Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id,Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
		var size=choice switch{"compact"=>new Windows.Graphics.SizeInt32(640,480),"comfort"=>new Windows.Graphics.SizeInt32(1000,760),_=>new Windows.Graphics.SizeInt32(1440,960)};
        var scale=Root.XamlRoot?.RasterizationScale??1;
        size.Width=Math.Min((int)Math.Round(size.Width*scale),area.Width);size.Height=Math.Min((int)Math.Round(size.Height*scale),area.Height);
        ApplyMinimumWindowSize();AppWindow.Resize(size);AppWindow.Move(new Windows.Graphics.PointInt32(area.X+(area.Width-size.Width)/2,area.Y+(area.Height-size.Height)/2));
	}
	private void WindowLayoutChanged(object sender,SizeChangedEventArgs e)
	{
		// SizeChanged can run during InitializeComponent, before the named controls exist.
		if(Form is null || Footer is null)return;
		var compact=e.NewSize.Width<1120||AdvancedModeToggle.IsChecked!=true;
		var shortWindow=e.NewSize.Height<700;
		var singleColumn=e.NewSize.Width<1180;
		NavigationColumn.Width=new GridLength(compact?68:212);
		NavigationRail.Padding=compact?new Thickness(8,18,8,12):new Thickness(18,30,18,30);
		NavigationHeading.Visibility=NavigationPromo.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
		NavigationPromo.Visibility=compact||shortWindow?Visibility.Collapsed:Visibility.Visible;
		foreach(var label in new[]{DesignNavLabel,PaintNavLabel,CarBuilderLabel,LibraryNavLabel,SetupNavLabel})label.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
		foreach(var button in new[]{DesignNav,PaintNav,CarBuilderNav,LibraryNav,SetupNav})
		{
			button.Padding=new Thickness(16,13,16,13);
			button.HorizontalContentAlignment=compact?HorizontalAlignment.Center:HorizontalAlignment.Left;
		}
		ContentLayout.Padding=compact?new Thickness(16,12,16,12):new Thickness(32,26,32,20);
		PageHeading.Margin=new Thickness(0,0,0,shortWindow?10:20);
		PageTitle.FontSize=compact||shortWindow?24:32;
		PageSubtitle.Visibility=shortWindow?Visibility.Collapsed:Visibility.Visible;
		NewLabel.Visibility=OpenLabel.Visibility=SaveLabel.Visibility=e.NewSize.Width<850?Visibility.Collapsed:Visibility.Visible;
		DesignSettingsButton.Content=compact?"Settings":"Finish & texture settings";
		Footer.Padding=compact?new Thickness(16,6,16,6):new Thickness(24,8,24,8);
		Notice.Margin=new Thickness(0,0,0,shortWindow?8:16);
		DesignView.ColumnSpacing=singleColumn?0:24;
		FormColumn.Width=new GridLength(singleColumn?0:420);
		if(singleColumn!=singleColumnDesign)
		{
			// Move the existing editor: controls, bindings and draft edits survive resizing.
			var previousLoading=loading;
			loading=true;
			try
			{
				if(singleColumn)
				{
					FormScroller.Content=null;
					DesignCards.Children.Insert(0,Form);
				}
				else
				{
					DesignCards.Children.Remove(Form);
					FormScroller.Content=Form;
				}
			}
			finally{loading=previousLoading;}
			singleColumnDesign=singleColumn;
		}
		FormScroller.Visibility=singleColumn?Visibility.Collapsed:Visibility.Visible;
	}
}

public sealed partial class CarPaintView
{
	private bool floatingInspector;
	private bool previewOnly;
	public void SetPreviewOnly(bool value){previewOnly=value;if(value)Select(0);UpdatePaintLayout(new(ActualWidth,ActualHeight));}
	private bool compactInspectorOpen;
	private void PaintLayoutChanged(object sender,SizeChangedEventArgs e)
		=>UpdatePaintLayout(e.NewSize);
	private void UpdatePaintLayout(Windows.Foundation.Size size)
	{
		if(InspectorTabs is null || DockedInspector is null)return;
		SimpleTray.Visibility=(!AdvancedMode||advancedQuickTools)&&!previewOnly&&!ImageReviewActive?Visibility.Visible:Visibility.Collapsed;
		if(SimpleTray.Visibility==Visibility.Visible)SimpleTray.SetAvailableHeight(size.Height);
		SimpleCanvasControls.Visibility=!AdvancedMode&&!previewOnly&&!ImageReviewActive?Visibility.Visible:Visibility.Collapsed;
		SimpleCanvasStatus.Visibility=SimpleCanvasControls.Visibility;
		if(!AdvancedMode&&!previewOnly&&!ImageReviewActive)
		{
			SimpleTray.SetAvailableHeight(size.Height);
			InspectorColumn.Width=new(0);PaintLayout.ColumnSpacing=0;DockedInspector.Visibility=InspectorDismiss.Visibility=InspectorToggle.Visibility=QuickTools.Visibility=CompactCameraButton.Visibility=CanvasHeading.Visibility=CanvasHelp.Visibility=Visibility.Collapsed;
			SimpleSelectionName.Visibility=size.Width<650?Visibility.Collapsed:Visibility.Visible;SyncSimpleSelection();return;
		}
		if(previewOnly){InspectorColumn.Width=new GridLength(0);PaintLayout.ColumnSpacing=0;DockedInspector.Visibility=InspectorDismiss.Visibility=InspectorToggle.Visibility=QuickTools.Visibility=Visibility.Collapsed;CompactCameraButton.Visibility=Visibility.Visible;CanvasHeading.Visibility=Visibility.Visible;CanvasHelp.Visibility=size.Height<300?Visibility.Collapsed:Visibility.Visible;return;}
        var compact=size.Width<800;
		if(compact!=floatingInspector)
		{
			compactInspectorOpen=false;
			floatingInspector=compact;
		}
		InspectorColumn.Width=new GridLength(compact||ImageReviewActive?0:320);
		PaintLayout.ColumnSpacing=compact||ImageReviewActive?0:20;
		// The inspector keeps one visual parent for its entire lifetime. Moving
		// controls between a popup HWND and the window invalidated their UIA peers.
		Grid.SetColumn(DockedInspector,compact?0:1);Grid.SetColumnSpan(DockedInspector,compact?2:1);
		DockedInspector.HorizontalAlignment=compact?HorizontalAlignment.Right:HorizontalAlignment.Stretch;DockedInspector.VerticalAlignment=compact?VerticalAlignment.Top:VerticalAlignment.Stretch;
		DockedInspector.Width=compact?Math.Min(344,Math.Max(240,size.Width-28)):double.NaN;DockedInspector.Padding=compact?new Thickness(12):new Thickness(0);DockedInspector.CornerRadius=new(compact?12:0);
		DockedInspector.Background=compact?(Brush)Application.Current.Resources["StudioCardBrush"]:null;DockedInspector.BorderBrush=compact?(Brush)Application.Current.Resources["StudioBorderBrush"]:null;DockedInspector.BorderThickness=new(compact?1:0);
		if(compact&&HostWindow?.Content is FrameworkElement root){var origin=PaintLayout.TransformToVisual(root).TransformPoint(new(0,0));var bottom=Math.Min(origin.Y+size.Height,root.ActualHeight-60);var height=Math.Min(Math.Clamp(root.ActualHeight-130,180,500),Math.Max(180,bottom-60));var top=Math.Max(60,bottom-height);DockedInspector.Height=height;DockedInspector.Margin=new(0,top-origin.Y,14,0);}else{DockedInspector.Height=double.NaN;DockedInspector.Margin=new(0);}
		InspectorCloseRow.Visibility=compact?Visibility.Visible:Visibility.Collapsed;
		Grid.SetRow(InspectorCloseRow,1);Canvas.SetZIndex(InspectorCloseRow,1);
		InspectorCloseRow.HorizontalAlignment=HorizontalAlignment.Right;InspectorCloseRow.VerticalAlignment=VerticalAlignment.Top;InspectorCloseRow.Margin=new(0);InspectorCloseRow.Width=28;
		InspectorHeading.Visibility=Visibility.Collapsed;InspectorSection.Margin=compact?new Thickness(0,0,38,0):new Thickness(0);InspectorTabs.RowSpacing=compact?8:12;
		SyncInspectorCommands();SyncInspectorVisibility();
		CanvasHeading.Margin=compact?new Thickness(14,12,140,12):new Thickness(18);
		VehicleTitle.FontSize=compact?16:20;
		CanvasHeading.Visibility=ImageReviewActive?Visibility.Collapsed:Visibility.Visible;
		CanvasStatus.Visibility=size.Height<280?Visibility.Collapsed:Visibility.Visible;
		CanvasHelp.Visibility=ImageReviewActive||inspectingLayers||size.Height<360?Visibility.Collapsed:Visibility.Visible;
		QuickTools.Visibility=ImageReviewActive||size.Height<200?Visibility.Collapsed:Visibility.Visible;
		CompactCameraButton.Visibility=!ImageReviewActive&&QuickTools.Visibility==Visibility.Collapsed?Visibility.Visible:Visibility.Collapsed;
		QuickTools.Margin=new Thickness(14,size.Height<280?44:72,14,0);
	}
	private void SyncInspectorVisibility(){if(!AdvancedMode&&!previewOnly){DockedInspector.Visibility=InspectorDismiss.Visibility=InspectorToggle.Visibility=Visibility.Collapsed;return;}DockedInspector.Visibility=!previewOnly&&!ImageReviewActive&&(!floatingInspector||compactInspectorOpen)?Visibility.Visible:Visibility.Collapsed;InspectorDismiss.Visibility=!previewOnly&&!ImageReviewActive&&floatingInspector&&compactInspectorOpen?Visibility.Visible:Visibility.Collapsed;InspectorToggle.Visibility=!previewOnly&&!ImageReviewActive&&floatingInspector&&!compactInspectorOpen?Visibility.Visible:Visibility.Collapsed;}
	private void ShowInspector(){if(!AdvancedMode){SimpleTray.SetExpanded(true);return;}if(!floatingInspector)return;compactInspectorOpen=true;SyncInspectorVisibility();InspectorSection.Focus(FocusState.Programmatic);}
	private void HideInspector(){if(!floatingInspector)return;compactInspectorOpen=false;SyncInspectorVisibility();}
	private void ToggleInspector(object sender,RoutedEventArgs e){if(compactInspectorOpen)HideInspector();else ShowInspector();}
	private void CloseInspector(object sender,RoutedEventArgs e){HideInspector();InspectorToggle.Focus(FocusState.Programmatic);}
	public void DismissInspectorOutside(Windows.Foundation.Point point)
	{
		if(!floatingInspector||!compactInspectorOpen||HostWindow?.Content is not UIElement root)return;
		bool Contains(FrameworkElement element){var origin=element.TransformToVisual(root).TransformPoint(new(0,0));return new Windows.Foundation.Rect(origin.X,origin.Y,element.ActualWidth,element.ActualHeight).Contains(point);}
		if(!Contains(DockedInspector)&&!Contains(InspectorToggle))HideInspector();
	}
}
