using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Nokto.Core.Models;
using Nokto.Core.Serialization;
using Nokto.Platform.Windows.Audio;
using Nokto.Platform.Windows.Hotkeys;

namespace Nokto.ConsoleTest;

internal static class AudioControlRegressionTests
{
    public static void Run()
    {
        Require(HotkeyChord.TryParse("Ctrl + Shift + M", out var mic) && mic == new HotkeyChord(6, 0x4D), "Atajo de micrófono inválido.");
        Require(HotkeyChord.TryParse("Ctrl+Shift+O", out var output) && output == new HotkeyChord(6, 0x4F), "Atajo de salida inválido.");
        Require(HotkeyChord.TryParse("F8", out var function) && function == new HotkeyChord(0, 0x77), "F8 debe ser configurable.");
        Require(HotkeyChord.TryParse("Pausa", out var pause) && pause == new HotkeyChord(0, 0x13), "Pausa debe poder interpretarse.");
        foreach (string invalid in new[] { "", "Ctrl++M", "Ctrl+Ctrl+M", "Shift+F12", "F25", "Alt+Unknown", "Ctrl+Shift", "Ctrl+M+O" })
            Require(!HotkeyChord.TryParse(invalid, out _), $"Debe rechazar '{invalid}'.");
        var old = JsonSerializer.Deserialize("{}", NoktoJsonContext.Default.AppSettings)!;
        Require(old.MicMuteHotkey == "Ctrl+Shift+M" && old.AudioMuteHotkey == "Ctrl+Shift+S", "Los archivos anteriores deben recibir los atajos predeterminados.");
        old.MicMuteHotkey = "F8";
        old.AudioMuteHotkey = "Alt+O";
        var restored = JsonSerializer.Deserialize(JsonSerializer.Serialize(old, NoktoJsonContext.Default.AppSettings), NoktoJsonContext.Default.AppSettings)!;
        Require(restored.MicMuteHotkey == "F8" && restored.AudioMuteHotkey == "Alt+O", "Los atajos personalizados deben persistir con AOT.");
        Console.WriteLine("[PASS] Atajos: sintaxis, teclas configurables, duplicados y configuración AOT compatible.");

        VerifyMessageLoop();
        VerifyInputIsolationPolicy();
        VerifyAudioControls();
    }

    private static void VerifyMessageLoop(Func<(uint Sent, int Error)>? injectInput = null)
    {
        using var pressed = new ManualResetEventSlim();
        int presses = 0;
        using var first = new GlobalHotkeyService(() => { Interlocked.Increment(ref presses); pressed.Set(); });
        using var second = new GlobalHotkeyService(() => { });
        var timer = Stopwatch.StartNew();
        Require(first.Start(7, 0x79) && first.IsRegistered, "Ctrl+Alt+Shift+F10 debe registrarse sin esperar un mensaje externo.");
        Require(timer.ElapsedMilliseconds < 1500, "El registro no debe agotar el timeout por una cola bloqueada.");
        Require(!second.Start(7, 0x79) && !second.IsRegistered && second.RegistrationError != 0, "Un conflicto real debe reportarse.");
        var inputs = new ushort[] { 0x11, 0x12, 0x10, 0x79 }.Select(vk => Key(vk, false))
            .Concat(new ushort[] { 0x79, 0x10, 0x12, 0x11 }.Select(vk => Key(vk, true))).ToArray();
        uint sent;
        int inputError;
        if (injectInput is null)
        {
            sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
            inputError = Marshal.GetLastWin32Error();
        }
        else { (sent, inputError) = injectInput(); }
        bool blockedByUipi = sent == 0 && inputError == 5;
        if (blockedByUipi)
            Console.WriteLine("[WARN] SendInput omitido por políticas UIPI de sesión de Windows");
        else
        {
            Require(sent == inputs.Length, "No se pudo enviar la combinación de prueba.");
            Require(pressed.Wait(2000) && Volatile.Read(ref presses) == 1, "Windows debe entregar un único callback al pulsar el atajo registrado.");
        }
        first.Stop();
        Require(second.Start(7, 0x79), "Stop debe liberar la combinación para el siguiente registro.");
        second.Stop();
        Require(first.Start(7, 0x78), "El servicio debe poder cambiar de combinación tras detenerse.");
        Console.WriteLine(blockedByUipi
            ? "[PASS] Hotkey Win32: registro, conflicto, liberación y cambio de combinación; pulsación sintética omitida."
            : "[PASS] Hotkey Win32: inicio sin bloqueo, conflicto, pulsación real, liberación y cambio de combinación.");
    }

