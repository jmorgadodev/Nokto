using NAudio.CoreAudioApi;
using Nokto.Platform.Windows.Audio;

internal static class AudioInteropProbe
{
    public static void Run()
    {
        // Solo lectura: jamás invoca Set, Toggle, captura ni reproducción.
        using var observerEnumerator = new MMDeviceEnumerator();
        string? expectedOutput = DefaultId(observerEnumerator, DataFlow.Render);
        string? expectedInput = DefaultId(observerEnumerator, DataFlow.Capture);
        Console.WriteLine("Enumerador del observador NAudio activo.");
        for (int i = 0; i < 8; i++) ReadPanel(expectedOutput, expectedInput);
        _ = observerEnumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        Task.Run(() =>
        {
            using var backgroundEnumerator = new MMDeviceEnumerator();
            for (int i = 0; i < 8; i++) ReadPanel(expectedOutput, expectedInput);
            _ = backgroundEnumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }).GetAwaiter().GetResult();
        using var controller = new WasapiAudioController();
        _ = controller.GetDefaultAudioProfile();
        Console.WriteLine("[PASS] Observador y panel de audio coexisten en STA/MTA; lecturas sin cambiar el audio.");
        Exception? failure = null;
        foreach (var apartment in new[] { ApartmentState.STA, ApartmentState.MTA })
        {
        var reverse = new Thread(() =>
        {
            try
            {
                ReadPanel(expectedOutput, expectedInput); // Crea primero el RCW desde el módulo anterior.
                using var afterPanel = new MMDeviceEnumerator();
                ReadPanel(expectedOutput, expectedInput); _ = afterPanel.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }
            catch (Exception ex) { failure = ex; }
        });
        reverse.SetApartmentState(apartment); reverse.Start(); reverse.Join();
        if (failure is not null) throw new Exception("Fallo al iniciar el panel antes del observador", failure);
        }
        Console.WriteLine("[PASS] Ambos órdenes de inicio conservan utilizable el enumerador NAudio tras las lecturas.");
    }
    private static string? DefaultId(MMDeviceEnumerator enumerator, DataFlow direction)
    {
        if (!enumerator.HasDefaultAudioEndpoint(direction, Role.Multimedia)) return null;
        using var device = enumerator.GetDefaultAudioEndpoint(direction, Role.Multimedia); return device.ID;
    }
    private static void ReadPanel(string? expectedOutput = null, string? expectedInput = null)
    {
        var profile = AudioDeviceProfileService.Capture();
        var output = AudioDeviceProfileService.Enumerate(false);
        var input = AudioDeviceProfileService.Enumerate(true);
        _ = AudioDeviceProfileService.GetMute(false);
        _ = AudioDeviceProfileService.GetMute(true);
        _ = AudioDeviceProfileService.GetMasterVolume();
        if (expectedOutput is not null && profile.OutputId != expectedOutput) throw new Exception("El perfil perdió la salida nativa conocida");
        if (expectedInput is not null && profile.InputId != expectedInput) throw new Exception("El perfil perdió la entrada nativa conocida");
        if (profile.OutputId is not null && !output.Any(d => d.Id == profile.OutputId)) throw new Exception("Salida predeterminada ausente de la enumeración");
        if (profile.InputId is not null && !input.Any(d => d.Id == profile.InputId)) throw new Exception("Entrada predeterminada ausente de la enumeración");
    }
}
