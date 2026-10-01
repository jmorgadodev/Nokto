# 01_PRD_CORE.md — Documento de Requerimientos de Producto y Especificación del Core

**Proyecto:** Nokto  
**Versión de especificación:** 1.0.0  
**Arquitectura base:** C# (.NET 8/9 LTS) + Avalonia UI  
**Tipo de distribución:** Binario único portable autocontenido (Single-File / Self-Contained) e instalador estándar (Inno Setup)  
**Entorno de ejecución:** Windows 10 (Build 19041+) y Windows 11 (x64 / ARM64), con arquitectura desacoplada para soporte futuro en macOS  

---

## 1. Filosofía y Objetivos Técnicos

Nokto es un gestor de estados de energía, automatización de tareas encadenadas y mantenimiento de actividad para entornos de escritorio.

### Principios rectores
1. **Zero-AI / 100% Determinista:** Toda decisión se rige por lógica booleana estricta, eventos del sistema operativo e instrucciones explícitas del usuario. Sin telemetría externa ni consumo de tokens.
2. **Cero dependencias externas:** No requiere la instalación previa del runtime de .NET, paquetes redistribuibles de C++ ni WebView2/Edge. El binario debe arrancar de forma inmediata en cualquier instalación limpia de Windows.
3. **Presupuesto estricto de recursos:**
   * **RAM en segundo plano (System Tray):** <= 25 MB (objetivo con Native AOT / Self-Contained).
   * **Consumo de CPU en reposo:** < 0.1%. Prohibidos los bucles de espera activa (busy-waiting); todo monitoreo debe basarse en temporizadores de baja frecuencia (`System.Threading.PeriodicTimer`) con intervalos de 1 a 3 segundos según el tipo de métrica.
4. **Portabilidad de huella cero (Zero-Trace):** Si el ejecutable detecta un archivo marcador `portable.lock` o un archivo `config.json` en su mismo directorio, opera en **Modo Portable**: lee y escribe configuraciones, registros y capturas en `./data/`, sin tocar el Registro de Windows ni `%APPDATA%`.

---

## 2. Arquitectura de Abstracción del Sistema Operativo

Para garantizar la extensibilidad futura a macOS sin reescribir la lógica de negocio, todo acceso a primitivas del sistema debe implementarse a través de interfaces desacopladas inyectadas mediante Dependency Injection compile-time:

    ┌─────────────────────────────────────────────────────────────────┐
    │                          Core Engine                            │
    │                      (Workflow & State)                         │
    └──────────────────────────────┬──────────────────────────────────┘
                                   │
    ┌──────────────────────────────▼──────────────────────────────────┐
    │                        ISystemAdapter                           │
    └──────────────────────────────┬──────────────────────────────────┘
                                   │
         ┌─────────────────────────┴─────────────────────────┐
         │                                                   │
    ┌────▼──────────────────────────┐           ┌────────────▼────────────────┐
    │     WindowsSystemAdapter      │           │      MacOsSystemAdapter     │
    │    (Win32 / WASAPI / P/I)     │           │       (IOKit / pmset)       │
    └───────────────────────────────┘           └─────────────────────────────┘

### Contrato `ISystemAdapter`
* `Task SetPowerState(PowerAction action, bool force)`: Ejecuta apagado, reinicio, suspensión, hibernación, bloqueo o cierre de sesión.
* `Task SetDisplayPower(bool turnOn)`: Apaga o enciende los monitores sin suspender la CPU.
* `Task SetMasterVolumeFade(float targetVolume, TimeSpan duration, CancellationToken ct)`: Modifica el volumen maestro progresivamente mediante una curva logarítmica.
* `Task MuteSystemAudio(bool mute)`: Silencia o reactiva el canal maestro de audio.
* `void SimulateKeepAliveInput(InputMode mode)`: Emite señales de actividad mínimas para evitar estados ausentes.
* `Task<byte[]> CaptureScreenAsync(bool stampMetadata, string label)`: Genera un mapa de bits del escritorio completo con estampas diagnósticas.
* `SystemMetrics GetCurrentMetrics()`: Obtiene instantáneas de CPU, GPU (si está disponible), tráfico de red y niveles de audio.

---

## 3. Especificación Exhaustiva de Disparadores (Triggers)

