using Avalonia;

namespace Nokto.UI;

internal static class Program
{
    private const string AppEventName = "Nokto_Desktop_ShowMainWindow_Event";
    private static EventWaitHandle? _showEventHandle;

    [STAThread]
    public static int Main(string[] args)
    {
        bool isFirstInstance;
        try
        {
            _showEventHandle = new EventWaitHandle(false, EventResetMode.AutoReset, AppEventName, out isFirstInstance);
        }
        catch
        {
            isFirstInstance = true;
        }

        if (!isFirstInstance)
        {
            // Otra instancia ya está en ejecución (posiblemente en la bandeja del sistema).
            // Le indicamos que muestre su ventana principal y salimos limpiamente sin conflictos.
            try
            {
                if (EventWaitHandle.TryOpenExisting(AppEventName, out var existingEvent))
                {
                    existingEvent.Set();
                    existingEvent.Dispose();
                }
            }
            catch { }

            return 0;
        }

        try
        {
            // Tarea en segundo plano para escuchar peticiones de mostrar ventana desde nuevas instancias
            _ = Task.Run(() =>
            {
                while (_showEventHandle != null)
                {
                    try
                    {
                        _showEventHandle.WaitOne();
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            App.CurrentInstance?.ShowMainWindow();
                        });
                    }
                    catch
                    {
                        break;
                    }
                }
            });

            return BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            try
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "crash.log"), ex.ToString());
            }
            catch { }
            return 1;
        }
        finally
        {
            _showEventHandle?.Dispose();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
