// Test-only controls for exercising the production MainWindow's async coordination.
// These tests do not validate WinUI rendering, native controls, or a printer driver.
using System.Collections.Concurrent;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Dispatching
{
    public enum DispatcherQueuePriority { Low }
    public sealed class DispatcherQueue
    {
        public ConcurrentQueue<Action> Pending { get; } = new();
        public bool TryEnqueue(Action callback) { Pending.Enqueue(callback); return true; }
        public void Drain() { while (Pending.TryDequeue(out var callback)) callback(); }
    }
}

namespace Microsoft.UI.Xaml
{
    public delegate void TestEventHandler<T>(object sender, T args);
    public class RoutedEventArgs : EventArgs { }
    public sealed class WindowEventArgs : EventArgs { }
    public sealed class WindowActivatedEventArgs : EventArgs { }
    public enum TextWrapping { Wrap }
    public class Window
    {
        public event TestEventHandler<WindowEventArgs>? Closed { add { } remove { } }
        public event TestEventHandler<WindowActivatedEventArgs>? Activated { add { } remove { } }
        public Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; } = new();
        public Control Content { get; } = new();
    }
    public sealed class Application
    {
        public static Application Current { get; } = new();
        public void Exit() { }
    }
}

namespace Microsoft.UI.Xaml.Controls
{
    public class Control { public bool IsEnabled { get; set; } public object XamlRoot { get; } = new(); }
    public sealed class SelectionChangedEventArgs : EventArgs { }
    public class ComboBox : Control
    {
        private object? _selected;
        public List<object> Items { get; } = [];
        public int SelectedIndex { get; set; } = -1;
        public object? SelectedItem
        {
            get => _selected;
            set { _selected = value; SelectionChanged?.Invoke(this, new()); }
        }
        public event Microsoft.UI.Xaml.TestEventHandler<SelectionChangedEventArgs>? SelectionChanged;
    }
    public sealed class ListView : ComboBox { }
    public sealed class Button : Control { }
    public class TextBox : Control { public string Text { get; set; } = ""; }
    public sealed class TextBlock : TextBox { public Microsoft.UI.Xaml.TextWrapping TextWrapping { get; set; } }
    public sealed class CheckBox : Control { public bool? IsChecked { get; set; } }
    public enum ContentDialogResult { None, Primary }
    public enum ContentDialogButton { Close }
    public sealed class ContentDialog : Control
    {
        public string Title { get; set; } = "";
        public object? Content { get; set; }
        public string PrimaryButtonText { get; set; } = "";
        public string CloseButtonText { get; set; } = "";
        public ContentDialogButton DefaultButton { get; set; }
        public new object? XamlRoot { get; set; }
        public Task<ContentDialogResult> ShowAsync() => Task.FromResult(ContentDialogResult.None);
    }
}

namespace Windows.Storage.Pickers
{
    public sealed class TestFile { public string Path { get; set; } = ""; }
    public sealed class FileOpenPicker
    {
        public List<string> FileTypeFilter { get; } = [];
        public Task<TestFile?> PickSingleFileAsync() => Task.FromResult<TestFile?>(null);
    }
}
namespace WinRT.Interop
{
    public static class InitializeWithWindow { public static void Initialize(object picker, nint handle) { } }
    public static class WindowNative { public static nint GetWindowHandle(object window) => 0; }
}

namespace Neobash.PrintBridge.Desktop
{
    public sealed partial class MainWindow
    {
        internal readonly ComboBox QueueBox = new(), PaperBox = new(), OrientationBox = new(), ScaleBox = new();
        internal readonly Button SaveButton = new(), PrintButton = new(), ConfirmButton = new();
        internal readonly TextBlock DestinationStatus = new(), PrintStatus = new(), PdfPath = new();
        internal readonly TextBox CopiesBox = new(), FirstPageBox = new(), LastPageBox = new(), PercentBox = new(), ConfirmationNote = new();
        internal readonly CheckBox AllPagesBox = new();
        internal readonly ListView HistoryList = new();
        private void InitializeComponent() { }
        internal Task TestLoadNames() => LoadNamesAsync();
        internal Task TestLoadDetails(string name) => LoadDetailsAsync(name);
        internal Task TestRecover() => RecoverAsync();
        internal Task TestHistory() => LoadHistoryAsync(null);
        internal void TestSnapshotReady() { _snapshotReady = true; UpdateCommandStates(); }
    }
}
