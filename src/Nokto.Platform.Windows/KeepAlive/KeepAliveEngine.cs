using System.Runtime.InteropServices;
using Nokto.Core.Models;
using Nokto.Platform.Windows.Interop;

namespace Nokto.Platform.Windows.KeepAlive;

public sealed class KeepAliveEngine
{
    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    /// <summary>
    /// Emite una pulsación atómica de VK_F15 sin dejar rastro de caracteres en pantalla.
    /// </summary>
    public static void SendVirtualKeyF15()
    {
        var inputs = new INPUT[2];

        // Key Down
        inputs[0] = new INPUT
        {
            type = NativeConstants.INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = NativeConstants.VK_F15,
                    wScan = 0,
                    dwFlags = 0,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        // Key Up
        inputs[1] = new INPUT
        {
            type = NativeConstants.INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = NativeConstants.VK_F15,
                    wScan = 0,
                    dwFlags = NativeConstants.KEYEVENTF_KEYUP,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        NativeMethods.SendInput((uint)inputs.Length, inputs, InputSize);
    }

    /// <summary>
    /// Emite un micro-movimiento relativo de ratón (+1px y -1px) con desplazamiento neto cero.
    /// </summary>
    public static void SendMouseJitter(int deltaPixels = 1)
    {
        int delta = Math.Max(1, deltaPixels);
        var inputs = new INPUT[2];

        // Mover +delta px
        inputs[0] = new INPUT
        {
            type = NativeConstants.INPUT_MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = delta,
                    dy = 0,
                    mouseData = 0,
                    dwFlags = NativeConstants.MOUSEEVENTF_MOVE,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        // Regresar -delta px
        inputs[1] = new INPUT
        {
            type = NativeConstants.INPUT_MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = -delta,
                    dy = 0,
                    mouseData = 0,
                    dwFlags = NativeConstants.MOUSEEVENTF_MOVE,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        NativeMethods.SendInput((uint)inputs.Length, inputs, InputSize);
    }

    /// <summary>
    /// Emite un pulso según el modo configurado.
    /// </summary>
    public static void EmitPulse(KeepAliveMode mode)
    {
        switch (mode)
        {
            case KeepAliveMode.InputSimulation:
                SendVirtualKeyF15();
                break;
            case KeepAliveMode.ThreadExecutionState:
                NativeMethods.SetThreadExecutionState(
                    NativeConstants.EXECUTION_STATE.ES_SYSTEM_REQUIRED |
                    NativeConstants.EXECUTION_STATE.ES_DISPLAY_REQUIRED);
                break;
            case KeepAliveMode.Mixed:
                NativeMethods.SetThreadExecutionState(
                    NativeConstants.EXECUTION_STATE.ES_SYSTEM_REQUIRED |
                    NativeConstants.EXECUTION_STATE.ES_DISPLAY_REQUIRED);
                SendVirtualKeyF15();
                SendMouseJitter();
                break;
            case KeepAliveMode.None:
            default:
                break;
        }
    }

    /// <summary>
    /// Ejecuta el bucle de mantenimiento de actividad con jitter pseudoaleatorio y PeriodicTimer.
    /// </summary>
    public async Task RunLoopAsync(
        KeepAliveMode mode,
        int jitterMinSeconds,
        int jitterMaxSeconds,
        Action<string>? onActivityLogged,
        CancellationToken cancellationToken)
    {
        int minSec = Math.Max(1, jitterMinSeconds);
        int maxSec = Math.Max(minSec, jitterMaxSeconds);

        // Bloquea suspensión mediante SetThreadExecutionState continuo
        NativeMethods.SetThreadExecutionState(
            NativeConstants.EXECUTION_STATE.ES_CONTINUOUS |
            NativeConstants.EXECUTION_STATE.ES_SYSTEM_REQUIRED |
            NativeConstants.EXECUTION_STATE.ES_DISPLAY_REQUIRED);

        onActivityLogged?.Invoke($"[KeepAlive] Iniciado modo {mode}. Rango jitter: {minSec}s - {maxSec}s.");

        try
        {
            // Pulso inicial
            EmitPulse(mode);
            int nextDelay = Random.Shared.Next(minSec, maxSec + 1);
            onActivityLogged?.Invoke($"[KeepAlive] Pulso inicial emitido. Próximo jitter en {nextDelay}s.");

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(nextDelay));

            while (!cancellationToken.IsCancellationRequested)
            {
                await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);

                EmitPulse(mode);

                nextDelay = Random.Shared.Next(minSec, maxSec + 1);
                timer.Period = TimeSpan.FromSeconds(nextDelay);

                onActivityLogged?.Invoke($"[KeepAlive] Señal emitida exitosamente ({mode}). Próximo jitter en {nextDelay}s.");
            }
        }
        catch (OperationCanceledException)
        {
            onActivityLogged?.Invoke("[KeepAlive] Bucle cancelado por el usuario.");
        }
        finally
        {
            // Restablece estado del hilo a normal (permite suspensión normal)
            NativeMethods.SetThreadExecutionState(NativeConstants.EXECUTION_STATE.ES_CONTINUOUS);
            onActivityLogged?.Invoke("[KeepAlive] Estado de energía de Windows restablecido a ES_CONTINUOUS.");
        }
    }
}
