using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Avalonia;

namespace Nokto.UI;

internal static partial class Program
{
    private const string PipeName = "Nokto_Desktop_IPC_Pipe";

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [STAThread]
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            LogCrash(e.ExceptionObject?.ToString() ?? "Unhandled exception");
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            LogCrash(e.Exception?.ToString() ?? "Unobserved task exception");
            e.SetObserved();
        };

        try
        {
            var current = Process.GetCurrentProcess();
            var existing = Process.GetProcessesByName(current.ProcessName)
                .FirstOrDefault(p => p.Id != current.Id);

            if (existing != null)
            {
                // Otra instancia ya está en ejecución.
                // Intentamos notificarle mediante canal IPC de Named Pipe para que restaure su ventana.
                bool notified = TryNotifyExistingInstance();

                if (!notified && existing.MainWindowHandle != IntPtr.Zero)
                {
                    try
                    {
                        ShowWindow(existing.MainWindowHandle, 9); // SW_RESTORE
                        SetForegroundWindow(existing.MainWindowHandle);
                    }
                    catch { }
                }

                return 0;
            }

            using var cts = new CancellationTokenSource();
            StartIpcServer(cts.Token);

            int exitCode = BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);

            cts.Cancel();
            return exitCode;
        }
        catch (Exception ex)
        {
            LogCrash(ex.ToString());
            return 1;
        }
    }

    private static bool TryNotifyExistingInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(500); // 500 ms de espera
            client.WriteByte(1); // Señal de activación
            client.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void StartIpcServer(CancellationToken token)
    {
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token);

                    int signal = server.ReadByte();
                    if (signal == 1)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            App.CurrentInstance?.ShowMainWindow();
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    try
                    {
                        await Task.Delay(250, token);
                    }
                    catch
                    {
                        break;
                    }
                }
            }
        }, token);
    }

    private static void LogCrash(string content)
    {
        try
        {
            string crashPath = Path.Combine(AppContext.BaseDirectory, "crash.log");
            File.AppendAllText(crashPath, $"[{DateTime.UtcNow:O}] {content}{Environment.NewLine}");
        }
        catch { }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