    private static void VerifyInputIsolationPolicy()
    {
        var originalOutput = Console.Out;
        using var captured = new StringWriter();
        try
        {
            Console.SetOut(captured);
            VerifyMessageLoop(() => (0, 5));
        }
        finally { Console.SetOut(originalOutput); }
        Require(captured.ToString().Contains("[WARN] SendInput omitido por políticas UIPI de sesión de Windows"),
            "La restricción UIPI debe advertirse sin impedir el resto de comprobaciones de hotkeys.");
        foreach (var failure in new[] { (Sent: 0u, Error: 87), (Sent: 4u, Error: 5) })
        {
            bool rejected = false;
            try { VerifyMessageLoop(() => failure); }
            catch (InvalidOperationException ex) when (ex.Message == "No se pudo enviar la combinación de prueba.") { rejected = true; }
            Require(rejected, "Un error distinto de UIPI o una inyección parcial debe seguir fallando.");
        }
        Console.WriteLine("[PASS] Política SendInput: UIPI simulado no aborta; otros errores e inyecciones parciales fallan.");
    }

    private static void VerifyAudioControls()
    {
        using var controller = new WasapiAudioController();
        var profile = AudioDeviceProfileService.Capture();
        var outputs = controller.GetOutputAudioDevices();
        var inputs = controller.GetInputAudioDevices();
        Require(outputs.Select(d => d.Id).Distinct().Count() == outputs.Count && inputs.Select(d => d.Id).Distinct().Count() == inputs.Count,
            "La enumeración debe conservar IDs únicos aunque se repitan nombres.");
        if (profile.OutputId != null) Require(outputs.Any(d => d.Id == profile.OutputId && d.IsDefault), "La salida predeterminada debe estar en el selector.");
        if (profile.InputId != null) Require(inputs.Any(d => d.Id == profile.InputId && d.IsDefault), "La entrada predeterminada debe estar en el selector.");
        Require(!controller.SetDefaultAudioDevice("nonexistent-device-id", false), "Un endpoint ajeno no debe modificar Windows.");
        bool? originalOutput = controller.GetOutputMute();
        bool? originalInput = controller.GetInputMute();
        try
        {
            if (originalOutput.HasValue)
            {
                Require(controller.ToggleOutputMute() && controller.GetOutputMute() == !originalOutput.Value, "La salida debe silenciarse a nivel del sistema.");
                Require(AudioDeviceProfileService.Capture().OutputMuted == !originalOutput.Value, "Inicio debe leer el silencio actualizado.");
                Require(controller.ToggleOutputMute() && controller.GetOutputMute() == originalOutput, "El segundo toggle debe restaurar la salida.");
            }
            if (originalInput.HasValue)
            {
                Require(controller.ToggleInputMute() && controller.GetInputMute() == !originalInput.Value, "El micrófono debe silenciarse a nivel del sistema.");
                Require(AudioDeviceProfileService.Capture().InputMuted == !originalInput.Value, "Inicio debe leer el silencio del micrófono actualizado.");
                Require(controller.ToggleInputMute() && controller.GetInputMute() == originalInput, "El segundo toggle debe restaurar el micrófono.");
            }
        }
        finally
        {
            if (originalOutput.HasValue) AudioDeviceProfileService.SetMute(false, originalOutput.Value);
            if (originalInput.HasValue) AudioDeviceProfileService.SetMute(true, originalInput.Value);
        }
        Require(controller.GetOutputMute() == originalOutput && controller.GetInputMute() == originalInput, "La verificación debe conservar el estado de audio del usuario.");
        Console.WriteLine($"[PASS] Audio nativo: {inputs.Count} entrada(s), {outputs.Count} salida(s), toggles reales y estados conservados.");
    }

    private static Input Key(ushort vk, bool up) => new() { Type = 1, Data = new InputData { Keyboard = new Keyboard { Key = vk, Flags = up ? 2u : 0u } } };
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputData Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputData
    {
        [FieldOffset(0)] public Keyboard Keyboard;
        [FieldOffset(0)] public Mouse Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Keyboard { public ushort Key, Scan; public uint Flags, Time; public IntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Mouse { public int X, Y; public uint Data, Flags, Time; public IntPtr Extra; }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
