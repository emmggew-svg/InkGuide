using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace InkGuide.App.Hosting;

/// <summary>
/// Single-instance enforcement: a named mutex plus a named pipe that forwards
/// launch arguments (file paths) to the first instance, which raises them as
/// an app.openPaths event over the bridge.
/// </summary>
internal sealed partial class SingleInstance : IDisposable
{
    private readonly ILogger<SingleInstance> _logger;
    private Mutex? _mutex;
    private CancellationTokenSource? _pipeCts;
    private Task? _pipeTask;
    private bool _disposed;

    public event Action<string[]>? ArgumentsReceived;

    public SingleInstance(ILogger<SingleInstance> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Returns true when this process is the first instance and owns the mutex.
    /// </summary>
    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, AppIdentity.SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;
            return false;
        }

        return true;
    }

    public void StartListener(CancellationToken appStopping)
    {
        _pipeCts = CancellationTokenSource.CreateLinkedTokenSource(appStopping);
        CancellationToken token = _pipeCts.Token;
        _pipeTask = Task.Run(() => ListenLoopAsync(token), token);
    }

    public static async Task ForwardToFirstInstanceAsync(string[] args, CancellationToken ct)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", AppIdentity.SingleInstancePipeName, PipeDirection.Out);
            await client.ConnectAsync(2000, ct).ConfigureAwait(false);
            string payload = string.Join("\n", args.Select(a => Convert.ToBase64String(Encoding.UTF8.GetBytes(a))));
            byte[] bytes = Encoding.UTF8.GetBytes(payload + "\n");
            await client.WriteAsync(bytes, ct).ConfigureAwait(false);
            await client.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException || ex is TimeoutException || ex is OperationCanceledException)
        {
            // First instance may already be gone; the new process simply exits.
        }
    }

    public static void ActivateFirstWindow()
    {
        IntPtr hwnd = NativeMethods.FindFirstWindow();
        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
            NativeMethods.SetForegroundWindow(hwnd);
        }
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    AppIdentity.SingleInstancePipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                using var reader = new StreamReader(server, Encoding.UTF8);
                string? payload = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
                string[] forwarded = (payload ?? string.Empty)
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => Encoding.UTF8.GetString(Convert.FromBase64String(line.Trim())))
                    .ToArray();
                AppLogMessages.ForwardedArgs(_logger, forwarded.Length);
                ArgumentsReceived?.Invoke(forwarded);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                AppLogMessages.PipeError(_logger, ex);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _pipeCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _pipeCts?.Dispose();
        _mutex?.Dispose();
    }

    private static partial class NativeMethods
    {
        public const int SW_RESTORE = 9;

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetForegroundWindow(IntPtr hWnd);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

        public static IntPtr FindFirstWindow()
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows(
                (hwnd, lParam) =>
                {
                    uint pid;
                    GetWindowThreadProcessId(hwnd, out pid);
                    if (pid == (uint)Environment.ProcessId && IsWindowVisible(hwnd))
                    {
                        found = hwnd;
                        return false;
                    }

                    return true;
                },
                IntPtr.Zero);
            return found;
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [LibraryImport("user32.dll", SetLastError = true)]
        private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool IsWindowVisible(IntPtr hWnd);
    }
}