El motor evalúa disparadores de forma asíncrona. Un flujo se activa cuando se satisface una de las siguientes condiciones:

### 3.1. Temporales
* **`CountdownTrigger`:** Temporizador decreciente definido en horas, minutos y segundos. Proporciona eventos por segundo hacia la interfaz y el System Tray.
* **`FixedTimeTrigger`:** Dispara a una hora fija (`HH:mm:ss`). Si la hora configurada ya transcurrió en el día en curso, programa la activación para el día siguiente.
* **`ScheduleTrigger`:** Disparo en días específicos de la semana con ventana horaria (ej. Lunes a Viernes a las 18:30).

### 3.2. Procesos y Rendimiento del Sistema
* **`ProcessExitTrigger`:**
  * Supervisa la tabla de procesos del sistema (`System.Diagnostics.Process`).
  * Parámetros: `ProcessName` (ej. `blender.exe`), `WindowPattern` (opcional), `DebounceSeconds` (tiempo de confirmación continuo para evitar falsos positivos si el proceso reinicia subprocesos hijos, valor predeterminado: 5 segundos).
* **`SustainedLoadTrigger`:**
  * Supervisa uso global de CPU o GPU.
  * Parámetros: `MetricType` (CPU, GPU), `ThresholdPercentage` (ej. < 8%), `DurationSeconds` (ej. 90 segundos continuos).
  * Mecanismo: Ventana deslizante de muestras cada 2 segundos. La condición es verdadera únicamente si el 100% de las muestras dentro de la ventana se mantienen bajo el umbral.
* **`NetworkThroughputTrigger`:**
  * Parámetros: `Direction` (Download, Upload, Ambas), `ThresholdKBs` (ej. < 50 KB/s), `DurationSeconds` (ej. 120s).
  * Lectura mediante interfaces de red activas descartando adaptadores de loopback y virtuales desconectados.
* **`AudioSilenceTrigger`:**
  * Detección de ausencia de audio en el mixer de Windows (`IAudioSessionManager2`).
  * Parámetros: `SilenceThresholdSeconds`. Dispara cuando ningún flujo de audio supere el nivel de pico detectable (> 0.001) durante el tiempo fijado.
* **`FileWatcherTrigger`:**
  * Monitorea rutas locales mediante `FileSystemWatcher`.
  * Modos: `FileAppeared`, `FileDeleted`, o `FileWriteComplete` (verifica que el tamaño del archivo se estabilice y libere los bloqueos de escritura de otros procesos).
* **`BatteryStateTrigger`:**
  * Monitorea la desconexión de corriente alterna (`PowerLineStatus.Offline`) o porcentaje de batería por debajo del umbral crítico configurado.

### 3.3. Actividad de Usuario
* **`UserIdleTrigger`:**
  * Lee el tiempo transcurrido desde la última interacción de ratón o teclado mediante la llamada nativa `GetLastInputInfo`.
  * Dispara cuando `(Environment.TickCount - lastInputTick) >= IdleTimeoutMilliseconds`.

---

## 4. Especificación Exhaustiva de Acciones (Actions)

Las acciones se dividen en **Intermedias** (no detienen el sistema) y **Terminales** (concluyen la sesión de energía).

### 4.1. Acciones de Energía y Sesión (Terminales)
* **`ShutdownAction`:**
  * *Graceful:* Invoca `InitiateSystemShutdownEx` o `ExitWindowsEx(EWX_SHUTDOWN | EWX_POWEROFF, ...)` sin el flag de forzado inmediato.
  * *Forced:* Invoca la desconexión forzada de procesos que no responden (`EWX_FORCEIFHUNG` o `shutdown.exe /s /f /t 0`).
* **`SleepAction`:** Invoca `SetSuspendState(false, false, false)` de `Powrprof.dll` para entrar en estado S3 (Sleep en RAM).
* **`HibernateAction`:** Invoca `SetSuspendState(true, false, false)` persistiendo estado en `hiberfil.sys`.
* **`RestartAction`:** Reinicio limpio o forzado del sistema.
* **`LockStationAction`:** Invoca `LockWorkStation()` de `user32.dll`. No requiere elevación de privilegios UAC.
* **`LogoffAction`:** Cierra la sesión activa del usuario interactivo.

