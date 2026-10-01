# Bitácora de Desarrollo — Nokto

Este documento registra de manera cronológica y detallada cada avance, fase del roadmap, archivos modificados y resultados de compilación del proyecto Nokto.

---

## [Fase 1: Core Headless & Windows System Adapter] - 2026-10-01 18:25:00
- **Fase del roadmap:** Fase 1 (Motor Headless y Adaptadores de Sistema Operativo)
- **Archivos creados o modificados:**
  - `Nokto.sln`
  - `src/Nokto.Core/Nokto.Core.csproj`
  - `src/Nokto.Core/Abstractions/ISystemAdapter.cs`
  - `src/Nokto.Core/Models/Enums.cs`
  - `src/Nokto.Core/Models/SystemMetrics.cs`
  - `src/Nokto.Core/Models/Config.cs`
  - `src/Nokto.Core/Models/Presets.cs`
  - `src/Nokto.Core/Models/SystemStatusState.cs`
  - `src/Nokto.Core/Models/AuditLogEntry.cs`
  - `src/Nokto.Core/Models/ApiRequests.cs`
  - `src/Nokto.Core/Serialization/NoktoJsonContext.cs`
  - `src/Nokto.Platform.Windows/Nokto.Platform.Windows.csproj`
  - `src/Nokto.Platform.Windows/Interop/NativeConstants.cs`
  - `src/Nokto.Platform.Windows/Interop/NativeStructs.cs`
  - `src/Nokto.Platform.Windows/Interop/NativeMethods.cs`
  - `src/Nokto.Platform.Windows/Audio/WasapiAudio.cs`
  - `src/Nokto.Platform.Windows/KeepAlive/KeepAliveEngine.cs`
  - `src/Nokto.Platform.Windows/Metrics/PassiveMetricsCollector.cs`
  - `src/Nokto.Platform.Windows/WindowsSystemAdapter.cs`
  - `src/Nokto.Platform.MacOs/Nokto.Platform.MacOs.csproj`
  - `src/Nokto.Platform.MacOs/MacOsSystemAdapter.cs`
  - `src/Nokto.LanServer/Nokto.LanServer.csproj`
  - `src/Nokto.LanServer/LanServerHost.cs`
  - `tests/Nokto.ConsoleTest/Nokto.ConsoleTest.csproj`
  - `tests/Nokto.ConsoleTest/Program.cs`
  - `CHANGELOG.md`
- **Resumen técnico del cambio:**
  1. **Configuración de la Solución y Arquitectura Modular:**
     - Creación de solución `Nokto.sln` con referencias cruzadas desacopladas.
     - Proyectos modulares: `Nokto.Core` (.NET 8 LTS), `Nokto.Platform.Windows` (`net8.0-windows10.0.19041.0`), `Nokto.Platform.MacOs` (.NET 8), `Nokto.LanServer` (.NET 8) y `tests/Nokto.ConsoleTest`.
     - Habilitación estricta de `Nullable` y `TreatWarningsAsErrors=true`.
  2. **Contratos y Serialización AOT (Nokto.Core):**
     - Definición de la interfaz desacoplada `ISystemAdapter`.
     - Modelado de contratos de datos (`AppConfig`, `PresetsFile`, `SystemMetrics`, `SystemStatusState`, `AuditLogEntry`, etc.).
     - Implementación de `NoktoJsonContext : JsonSerializerContext` para serialización JSON rápida en tiempo de compilación sin reflexión, compatible con Native AOT.
  3. **Adaptador Nativo de Windows (Nokto.Platform.Windows):**
     - Invocaciones Win32 directas (P/Invoke) para control de energía con elevación de privilegios `SE_SHUTDOWN_NAME` (`ExitWindowsEx`, `InitiateSystemShutdownEx`, `SetSuspendState`, `LockWorkStation`).
     - Apagado y encendido de monitores mediante señales de hardware `WM_SYSCOMMAND` (`SC_MONITORPOWER`).
     - Controlador de audio maestro WASAPI nativo (`IAudioEndpointVolume`, COM Interop) con atenuación de volumen perceptual suave basada en `PeriodicTimer` sin dependencias pesadas.
     - Motor de Modo Trabajo (Keep-Alive): bucle no bloqueante con `PeriodicTimer`, jitter pseudoaleatorio (45s - 105s), emisión de `VK_F15` y micro-movimiento de ratón $\pm 1\text{px}$ vía `SendInput`, integrado con `SetThreadExecutionState`.
     - Lector pasivo de métricas del sistema: CPU instantáneo mediante `GetSystemTimes` (<0.01% de uso de CPU, cero allocations), RAM física vía `GlobalMemoryStatusEx`, inactividad por `GetLastInputInfo` y tráfico de red por `NetworkInterface`.
  4. **Consola Interactiva y Verificación Automatizada (Nokto.ConsoleTest):**
     - Menú interactivo para probar individualmente: apagado de monitores, bucle de Keep-Alive con registro de jitter, atenuación WASAPI con barra de progreso, telemetría en vivo y serialización AOT.
     - Soporte para verificación desatendida / CI (`--verify`) que ejecuta la batería de pruebas y valida el estado nativo con 0 errores.
- **Resultado de la compilación:**
  - `dotnet build Nokto.sln`: 0 Advertencias, 0 Errores.
  - Ejecución de prueba con `dotnet run --project tests/Nokto.ConsoleTest -- --verify`: Exitoso con código de salida 0.

---

## [Fase 0: Inicialización y Estructura Base] - 2026-10-01 18:15:00
- **Fase del roadmap:** Fase 0 (Preparación del Entorno)
- **Archivos creados o modificados:**
  - `docs/01_PRD_CORE.md` (reubicado)
  - `docs/02_UI_UX_SPEC.md` (reubicado)
  - `docs/03_DATA_CONTRACTS.md` (reubicado)
  - `docs/04_DEV_ROADMAP.md` (reubicado)
  - `docs/05_AGENT_KICKOFF.md` (reubicado)
  - `.gitignore` (creado)
  - `CHANGELOG.md` (creado)
- **Resumen técnico del cambio:**
  - Reorganización de la documentación en la carpeta canónica `docs/`.
  - Inicialización del repositorio Git local.
  - Creación del archivo `.gitignore` optimizado para .NET 8, Avalonia y archivos temporales de ejecución.
- **Resultado de la compilación:** N/A (Fase documental).
