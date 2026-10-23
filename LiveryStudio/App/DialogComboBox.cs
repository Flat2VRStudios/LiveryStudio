using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace LiveryStudio;

// Scrolling a short dialog should not silently change a closed choice. Keep
// keyboard selection and the open dropdown's native scrolling behavior.
public sealed class DialogComboBox : ComboBox
{
    protected override void OnPointerWheelChanged(PointerRoutedEventArgs e)
    {
        if(IsDropDownOpen){base.OnPointerWheelChanged(e);return;}
        for(DependencyObject? parent=VisualTreeHelper.GetParent(this);parent is not null;parent=VisualTreeHelper.GetParent(parent))
        {
            if(parent is not ScrollViewer scroll||scroll.ScrollableHeight<=0)continue;
            var delta=e.GetCurrentPoint(this).Properties.MouseWheelDelta;
            scroll.ChangeView(null,Math.Clamp(scroll.VerticalOffset-delta*.5,0,scroll.ScrollableHeight),null,true);
            break;
        }
        e.Handled=true;
    }
}
