using System.Runtime.InteropServices;
using Neobash.PrintBridge.Core.Dispatch;

namespace Neobash.PrintBridge.Desktop;

// One background poll at a time. Never force live remote RPC or alter a queue.
public sealed class WindowsQueueObserver(Func<string, QueueObservation>? read = null, Func<DateTimeOffset>? now = null)
{
    private readonly Func<string, QueueObservation> _read = read ?? Read;
    private readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.UtcNow);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, QueueObservation> _cache = new(StringComparer.OrdinalIgnoreCase);
    public async Task<IReadOnlyDictionary<string, QueueObservation>?> RefreshAsync(IReadOnlyList<string> names)
    {
        if (!await _gate.WaitAsync(0)) return null;
        try
        {
            return await Task.Run(() =>
            {
                foreach (var removed in _cache.Keys.Except(names, StringComparer.OrdinalIgnoreCase).ToArray()) _cache.Remove(removed);
                foreach (var name in names)
                {
                    if (_cache.TryGetValue(name, out var cached) && _now() - cached.ObservedAt < TimeSpan.FromSeconds(15)) continue;
                    try { _cache[name] = _read(name); }
                    catch (Exception exception) { _cache[name] = new(0, null, _now(), Error: exception.Message); }
                }
                return (IReadOnlyDictionary<string, QueueObservation>)new Dictionary<string, QueueObservation>(_cache, StringComparer.OrdinalIgnoreCase);
            });
        }
        finally { _gate.Release(); }
    }

    public static QueueObservation Read(string name)
    {
        if (!OpenPrinter(name, out var handle, IntPtr.Zero))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            GetPrinter(handle, 2, IntPtr.Zero, 0, out var needed);
            if (needed == 0 || needed > 16 * 1024 * 1024) throw new InvalidOperationException("Datos de cola no disponibles.");
            var buffer = Marshal.AllocHGlobal(checked((int)needed));
            try
            {
                if (!GetPrinter(handle, 2, buffer, needed, out _)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                var info = Marshal.PtrToStructure<PrinterInfo2>(buffer);
                return new(info.Status, info.Jobs, DateTimeOffset.UtcNow, Marshal.PtrToStringUni(info.Driver), Marshal.PtrToStringUni(info.Port));
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally { ClosePrinter(handle); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PrinterInfo2
    {
        public IntPtr Server, Printer, Share, Port, Driver, Comment, Location, DevMode, Separator, Processor, DataType, Parameters, Security;
        public uint Attributes, Priority, DefaultPriority, StartTime, UntilTime, Status, Jobs, AveragePpm;
    }
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string name, out IntPtr handle, IntPtr defaults);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetPrinter(IntPtr handle, uint level, IntPtr data, uint size, out uint needed);
    [DllImport("winspool.drv")] private static extern bool ClosePrinter(IntPtr handle);
}
