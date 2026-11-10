using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Neobash.PrintBridge.Core.Dispatch;

namespace Neobash.PrintBridge.Desktop;

public sealed partial class SettingsPage : Page, ISessionPage
{
    public SettingsPage() => InitializeComponent();

    public void Activate()
    {
        var preferences = AppServices.Window.Preferences.Load();
        ThemeText.Text = preferences.Appearance == AppearancePreference.Graphite
            ? "La preferencia guardada es grafito. Hasta que exista ese diccionario, la ventana permanece en claro."
            : "Preferencia guardada: claro.";
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = "Neobash Print Bridge Desktop " + version;
        EnvironmentText.Text = $"{RuntimeInformation.OSDescription} · {RuntimeInformation.OSArchitecture} · {RuntimeInformation.FrameworkDescription}";
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Neobash", "PrintBridge", "Desktop");
        LogText.Text = root;
        var catalogWarnings = AppServices.Window.Session.CatalogWarnings();
        Warnings.Text = catalogWarnings.Count == 0
            ? "Sin advertencias nuevas del catálogo en esta vista."
            : string.Join(Environment.NewLine, catalogWarnings);
        Queues.Text = string.Join(Environment.NewLine, AppServices.Window.Session.Installed.Select(queue =>
            $"{queue.QueueName} · {(string.IsNullOrWhiteSpace(queue.DriverName) ? "controlador no leído" : queue.DriverName)} · {DevicePresentation.Status(queue, DevicePresentation.IsKnownVirtual(queue) ? DeviceKind.Virtual : DeviceKind.Physical, true)}"));
        if (string.IsNullOrWhiteSpace(Queues.Text))
        {
            Queues.Text = "Todavía no hay colas en la instantánea.";
        }
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Neobash", "PrintBridge", "Desktop");
        Directory.CreateDirectory(root);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = root,
            UseShellExecute = true,
        });
    }
}