### 4.2. Pantalla y Periféricos
* **`TurnOffMonitorsAction`:**
  * Envía el mensaje Win32 `SendMessage(HWND_BROADCAST, WM_SYSCOMMAND, SC_MONITORPOWER, 2)`.
  * **Filtro de supresión de ratón:** Registra un hook de bajo nivel temporal que descarta micromovimientos del cursor durante los primeros 5 segundos tras el corte de señal para evitar que el monitor se reactive por vibraciones en la mesa.

### 4.3. Control de Audio
* **`AudioFadeOutAction`:**
  * Conecta con la interfaz de bajo nivel `IAudioEndpointVolume` (WASAPI).
  * Reduce el volumen maestro de forma logarítmica para igualar la curva de percepción auditiva humana:
    Volumen(t) = VolumenInicial * (1 - t / T)^2
  * Parámetros: `DurationSeconds` (ej. 300 segundos para los últimos 5 minutos).
* **`MediaControlAction`:** Emite eventos de teclado virtuales `keybd_event(VK_MEDIA_PAUSE, ...)` o `VK_MEDIA_STOP` para pausar reproductores globales antes del apagado.

### 4.4. Modo Trabajo / Anti-Ausente (Keep-Alive Engine)
Diseñado para evitar que plataformas como Microsoft Teams, Slack o Discord marquen el estado del usuario como "Ausente" o "Inactivo":
* **Nivel 1 (OS Level):** Invoca `SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED)`. Esto impide que Windows active el protector de pantalla o apague los monitores por inactividad.
* **Nivel 2 (Input Simulation con Jitter):**
  * Emite un evento de movimiento relativo del cursor de ratón mediante `SendInput` con desplazamiento neto cero (+1px horizontal, seguido inmediatamente por -1px).
  * Como alternativa segura para no desviar clics del usuario, emite la pulsación de una tecla virtual no destructiva y reservada: `VK_F15` (0x7E).
  * **Algoritmo de Jitter:** El intervalo entre pulsaciones no es constante; selecciona un valor pseudoaleatorio entre 45 y 105 segundos para replicar patrones orgánicos y no alertar monitores de telemetría de software corporativo.
  * **Interrupción suave:** Se detiene automáticamente cuando el usuario mueve activamente el ratón físico (`GetLastInputInfo` reciente) y reanuda el ciclo una vez transcurrido el tiempo de inactividad base.

### 4.5. Automatización y Diagnóstico
* **`ExecuteCommandAction`:**
  * Ejecuta binarios, scripts de PowerShell o archivos batch de forma asíncrona (`ProcessStartInfo`).
  * Parámetros: `ExecutablePath`, `Arguments`, `WorkingDirectory`, `TimeoutSeconds`, `ExpectedExitCode` (valor típico: `0`).
  * **Bifurcación:** Si el proceso termina con un código distinto al esperado, aborta el flujo de apagado para proteger el sistema, almacena el `StdErr` en los logs y dispara la notificación de contingencia.
* **`CaptureScreenshotAction`:**
  * Captura las coordenadas virtuales de todos los monitores combinados (`SystemInformation.VirtualScreen`).
  * Comprime la imagen en formato PNG o WebP con estampación de metadatos en la esquina inferior: Fecha, hora, proceso supervisado y estado de la máquina.

---

## 5. Arquitectura de Flujos Encadenados (Execution Pipeline)

Un flujo de trabajo se representa mediante una máquina de estados determinista:

    [ESTADO: INACTIVO]
           │
           │ (Comando Iniciar)
           ▼
    [ESTADO: ESPERANDO DISPARADOR] ──(Condición cumplida)──▶ [ESTADO: ACCIONES INTERMEDIAS]
           │                                                               │
           │ (Abortar / Hotkey)                                            │ (Ejecución secuencial)
           │                                                               ▼
           │                                                  [ESTADO: PERIODO DE GRACIA (60s)]
           │                                                               │
           │                                                               ├── (Posponer / Cancelar) ──▶ Regresa a Espera
           │                                                               │
           ▼                                                               ▼ (Expiración de gracia)
    [ESTADO: CANCELADO / REPOSO] ◀─────────────────────────────── [ESTADO: ACCIÓN TERMINAL]

