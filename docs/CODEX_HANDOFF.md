# Documento de Relevo Técnico (Handoff a Codex)

**Proyecto:** Nokto — Sistema Determinista de Energía & Automatización  
**Fecha:** Octubre 2026  
**Destinatario:** Codex (Ingeniero de Software / Modelo de Desarrollo)  
**Versión de Código Base:** v1.0.0-rc  

---

## Índice de Contenidos

1. [Resumen de Arquitectura](#1-resumen-de-arquitectura)
2. [Estructura del Proyecto y Rutas Clave](#2-estructura-del-proyecto-y-rutas-clave)
3. [Contratos de Datos y Persistencia](#3-contratos-de-datos-y-persistencia)
4. [Misión Específica para Codex: Parser Real de Cuotas IA (`AiQuotaService.cs`)](#4-misión-específica-para-codex-parser-real-de-cuotas-ia-aiquotaservicecs)
   - [4.1 Estado Actual](#41-estado-actual)
   - [4.2 Hallazgos Forenses en Disco Local](#42-hallazgos-forenses-en-disco-local)
   - [4.3 Especificaciones de Implementación para Codex](#43-especificaciones-de-implementación-para-codex)
5. [Reglas de Oro del Proyecto y Restricciones Estrictas](#5-reglas-de-oro-del-proyecto-y-restricciones-estrictas)
6. [Comandos de Verificación y Compilación](#6-comandos-de-verificación-y-compilación)
7. [Concurrencia de Rutinas y Manejo de Audio en v1.0](#7-concurrencia-de-rutinas-y-manejo-de-audio-en-v10)

---

## 1. Resumen de Arquitectura

Nokto es una solución de escritorio determinista de grado industrial diseñada para Windows 10/11 con soporte multiplataforma proyectado:

- **Runtime & Lenguaje:** .NET 8 (C# 12) con modo estricto de tipos nulos (`<Nullable>enable</Nullable>`).
- **Framework de UI:** Avalonia UI (v11.x) con MVVM Toolkit (`CommunityToolkit.Mvvm`).
- **Filosofía Operativa:** 100% Offline y Air-Gapped. No abre sockets externos hacia internet ni envía telemetría.
- **Eficiencia de Recursos:** Consumo objetivo en reposo < 25 MB RAM y 0.0% CPU continuo mediante sondeo adaptativo con `PeriodicTimer`.
- **Despliegue:** Single-file ejecutable portable (`Nokto.exe`) con autodetección de almacenamiento (modo portable junto al ejecutable en `./data/` o instalado en `%APPDATA%\Nokto\`).
- **Compatibilidad Native AOT:** Serialización 100% reflection-free mediante `System.Text.Json` Source Generators (`NoktoJsonContext` y `NoktoCompactJsonContext`).

---

## 2. Estructura del Proyecto y Rutas Clave

```
C:\Users\jorge\Proyectos\Nokto\
├── Nokto.sln                          # Solución principal (.NET 8)
├── src\
│   ├── Nokto.Core\                    # Motor de reglas, modelos y abstracciones
│   │   ├── Abstractions\              # ISystemAdapter, IWorkflowEngine
│   │   ├── Engine\                    # WorkflowEngine (máquina de estados reactiva)
│   │   ├── Models\                    # Enums.cs, Config.cs, Presets.cs, SystemStatusState.cs
│   │   ├── Persistence\               # PersistenceService, StorageResolver
│   │   ├── Serialization\             # NoktoJsonContext (AOT)
│   │   └── Services\                  # AiQuotaService (inspección de entornos IA)
│   ├── Nokto.Platform.Windows\        # Implementación Win32, WASAPI, Power, Shell
│   │   ├── Audio\                     # WasapiAudioAdapter (CoreAudio COM)
│   │   ├── Diagnostics\               # WindowsMetricsProvider (PInvoke CPU/RAM/Disco/Red)
│   │   ├── Input\                     # WindowsInputSimulator (SendInput VK_F15 / Jitter)
│   │   ├── Power\                     # WindowsPowerAdapter (SetSuspendState, ExitWindowsEx)
│   │   └── Screen\                    # WindowsScreenCapture (GDI+ bitmap capture)
│   └── Nokto.UI\                      # Capa de presentación Avalonia
│       ├── ViewModels\                # MainViewModel, StudioStepItem, AiEnvironmentItem
│       ├── Views\                     # MainWindow.axaml, GraceOverlayWindow.axaml
│       ├── Tray\                      # DynamicTrayIconRenderer (renderizado nativo de icono)
│       └── Assets\                    # Temas Dark/Light/OLED, iconos vectoriales
└── tests\
    └── Nokto.ConsoleTest\             # Suite de 14 tests de integración deterministas
```

---

## 3. Contratos de Datos y Persistencia

### 3.1 `Config.cs` (`config.json`)
Almacena configuración de usuario: temas (Dark/Light/OLED/System/Scheduled), idioma (`es-ES`, `en-US`), comportamiento de bandeja, tolerancia de inactividad, habilitación de capturas de evidencia y visualización del radar IA.

### 3.2 `Presets.cs` (`presets.json`)
Define los contratos de automatización:
- **`TriggerDefinition`:** `Countdown`, `FixedTime`, `Schedule`, `ProcessExit`, `SustainedLoad`, `NetworkThroughput`, `AudioSilence`, `UserIdle`, `BatteryState`.
- **`PipelineStepDefinition`:** `CaptureScreenshot`, `AudioFadeOut`, `MuteAudio`, `MediaControl`, `TurnOffMonitors`, `ExecuteCommand`, `KeepAliveEngine`, `WaitDelay`.
- **`TerminalActionDefinition`:** `Shutdown`, `Sleep`, `Hibernate`, `Restart`, `LockStation`, `Logoff`, `None` (con parámetro opcional `gracePeriodSeconds` para el aviso flotante).

**5 Rutinas de Fábrica del Sistema (`IsSystemPreset = true`):**
1. `preset_workday_keepalive`: "Jornada Laboral Anti-Ausente" (UserIdle 3m -> KeepAliveEngine F15 -> LockStation tras 8h).
2. `preset_blender_night_render`: "Render Nocturno Blender" (ProcessExit `blender.exe` -> CaptureScreenshot -> Shutdown con 60s gracia).
3. `preset_sleep_multimedia`: "Modo Dormir Multimedia" (Countdown 45m -> AudioFadeOut WASAPI 15s + MediaControl -> Sleep).
4. `preset_download_finished`: "Fin de Descarga Pesada" (NetworkThroughput <50 KB/s por 180s -> CaptureScreenshot -> Shutdown).
5. `preset_pomodoro_break`: "Pausa Activa / Pomodoro" (Countdown 50m -> Aviso flotante de gracia -> LockStation).

### 3.3 `SystemStatusState.cs` & `AuditLogEntry` (`audit.jsonl`)
Estado en vivo de telemetría y bitácora inmutable en formato JSON Lines append-only estricto UTF-8 sin BOM.

---

## 4. Misión Específica para Codex: Parser Real de Cuotas IA (`AiQuotaService.cs`)

### 4.1 Estado Actual
La clase `AiQuotaService` actualmente:
1. Detecta la presencia física del binario (`antigravity.exe`, `codex.exe`, `opencode.exe`) en directorios de usuario (`%LOCALAPPDATA%`, `%APPDATA%`, `PATH`).
2. Verifica carpetas de configuración activas.
3. Genera un estado representativo pero **no lee los porcentajes numéricos exactos de consumo en vivo** (ventana de 5 horas y límite de cuota semanal).

### 4.2 Hallazgos Forenses en Disco Local
Durante la auditoría forense se confirmaron los siguientes depósitos de estado en la máquina de desarrollo de Windows:

1. **Bases de datos SQLite de VS Code / Extensiones:**
   - Ruta: `%APPDATA%\Antigravity IDE\User\globalStorage\state.vscdb`
   - Ruta alternativa: `%APPDATA%\Code\User\globalStorage\state.vscdb`
   - Formato: Base de datos SQLite estándar (tabla `ItemTable` con columnas `key` (TEXT) y `value` (BLOB/TEXT)).
   - Claves identificadas con telemetría de cuotas y tokens:
     * Prefijos `antigravity.quota`, `antigravity.session`, `codex.quota`, `copilot.usage`.
2. **Directorio de configuración Codex / Antigravity CLI:**
   - Ruta: `%USERPROFILE%\.codex\` y `%USERPROFILE%\.antigravity\`
   - Archivos: `auth.json`, `config.json`, `sessions\*.json`, archivos de logs de telemetría local.
   - En pantalla del IDE se renderiza explícitamente: `"Uso restante: 5 h 82%, Semanal 97%"`. Esos datos provienen de lecturas en estos almacenes locales.

### 4.3 Especificaciones de Implementación para Codex
Codex debe implementar la extracción determinista en `AiQuotaService.cs` siguiendo estas pautas:

1. **Lectura Segura y No Bloqueante:**
   - Para bases de datos SQLite (`state.vscdb`), copiar previamente el archivo a una ubicación temporal en `%TEMP%` con modo lectura exclusiva (`FileAccess.Read`, `FileShare.ReadWrite`) para evitar conflictos de bloqueo con el IDE en ejecución.
   - Abrir conexión `Microsoft.Data.Sqlite` con `Mode=ReadOnly`.
2. **Consultas Específicas:**
   - Consultar `SELECT key, value FROM ItemTable WHERE key LIKE '%quota%' OR key LIKE '%usage%' OR key LIKE '%session%'`.
3. **Parseo Resiliente:**
   - Tratar los valores devueltos como cadenas JSON o fragmentos escapados.
   - Extraer valores numéricos de porcentaje restante/usado (ej. `windowUsagePercent`, `weeklyUsagePercent`, `resetsAt`).
4. **Fallback Limpio y Determinista:**
   - Si el archivo no existe o está dañado, registrar nota de diagnóstico silenciosa en log interno y presentar el entorno como "Activo (Telemetría en espera)", **sin inventar porcentajes falsos** ni lanzar excepciones que congelen la UI.
5. **Alineación con el Modelo `AiEnvironmentItem`:**
   - Actualizar las propiedades observables: `FiveHourUsagePercentage`, `WeeklyUsagePercentage`, `UsageDetailsText` y `TimeRemainingText`.

---

## 5. Reglas de Oro del Proyecto y Restricciones Estrictas

1. **Integridad de Bindings en XAML:** Prohibido renombrar o eliminar bindings en `MainWindow.axaml` (especialmente `WorkModeStatusText`, `PowerSourceStatusText`, `ActiveTaskTitle`, `TimeRemainingText`, selectores de Control Manual).
2. **Control Manual Intacto:** En la pestaña `Control Manual`, los modificadores reactivos (`ForceCloseApps`, `EnableWasapiFade`, `EnableGraceOverlay`, `TakeEvidenceScreenshot`) y el ComboBox de acción terminal con sus 6 opciones deben preservarse intactos.
3. **Preservar Rutinas de Fábrica:** Las 5 rutinas del sistema nunca deben sobrescribirse de forma destructiva; siempre deben fusionarse y mantener `IsSystemPreset = true`.
4. **Cero Dependencias de Red:** No introducir clientes HTTP externos, librerías de analíticas en la nube ni SDKs pesados.
5. **Consumo de Memoria:** Mantener el uso en reposo por debajo de 25 MB. Usar `Dispose()` estricto en controladores GDI y COM WASAPI.

---

## 6. Comandos de Verificación y Compilación

Para compilar y validar la integridad del sistema:

```powershell
# Compilación completa de la solución
$dotnetExe = "C:\Users\jorge\AppData\Local\Microsoft\dotnet\dotnet.exe"
$env:DOTNET_ROOT = "C:\Users\jorge\AppData\Local\Microsoft\dotnet"
& $dotnetExe build Nokto.sln

# Ejecución de la suite de pruebas (14 tests automáticos)
& $dotnetExe run --project tests\Nokto.ConsoleTest\Nokto.ConsoleTest.csproj

# Publicación de binario standalone portable (Win-x64)
& $dotnetExe publish src\Nokto.UI\Nokto.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:TreatWarningsAsErrors=true -o artifacts\Nokto-Portable-x64\
```

## 7. Concurrencia de Rutinas y Manejo de Audio en v1.0

`WorkflowEngine` coordina un `ConcurrentDictionary<string, RunningWorkflowContext> _activeWorkflows`. Cada ID tiene una tarea, un token de cancelación enlazado, un cronómetro y un `WorkflowRunner` independiente. `PresetDefinition` continúa siendo el contrato de rutina persistido; sus parámetros y pasos se copian al iniciar. `StartRoutine` devuelve la tarea de ejecución y los inicios repetidos de una ID activa se unen a esa misma tarea. `StopRoutine(id)` cancela y retira exclusivamente esa ID; la finalización de una ejecución antigua no puede retirar una nueva con la misma ID. Los tokens se liberan cuando termina la tarea, y el cierre de la aplicación cancela y espera las ejecuciones antes de liberar el adaptador.

`IsRoutineRunning(id)` y `GetActiveWorkflows()` reemplazan el bloqueo global de la UI. `StatusChanged` conserva un estado agregado de compatibilidad e incluye las ejecuciones activas. `RunningWorkflowInfo` distingue tiempo restante conocido de tiempo transcurrido: cuenta atrás y aviso previo usan **Restante**, vigilancia y trabajo continuo usan **Activa**. La jornada laboral conserva `delayHours` y mantiene el loop existente de actividad hasta ese plazo; finalizarla impide su acción terminal pendiente. Los errores se propagan sólo a la tarea correspondiente y se auditan; una finalización solicitada se registra como `Finalized`.

`RoutineItem` aporta badges reactivos; `ActiveRoutineItem` enlaza cada chip de Inicio con su propia finalización. El comando de inicio permite ejecuciones simultáneas y consulta únicamente la ID del editor. Control Manual conserva la ID `manual_execution` y sus controles independientes. El aviso flotante actúa sobre la rutina con el periodo de gracia más próximo; finalizar o posponer ese aviso no afecta a las demás. La tecla de pánico y la opción global de bandeja finalizan todas. El Footer limita su ancho a 220 px y resume varias ejecuciones en un contador con tooltip, sin ampliar las cápsulas de hardware.

Los botones de audio y los callbacks de `GlobalHotkeyService` (`RegisterHotKey`/`WM_HOTKEY`) invocan `ToggleInputMute` o `ToggleOutputMute` en el dispatcher de Avalonia y refrescan `AudioDevices` inmediatamente. Los atajos predeterminados son `Ctrl+Shift+M` para micrófono y `Ctrl+Shift+O` para salida, configurables y persistidos en `AppSettings`. WASAPI lee volumen y mute del endpoint predeterminado; el paso `MuteAudio` de una rutina usa ese mismo adaptador. Audio y energía son recursos del sistema: las rutinas tienen estados independientes, pero no endpoints privados. El panel permanece en Inicio y su visibilidad se configura en Ajustes.

`dotnet test Nokto.sln` ejecuta las pruebas de concurrencia con un adaptador simulado (sin efectos de energía o audio), la suite existente, las verificaciones de selección/finalización en Avalonia y la prueba nativa de 10.000 actualizaciones de bandeja sin crecimiento de recursos. No se reinician automáticamente rutinas al abrir el portable; config, rutinas guardadas y auditoría conservan su almacenamiento local.

## 8. Consolidación de Inicio y Control Remoto LAN (2026-10-03)

Inicio dispone de seis módulos en tres columnas y dos filas, sin scroll global. `HomeAudioView` condensa los controles completos de audio. `ActiveTasksView` comparte las cápsulas de 270×95 entre Inicio y Control Manual; únicamente su lista tiene scroll cuando las ejecuciones exceden el espacio disponible. Las tareas manuales actuales utilizan IDs `manual_GUID`, permitiendo iniciar y finalizar cada temporizador por separado. La ventana arranca maximizada desde XAML y desde su constructor.

El endpoint de audio se resuelve por el ID multimedia real de Windows (`GetDefaultAudioEndpoint(eRender, eMultimedia)`), con lectura de volumen y silencio del mismo dispositivo. La sincronización de la UI nunca cambia el predeterminado por el orden de enumeración. HDMI1 sigue apareciendo si Windows lo tiene seleccionado. Los nuevos ajustes usan Ctrl+Shift+M / Ctrl+Shift+S; las combinaciones previamente guardadas se conservan.

`LocalRemoteServerService` ofrece la SPA española autocontenida y los endpoints de estado, snapshot, audio, finalización por ID y energía solicitados. El puerto predeterminado de nuevas configuraciones es 5050 y el servicio permanece desactivado hasta habilitarlo en Ajustes. HTTP.sys/HttpListener es el primer transporte; si el comodín requiere una URL ACL, utiliza Kestrel local sin elevar el portable ni alterar permisos o firewall. Core incorpora el framework ASP.NET Core y UI genera el QR con QRCoder.

El servidor admite únicamente loopback e IPv4 privadas RFC1918, verifica Host/Origin y exige una clave aleatoria por arranque para la API. El QR incluye esa clave en el fragmento de URL. No se utiliza el token legado persistido ni se incluyen credenciales o configuración en el JSON de estado. Las peticiones, cuerpos y capturas concurrentes están limitados. La SPA no carga recursos externos, muestra contenido de tareas mediante textContent y recibe CSP con nonce y cabeceras sin caché. La vista de pantalla reutiliza la captura GDI nativa y se codifica en memoria con Skia a JPEG 1280×720, calidad 70, conservando proporción.

Las regresiones LAN prueban autenticación, interfaz IP local, aislamiento por tarea, Host/Origin, cuerpos inválidos y demasiado grandes, audio, energía simulada, snapshot y reinicio con rotación de clave. Las pruebas de Avalonia verifican seis tarjetas a dos tamaños, diez cápsulas sin desplazar las acciones, selección por ID, QR y activación/desactivación del servidor, además del tamaño y letterboxing del JPEG. El navegador móvil se verificó a 390 y 360 px con adaptador simulado, sin accionar energía del equipo.
