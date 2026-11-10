using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Neobash.PrintBridge.Desktop;

public sealed partial class DeviceArt : UserControl
{
    public static readonly DependencyProperty CategoryProperty = DependencyProperty.Register(
        nameof(Category),
        typeof(string),
        typeof(DeviceArt),
        new PropertyMetadata("Physical", OnCategoryChanged));

    public DeviceArt()
    {
        InitializeComponent();
        Apply(Category);
    }

    public string Category
    {
        get => (string)GetValue(CategoryProperty);
        set => SetValue(CategoryProperty, value);
    }

    private static void OnCategoryChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is DeviceArt art)
        {
            art.Apply(args.NewValue as string);
        }
    }

    private void Apply(string? category)
    {
        if (PhysicalPath is null)
        {
            return;
        }

        PhysicalPath.Visibility = category == "Physical" ? Visibility.Visible : Visibility.Collapsed;
        ReceiptPath.Visibility = category == "Receipt" ? Visibility.Visible : Visibility.Collapsed;
        VirtualPath.Visibility = category == "Virtual" ? Visibility.Visible : Visibility.Collapsed;
        var known = category is "Physical" or "Receipt" or "Virtual";
        Fallback.Visibility = known ? Visibility.Collapsed : Visibility.Visible;
    }
}