### Reglas del Pipeline
1. Las acciones intermedias se ejecutan en orden secuencial estricto.
2. Si una acción intermedia falla (ej. un script devuelve código de error), el flujo no progresa a la acción terminal a menos que el paso tenga activada la bandera `IgnoreFailure = true`.
3. Todos los flujos concluyen con un **Periodo de Gracia** antes de llamar a la acción terminal de energía.

---

## 6. Integración con el Sistema Operativo (Tray y Notificaciones)

### 6.1. Indicador Dinámico en System Tray (Bandeja del Sistema)
* En lugar de cargar archivos `.ico` estáticos del disco, el System Tray Icon se genera dinámicamente en memoria utilizando un mapa de bits de 32x32 píxeles:
  * **En Reposo:** Glifo geométrico nítido de Nokto (blanco monocromo puro `#FFFFFF`).
  * **En Progreso:** Un anillo exterior de progreso (estilo donut chart circular). El trazo tiene un grosor de 2 píxeles. El fondo del anillo es gris translúcido y el arco completado se renderiza en cian técnico (`#00D2FF`) o ámbar de advertencia si quedan menos de 2 minutos.
  * **Al Completar:** El anillo se sustituye por un glifo de confirmación geométrico sólido de 8x8 píxeles.
* **Tooltip nativo:** Al posar el cursor, reporta en texto plano sin saltos de línea largos: `Nokto: [Nombre Flujo] - [Tiempo restante o Estado actual]`.

### 6.2. Integración con el Centro de Notificaciones de Windows
* Utiliza las APIs nativas de Windows Toast Notification mediante `Microsoft.Toolkit.Uwp.Notifications` o llamadas COM directas a `IToastNotificationManagerStatics`:
  * Se asigna un identificador de etiqueta único (`Tag = "Nokto_ActiveTask"`) para que las actualizaciones modifiquen la notificación existente sin llenar el panel de mensajes repetidos.
  * Incluye barra de progreso nativa vinculada a datos (`AdaptiveProgressBar`).
  * Incluye botones de acción directa en la tarjeta: `[ +10 Min ]`, `[ Cancelar ]` y `[ Ver Log ]`.
  * Respeta el estado de concentración "No molestar" del sistema: si está activo, omite alarmas sonoras y dirige la tarjeta directamente a la lista del centro de actividades sin interrumpir a pantalla completa.

### 6.3. Ventana Flotante de Gracia (Overlay Flotante)
* Una ventana ligera de Avalonia (`WindowStyle.None`, `Topmost = true`, `ShowInTaskbar = false`) posicionada en la esquina superior derecha de la pantalla principal durante los últimos 60 segundos previos a la acción terminal.
* Muestra la cuenta atrás en segundos grandes y una barra de progreso regresiva.
* **Atajos de teclado en el overlay:**
  * Presionar la tecla `Esc` cancela el apagado instantáneamente.
  * Presionar `Espacio` añade 10 minutos al temporizador activo.

### 6.4. Atajo de Pánico Global (Panic Hotkey)
* Registra un hook global de teclado mediante `RegisterHotKey(hWnd, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_F12)`.
* Su pulsación interrumpe de inmediato cualquier flujo activo, cancela temporizadores de gracia, restaura el volumen al nivel previo y detiene el microservidor de red local.

---

## 7. Requisitos de Rendimiento e Implementación para el Agente

1. **Gestión de hilos:** Todas las tareas de monitoreo (CPU, red, procesos, WASAPI) deben ejecutarse en hilos de fondo mediante `Task.Run` o bucles `PeriodicTimer`, sin bloquear el hilo de la interfaz de usuario de Avalonia (`DispatcherPriority.Normal` para refrescos visuales).
2. **Uso de memoria:**
   * Reutilizar buffers en la lectura de métricas y streams de red para evitar recolecciones de basura frecuentes (`GC Gen 0`).
   * No mantener mapas de bits de capturas de pantalla en memoria RAM; escribir directamente a disco mediante `FileStream` optimizado con compresión secuencial.
3. **Manejo de terminación de proceso:** Registrar manejadores en `AppDomain.CurrentDomain.ProcessExit` y en la ventana principal para asegurar que `SetThreadExecutionState` sea reseteado a `ES_CONTINUOUS` (evitando que el sistema quede bloqueado en estado despierto si la app se cierra inesperadamente).