using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;

namespace Neobash.PrintBridge.Desktop;

public sealed partial class DeviceCard : UserControl
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(nameof(Model), typeof(DeviceCardModel), typeof(DeviceCard), new PropertyMetadata(null, OnModelChanged));
    public DeviceCardModel? Model { get => (DeviceCardModel?)GetValue(ModelProperty); set => SetValue(ModelProperty, value); }
    public DeviceCard() => InitializeComponent();
    private static void OnModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var card = (DeviceCard)sender;
        if (args.NewValue is not DeviceCardModel model) return;
        card.OpenButton.DataContext = model;
        card.Art.Category = model.Art;
        var key = model.Tone switch
        {
            "Available" => "NpbReadyBrush", "Printing" or "Busy" => "NpbIndigoBrush",
            "Attention" => "NpbWarnBrush", "Paused" => "NpbPausedBrush", _ => "NpbMutedBrush",
        };
        card.StatusLabel.Foreground = (Brush)Application.Current.Resources[key];
        card.OpenButton.Opacity = model.Tone == "Offline" ? 0.65 : 1;
        AutomationProperties.SetName(card.OpenButton, model.QueueName + ", " + model.KindLabel + ", " + model.Status);
    }
    private void OnOpen(object sender, RoutedEventArgs args)
    {
        if (Model is { } model) AppServices.Window.OpenPrinter(model.QueueName);
    }
}
