# Bitácora de Desarrollo — Nokto

Este documento registra de manera cronológica y detallada cada avance, fase del roadmap, archivos modificados y resultados de compilación del proyecto Nokto.

## [Corrección de Icono en Barra de Título y Rediseño Ergonómico de Entradas Numéricas en Versión Portable] - 2026-10-01 20:18:00
- **Fase del roadmap:** Post-Fase 6 (Experiencia de Usuario, Estabilidad Visual y Empaquetado Portable)
- **Archivos creados o modificados:**
  - `src/Nokto.UI/Tray/DynamicTrayIconRenderer.cs`: Añadido método de alta fidelidad `RenderAppWindowIcon()` (64x64 px) renderizado directamente en memoria con SkiaSharp para la ventana Win32.
  - `src/Nokto.UI/Views/MainWindow.axaml.cs`: Asignación determinista de `Icon = DynamicTrayIconRenderer.RenderAppWindowIcon()` en el constructor de la ventana.
  - `src/Nokto.UI/App.axaml.cs`: Configuración explícita de `_mainWindow.Icon` antes de mostrar la aplicación.
  - `src/Nokto.UI/Views/MainWindow.axaml`: Actualizada URI del icono a `avares://Nokto/Assets/nokto.ico`. Rediseñado el panel de Cuenta Atrás con 3 tarjetas dedicadas (HORAS, MINUTOS, SEGUNDOS), cada una con botones paso a paso `[−]` y `[+]`, entradas numéricas con `ShowButtonSpinner="False"`, tipografía `Consolas` 18px en negrita, centrada y en color cian `#00D2FF`, eliminación de spinners comprimidos y barra de preajustes rápidos (+5m, +15m, +30m, +45m, +1h, +2h, Reset). Actualizados los paneles dinámicos de Inactividad, Al Terminar Proceso, Silencio de Audio y Batería con controles numéricos limpios y alias de binding.
  - `src/Nokto.UI/ViewModels/MainViewModel.cs`: Migradas propiedades `CountdownHours`, `CountdownMinutes`, `CountdownSeconds` a `decimal?` para compatibilidad total con `NumericUpDown.Value`, añadida propiedad `FormattedCountdownText` con cálculo en tiempo real, implementados comandos `IncrementHoursCommand`, `DecrementHoursCommand`, `IncrementMinutesCommand`, `DecrementMinutesCommand`, `IncrementSecondsCommand`, `DecrementSecondsCommand`, y alias de compatibilidad (`IsTriggerIdle`, `IdleMinutesThreshold`, `SelectedExactTime`, `SelectedProcessToWatch`, `WaitForCpuDrop`).
  - `artifacts/Nokto-Portable-x64/Nokto.exe`: Binario único portable re-publicado y actualizado al 100%.
  - `artifacts/Nokto-v1.0.0-Portable-x64.zip`: Archivo comprimido portable actualizado.
- **Resumen técnico del cambio:**
  1. **Icono de la Barra de Título y Barra de Tareas (Win32 & Avalonia):**
     - En Avalonia bajo empaquetado `SingleFile`, las rutas relativas `Icon="/Assets/nokto.ico"` intentan resolver en la raíz del disco físico (`C:\Assets\nokto.ico`), dejando `Window.Icon` en nulo y provocando que Windows muestre el icono genérico en blanco.
     - Se reemplazó la referencia XAML por el esquema nativo de recursos de ensamblado `avares://Nokto/Assets/nokto.ico` y se implementó `RenderAppWindowIcon()` en `DynamicTrayIconRenderer.cs`, generando un bitmap RGBA de 64x64 px con el disco oscuro `#16181D`, arco cian neón `#00D2FF` y núcleo blanco `#F0F2F5` inyectado por código en `MainWindow.axaml.cs` y `App.axaml.cs`. El icono se visualiza de forma nítida en la barra de título, en la barra de tareas de Windows y en Alt-Tab.
  2. **Rediseño Integral de la Entrada Numérica de Tiempo:**
     - El control `NumericUpDown` estándar en FluentTheme comprimía el cuadro de texto a menos de 25px debido a los spinners verticales `^` y `v` en columnas de ancho restringido, provocando que los números quedaran ocultos y mostrando solo una ranura con cursor vertical `[ | ^ v ]`.
     - Se eliminó el spinner integrado (`ShowButtonSpinner="False"`), otorgando el ancho completo a la caja de entrada numérica.
     - Se dispuso una estructura de 3 tarjetas individuales para **HORAS (0-23)**, **MINUTOS (0-59)** y **SEGUNDOS (0-59)** con botones de incremento `[+]` y decremento `[−]` laterales ergonómicos, tipografía monoespaciada de 18px en negrita, centrado horizontal y vertical, y color cian neón `#00D2FF`. El usuario puede tanto hacer clic en `+` / `−` como teclear números libremente o pulsar flechas arriba/abajo en su teclado.
     - Barra de botones chips con preajustes inmediatos: `+5m`, `+15m`, `+30m`, `+45m`, `+1h`, `+2h`, y `Reset`.
  3. **Compilación y Empaquetado Portable:**
     - Re-publicado `artifacts/Nokto-Portable-x64/Nokto.exe` (51.9 MB) y actualizado `artifacts/Nokto-v1.0.0-Portable-x64.zip` (46.3 MB) para reflejar de inmediato los cambios al usuario.
- **Resultado de la compilación y pruebas:**
  - `dotnet build Nokto.sln`: 0 Advertencias, 0 Errores.
  - Suite de pruebas automatizadas: 10/10 tests superados (0 fallos).
  - Verificación del ejecutable portable: Proceso iniciado y respondiendo con éxito.

## [Refinamiento Visual y Conectividad: Control Remoto LAN Robusto, PWA OLED en Reposo, Iconografía Vectorial Fluent e Icono Oficial .ico] - 2026-10-01 20:05:00
- **Fase del roadmap:** Post-Fase 6 (Experiencia de Usuario, Estabilidad de Red LAN y Diseño Visual)
- **Archivos creados o modificados:**
  - `src/Nokto.LanServer/LanHttpServer.cs`
  - `src/Nokto.LanServer/LanServerHost.cs`
  - `src/Nokto.LanServer/Embedded/pwa.html`
  - `build/setup-lan-firewall.bat` *(Nuevo)*
  - `src/Nokto.UI/Assets/nokto.ico` *(Nuevo)*
  - `src/Nokto.UI/Nokto.UI.csproj`
  - `src/Nokto.UI/App.axaml`
  - `src/Nokto.UI/Views/MainWindow.axaml`
  - `src/Nokto.UI/ViewModels/MainViewModel.cs`
  - `tests/Nokto.ConsoleTest/IconGenerator.cs` *(Nuevo)*
  - `tests/Nokto.ConsoleTest/Nokto.ConsoleTest.csproj`
  - `tests/Nokto.ConsoleTest/Program.cs`
  - `docs/AUDIT_STATUS.md`
  - `CHANGELOG.md`
- **Resumen técnico del cambio:**
  1. **Diagnóstico y Corrección Integral del Control Remoto LAN y Código QR:**
     - En `LanHttpServer.cs`: Implementada resolución determinista de IPv4 local activa (`GetLocalIpAddress`) descartando adaptadores virtuales y contenedores (`vEthernet`, `WSL`, `Docker`, `Hyper-V`, `VMware`, `VirtualBox`, `Tailscale`, `VPN`) y priorizando tarjetas físicas Wi-Fi / Ethernet con Gateway por defecto, complementado con consulta de enrutamiento UDP al kernel (`8.8.8.8`). El código QR y la URL de emparejamiento apuntan siempre a la IP física real de la LAN (ej. `192.168.1.84:4884`).
     - **Modo Puente TCP Determinista (No-Elevado):** En Windows sin permisos de administrador o sin reserva previa de URLACL, `http.sys` rechaza enlaces no-loopback arrojando `HttpListenerException (Acceso denegado)`. Se implementó un enlace resiliente en 3 capas: intenta `http://+:4884/`, si falla prueba la IP local, y si persiste el rechazo activa un puente socket TCP (`TcpListener` en `0.0.0.0:4884`) que reenvía limpiamente los paquetes HTTP a `127.0.0.1:4885`. De este modo, los smartphones en la LAN se conectan y cargan la PWA al 100% sin requerir permisos de administrador.
     - En `LanServerHost.cs`: Métodos utilitarios `GetFirewallCommand`, `GetUrlAclCommand` y `TryConfigureFirewall`.
     - Creado script `build/setup-lan-firewall.bat` para configurar la regla de entrada en el Firewall de Windows y la reserva URLACL en un solo clic.
  2. **Estado en Reposo Elegante en la PWA Móvil (`Embedded/pwa.html`):**
     - Rediseño Mobile-First en negro OLED (`#000000`).
     - Cuando el PC está en estado `Idle`, la PWA oculta los controles de tarea activa y muestra la tarjeta Hero con estado "PC en reposo (Listo)", telemetría en vivo (CPU %, RAM MB, Red KB/s) y accesos rápidos prominentes: "Apagar Pantallas", "Bloquear Sesión", "Apagar PC Ahora" y "Ver Captura de Pantalla".
     - Al activarse un flujo, la tarjeta transmuta automáticamente al temporizador decreciente con barra de progreso cian y botones "+10 Min", "+30 Min" y "ABORTAR TAREA".
  3. **Icono Oficial del Ejecutable y de la Ventana (.ICO Multirresolución):**
     - Generado en `src/Nokto.UI/Assets/nokto.ico` con capas de 16x16, 32x32, 48x48 y 256x256 px mediante SkiaSharp (`IconGenerator.cs`), conteniendo la identidad de Nokto: placa circular técnica oscura `#16181D`, arco geométrico en cian `#00D2FF` y núcleo luminoso blanco `#F0F2F5`.
     - Vinculado en `Nokto.UI.csproj` mediante `<ApplicationIcon>Assets\nokto.ico</ApplicationIcon>` y en `MainWindow.axaml` con `Icon="/Assets/nokto.ico"`.
  4. **Calibración de Dimensiones de Ventana (Layout y Escalado DPI):**
     - Dimensiones ampliadas y balanceadas para escalados DPI al 125% y 150%: `Width="880"`, `Height="640"`, `MinWidth="820"`, `MinHeight="580"`, `WindowStartupLocation="CenterScreen"`.
     - Contenedores con `Padding="24,16,24,16"` y `ScrollViewer` vertical pasivo para garantizar visibilidad permanente de controles y del botón verde "[ ▶ INICIAR TAREA ]" en cualquier resolución.
  5. **Sustitución de Emojis por Iconografía Vectorial Fluent (`StreamGeometry`):**
     - Eliminados todos los emojis de texto de pestañas, encabezados y ComboBoxes.
     - Declarados en `App.axaml` recursos `StreamGeometry` vectoriales (`IconSliders`, `IconLightning`, `IconWorkflow`, `IconPower`, `IconRestart`, `IconMoon`, `IconHibernate`, `IconLock`, `IconMonitorOff`, `IconClock`, `IconPlay`, `IconStop`).
     - Pestañas estilizadas con iconos vectoriales en sus encabezados.
     - Selector de acción terminal con iconos temáticos coloreados (Apagar en rojo `#FF4B4B`, Reiniciar en cian `#00D2FF`, Suspender/Hibernar en gris `#A0A5B0`, Bloquear en ámbar `#FFB300`, Apagar Monitores en cian `#00D2FF`).
- **Resultado de la compilación y pruebas:**
  - `dotnet build Nokto.sln`: 0 Advertencias, 0 Errores.
  - Verificación LAN: PWA servida con HTTP 200 y JSON autenticado recibido desde IP LAN `192.168.1.84:4884`.
  - 10 de 10 tests automatizados superados con éxito en 10.20s.

## [Fase 6: Disparadores AudioSilence y BatteryState, Modo Seguro Dry-Run y Suite de 10 Tests Automatizados] - 2026-10-01 19:48:00
- **Fase del roadmap:** Cierre 100% de Especificaciones PRD (`docs/01_PRD_CORE.md`) y Suite de Calidad
- **Archivos creados o modificados:**
  - `src/Nokto.Core/Abstractions/ISystemAdapter.cs`
  - `src/Nokto.Core/Engine/IWorkflowEngine.cs`
  - `src/Nokto.Core/Engine/WorkflowEngine.cs`
  - `src/Nokto.Core/Models/Enums.cs`
  - `src/Nokto.Core/Models/BatteryStatus.cs` *(Nuevo)*
  - `src/Nokto.Core/Models/SystemMetrics.cs`
  - `src/Nokto.Core/Serialization/NoktoJsonContext.cs`
  - `src/Nokto.Platform.Windows/Interop/NativeStructs.cs`
  - `src/Nokto.Platform.Windows/Interop/NativeMethods.cs`
  - `src/Nokto.Platform.Windows/Audio/WasapiAudio.cs`
  - `src/Nokto.Platform.Windows/WindowsSystemAdapter.cs`
  - `src/Nokto.Platform.MacOs/MacOsSystemAdapter.cs`
  - `src/Nokto.UI/ViewModels/MainViewModel.cs`
  - `src/Nokto.UI/Views/MainWindow.axaml`
  - `tests/Nokto.ConsoleTest/Program.cs`
  - `docs/AUDIT_STATUS.md`
  - `CHANGELOG.md`
- **Resumen técnico del cambio:**
  1. **Disparador por Silencio de Audio (`AudioSilenceTrigger` - REQ-22):**
     - En `Nokto.Platform.Windows/Audio/WasapiAudio.cs`: Implementada la interfaz nativa COM `IAudioMeterInformation` (`C02216F6-0388-4E45-9285-18B42C1B15F9`) activada directamente sobre el dispositivo de salida por defecto mediante `IMMDevice.Activate(CLSCTX_ALL)`.
     - Lectura en tiempo real del nivel de pico maestro (`GetPeakValue(out float peak)`) en rango de 0.0f a 1.0f con cero impacto en la latencia o distorsión del audio.
     - En `Nokto.Core/Engine/WorkflowEngine.cs`: Evaluación asíncrona mediante `PeriodicTimer` (1s), detectando silencio continuo cuando el pico se mantenga bajo el umbral estricto (< 0.001f) durante `silenceThresholdSeconds`.
  2. **Disparador por Estado de Batería (`BatteryStateTrigger` - REQ-23):**
     - En `Nokto.Platform.Windows/Interop/`: Mapeo de la estructura nativa `SYSTEM_POWER_STATUS` y la función P/Invoke `GetSystemPowerStatus` de `kernel32.dll`.
     - En `Nokto.Core/Models/BatteryStatus.cs`: Modelo fuertemente tipado (`HasBattery`, `IsCharging`, `IsOnAcPower`, `BatteryLifePercent`, `BatteryLifeSecondsRemaining`) registrado en `NoktoJsonContext` para compatibilidad Native AOT.
     - En `Nokto.Core/Engine/WorkflowEngine.cs`: Monitoreo en bucle asíncrono con evaluación de desconexión de corriente (`ACLineStatus == 0`) y/o caída del porcentaje de batería por debajo del umbral (`BatteryLifePercent <= threshold`).
  3. **Integración en UI Desktop (`Nokto.UI`):**
     - Controles reactivos en `MainWindow.axaml` (Pestaña "Configuración Manual"): ComboBox extendido con opciones 4 ("Silencio de Audio") y 5 ("Estado de Batería / AC"), junto a paneles de entrada dinámicos con selectores de segundos de silencio y umbrales porcentuales de batería.
     - Soporte completo en `MainViewModel.cs` para enlazar ambos disparadores directamente a la máquina de estados.
  4. **Modo Seguro de Pruebas (`IsDryRunMode`):**
     - Flag booleano añadido en `ISystemAdapter` y `WorkflowEngine`.
     - Cuando `IsDryRunMode == true`, las acciones de energía potencialmente destructivas (`Shutdown`, `Sleep`, `Hibernate`, `Restart`) omiten las llamadas reales al kernel de Windows y registran en consola y en `audit.jsonl` la cadena: `[DRY-RUN] Acción de energía simulada con éxito: {Action} (Forzado: {Force})`.
     - Las acciones no destructivas (lectura de hardware, Keep-Alive VK_F15, screenshots, WASAPI metering y microservidor LAN) operan al 100% de fidelidad real.
  5. **Suite Automatizada de Verificación (10/10 Tests en `Nokto.ConsoleTest`):**
     - Ejecutable mediante argumento de línea de comandos `--auto-test` o `-a`.
     - Cobertura exhaustiva de componentes clave:
       * `[TEST 01]` Detección de Procesos y Debounce (`Nokto.ConsoleTest.exe` supervisado).
       * `[TEST 02]` Métricas en vivo (lectura real de CPU %, RAM MB y Red KB/s).
       * `[TEST 03]` Monitor de Inactividad de Periféricos (`GetLastInputInfo` real).
       * `[TEST 04]` Detección de Estado de Batería / AC (`GetSystemPowerStatus` real).
       * `[TEST 05]` Detección de Nivel y Silencio de Audio (WASAPI COM `IAudioMeterInformation` real).
       * `[TEST 06]` Motor Keep-Alive / Jitter (simulación no destructiva `VK_F15` y micro-movimiento).
       * `[TEST 07]` Captura de Pantalla real estampada guardada en `./data/snapshots/`.
       * `[TEST 08]` Serialización AOT transaccional de `presets.json` y `audit.jsonl`.
       * `[TEST 09]` Microservidor HTTP LAN y respuesta HTTP 200 en `/api/status`.
       * `[TEST 10]` Disparo de flujo encadenado en modo Dry-Run con periodo de gracia de 5s y auditoría JSONL.
- **Resultado de la compilación y ejecución:**
  - `dotnet build Nokto.sln`: 0 Advertencias, 0 Errores.
  - `dotnet run --project tests/Nokto.ConsoleTest -- --auto-test`: 10/10 tests superados (0 fallos) en 8.14s.
  - Cumplimiento de `docs/01_PRD_CORE.md`: 100% completado (23/23 requerimientos).

## [Corrección de Arranque Portable: Instancia Única por Named Pipe y Resiliencia en Minimizaciones] - 2026-10-01 19:30:00
- **Fase del roadmap:** Post-Fase 5 (Estabilidad de Ejecución y Empaquetado Portable)
- **Archivos creados o modificados:**
  - `src/Nokto.UI/Program.cs`
  - `src/Nokto.UI/App.axaml.cs`
  - `src/Nokto.LanServer/LanHttpServer.cs`
  - `artifacts/Nokto-Portable-x64/Nokto.exe`
  - `artifacts/Nokto-v1.0.0-Portable-x64.zip`
- **Diagnóstico de causa raíz:**
  1. **Conflicto de puerto LAN en arranques secundarios:** Al cerrar la ventana principal mediante el botón `[X]`, la aplicación continuaba en ejecución en segundo plano (minimizada en el área de notificación/bandeja del sistema). Al pulsar `Nokto.exe` por segunda vez, se intentaba vincular `LanHttpServer` nuevamente al puerto 4884, arrojando una excepción no controlada (`HttpListenerException 183`) que terminaba el proceso abruptamente antes de desplegar la ventana.
  2. **Detección de instancia previa oculta en bandeja:** Cuando una aplicación gráfica de Avalonia/WPF se oculta mediante `Hide()`, Windows reporta `Process.MainWindowHandle = 0`. Cualquier intento tradicional de restaurar la ventana mediante P/Invoke sobre dicho handle nulo era ignorado y el segundo proceso se cerraba en silencio.
  3. **Persistencia de EventWaitHandle en el kernel de Windows:** El uso previo de eventos con nombre sin recolección de basura determinista provocaba estados huérfanos que marcaban falsos positivos en arranques limpios.
- **Solución implementada:**
  1. **Canal IPC Asíncrono de Instancia Única (`NamedPipeServerStream`):** El proceso principal hospeda un pipe con nombre (`Nokto_Desktop_IPC_Pipe`). Si se lanza una segunda instancia de `Nokto.exe`, ésta detecta el proceso existente, le envía un byte de señalización por el pipe y sale limpiamente (ExitCode: 0).
  2. **Restauración al Frente y Foco:** Al recibir la señal, el proceso en ejecución invoca `ShowMainWindow()` en el hilo de UI, garantizando `IsVisible = true`, restaurando el estado a `WindowState.Normal`, forzando el z-order al frente (`Topmost = true/false`) y asignando foco a la ventana.
  3. **Tolerancia a Fallos en el Servidor LAN:** Protección con bloque `try/catch` defensivo en el inicio de `HttpListener`, permitiendo que la interfaz gráfica inicie sin trabas aunque el puerto esté ocupado o requiera elevación.
  4. **Captura Global de Excepciones:** Registro automático de errores críticos a `crash.log` mediante `AppDomain.UnhandledException` y `TaskScheduler.UnobservedTaskException`.
- **Resultado:** Ejecutable portable autónomo y determinista con 0 advertencias, 0 errores, verificado con doble ejecución concurrente y empaquetado final en `artifacts/Nokto-v1.0.0-Portable-x64.zip`.

## [Refactorización UI: Configuración Manual por Pestañas y Limpieza de Barra de Estado] - 2026-10-01 19:06:00
- **Fase del roadmap:** Post-Fase 5 (Refinamiento UI/UX y Flexibilidad Operativa)
- **Archivos creados o modificados:**
  - `src/Nokto.UI/Views/MainWindow.axaml`
  - `src/Nokto.UI/ViewModels/MainViewModel.cs`
  - `CHANGELOG.md`
- **Resumen técnico del cambio:**
  1. **Estructura por Pestañas (`TabControl`):**
     - **Pestaña 1: "Configuración Manual" (Vista principal por defecto):** Panel en 2 columnas claras que otorga libertad total al usuario para definir cualquier combinación de disparador y acción terminal.
     - **Pestaña 2: "Accesos Rápidos":** Contiene las 4 tarjetas operativas preconfiguradas (Modo Trabajo, Modo Dormir, Fin de Tarea, Apagado Rápido).
     - **Pestaña 3: "Modo Studio":** Integración nativa del catálogo de presets y editor secuencial de tuberías como pestaña propia.
  2. **Columna Izquierda: "¿Cuándo ejecutar? (Disparador)":**
     - Selector desplegable con 4 modos y paneles reactivos:
       * **Cuenta Atrás:** Inputs numéricos de Horas (0-23), Minutos (0-59), Segundos (0-59) con botones rápidos de suma (`+15m`, `+30m`, `+1h`, `Reset`).
       * **Hora Exacta:** Control `TimePicker` de formato 24h con cálculo dinámico en tiempo real (`ExactTimeSummaryText`).
       * **Inactividad:** Input numérico para minutos de inactividad de periféricos sin usar ratón ni teclado.
       * **Al Terminar Proceso:** Selector desplegable de procesos activos con botón de refresco y umbral opcional de CPU.
  3. **Columna Derecha: "¿Qué acción realizar?":**
     - Selector de acción terminal: Apagar el PC, Suspender, Hibernar, Reiniciar, Bloquear Sesión o Apagar Monitores.
     - Checkboxes modificadores: Forzar cierre inmediato, desvanecimiento WASAPI, aviso flotante previo de gracia (60s) y captura de pantalla de evidencia.
  4. **Zona Inferior y Panel Reactivo:**
     - Botón destacado a ancho completo `[ ▶ INICIAR TAREA ]` en verde `#00E676` en estado de reposo.
     - En ejecución: panel con título, tiempo restante en fuente monospace de 20px, barra de progreso cian `#00D2FF`, botón `[ ⏱ +10 Min ]` y botón `[ ⏹ CANCELAR / ABORTAR ]` en rojo `#FF4B4B`.
  5. **Limpieza de Barra de Estado:**
     - Eliminación total del botón residual que mostraba `"True"` en la esquina inferior izquierda.
     - Conservación de métricas pasivas de hardware en vivo (CPU, RAM, Red) y acceso directo al modal de código QR para control remoto LAN.
- **Resultado de la compilación:**
  - `dotnet build Nokto.sln`: 0 Advertencias, 0 Errores.
  - Verificación automatizada con `dotnet run --project tests/Nokto.ConsoleTest -- --verify`: 10 de 10 pruebas exitosas.

---

## [Fase 5: Empaquetado, CI/CD y Distribución] - 2026-10-01 18:50:00
- **Fase del roadmap:** Fase 5 (Packaging, CI/CD & Distribution)
- **Archivos creados o modificados:**
  - `src/Nokto.UI/Nokto.UI.csproj`
  - `build/inno-setup/nokto-setup.iss`
  - `.github/workflows/release.yml`
  - `build/winget/nokto.yaml`
  - `CHANGELOG.md`
- **Resumen técnico del cambio:**
  1. **Configuración de Binario Único Portable ([`Nokto.UI.csproj`](file:///c:/Users/jorge/Proyectos/Nokto/src/Nokto.UI/Nokto.UI.csproj)):**
     - Ajuste de nombre de ensamblado a `Nokto` (`<AssemblyName>Nokto</AssemblyName>`).
     - Habilitación de publicación en archivo único auto-contenido (`PublishSingleFile=true`, `SelfContained=true`, `IncludeNativeLibrariesForSelfExtract=true`, `EnableCompressionInSingleFile=true`, `TieredCompilation=true`).
     - Generación validada de `Nokto.exe` (51.8 MB) sin dependencias de runtimes o librerías externas en el equipo host.
  2. **Script de Instalador Formal con Inno Setup ([`nokto-setup.iss`](file:///c:/Users/jorge/Proyectos/Nokto/build/inno-setup/nokto-setup.iss)):**
     - Empaquetador nativo para Windows x64 con compresión `lzma2/ultra64` sólida.
     - Tareas para crear acceso directo en el escritorio y configurar inicio automático con Windows (`startup`).
     - Soporte multiidioma (español e inglés) e instalación en espacio de usuario sin requerir privilegios de administrador (`PrivilegesRequired=lowest`).
  3. **Pipeline de Integración y Entrega Continua ([`release.yml`](file:///c:/Users/jorge/Proyectos/Nokto/.github/workflows/release.yml)):**
     - Workflow de GitHub Actions activado por etiquetas de versión `v*.*.*` o ejecución manual (`workflow_dispatch`).
     - Pasos automatizados: Checkout, Setup .NET SDK 8, compilación y publicación de binario único, generación del marcador `portable.lock`, empaquetado del archivo comprimido `.zip` portable, compilación del instalador `.exe` con `Minionguyjpro/Inno-Setup-Action@v1.2.2`, y publicación automática del Release en GitHub con todos los artefactos adjuntos.
  4. **Manifiesto de Distribución para Windows Package Manager ([`nokto.yaml`](file:///c:/Users/jorge/Proyectos/Nokto/build/winget/nokto.yaml)):**
     - Especificación singleton acorde al esquema 1.6.0 de Winget (`PackageIdentifier: Nokto.Nokto`).
     - Configuración de tipo de instalador Inno y soporte de actualización silenciosa.
- **Resultado de la compilación:**
  - `dotnet build Nokto.sln`: 0 Advertencias, 0 Errores.
  - `dotnet publish src/Nokto.UI/Nokto.UI.csproj`: 0 Errores, generación exitosa de `publish/win-x64/Nokto.exe`.
  - Verificación automatizada con `dotnet run --project tests/Nokto.ConsoleTest -- --verify`: 10 de 10 pruebas exitosas.

---

## [Fase 4: Microservidor LAN, PWA Embebida y Control Remoto Web] - 2026-10-01 18:48:00
- **Fase del roadmap:** Fase 4 (Local LAN Control & Mobile PWA)
- **Archivos creados o modificados:**
  - `src/Nokto.LanServer/Nokto.LanServer.csproj`
  - `src/Nokto.LanServer/Embedded/pwa.html`
  - `src/Nokto.LanServer/LanHttpServer.cs`
  - `src/Nokto.LanServer/QrCodeService.cs`
  - `src/Nokto.UI/App.axaml.cs`
  - `src/Nokto.UI/ViewModels/MainViewModel.cs`
  - `src/Nokto.UI/Views/MainWindow.axaml.cs`
  - `src/Nokto.UI/Views/QrModalWindow.axaml`
  - `src/Nokto.UI/Views/QrModalWindow.axaml.cs`
  - `tests/Nokto.ConsoleTest/Nokto.ConsoleTest.csproj`
  - `tests/Nokto.ConsoleTest/Program.cs`
  - `CHANGELOG.md`
- **Resumen técnico del cambio:**
  1. **Microservidor HTTP Embebido ([`LanHttpServer`](file:///c:/Users/jorge/Proyectos/Nokto/src/Nokto.LanServer/LanHttpServer.cs)):**
     - Basado en `System.Net.HttpListener` de huella mínima (<1 MB memoria adicional).
     - Mecanismo de enlace resiliente en 3 niveles: intenta wildcard `http://*:port/`, enlace con IP LAN `http://<local-ip>:port/`, y fallback estricto no elevado a `localhost` y `127.0.0.1`.
     - Seguridad por token de autorización (`X-Nokto-Auth` header o parámetro `?auth=token` en query string). Rechazo con `401 Unauthorized` si no coincide.
     - Soporte integral de CORS (`*`, métodos `GET, POST, OPTIONS`).
     - Endpoints REST deterministas:
       * `GET /`: Entrega la PWA embebida con cabecera `text/html; charset=utf-8`.
       * `GET /api/status`: Retorna `SystemStatusState` (estado de máquina, trigger actual, progreso restante, métricas CPU/RAM/Red).
       * `GET /api/screen-preview`: Captura en tiempo real de pantalla principal y entrega como imagen JPEG.
       * `POST /api/action/abort`: Interrupción inmediata del flujo o periodo de gracia.
       * `POST /api/action/postpone`: Aplaza el flujo o extiende el periodo de gracia en N segundos (por defecto +600s).
       * `POST /api/action/quick-power`: Ejecuta acciones directas de energía (`Sleep`, `Hibernate`, `Shutdown`, `Restart`, `TurnOffMonitors`).
  2. **PWA Embebida OLED ([`pwa.html`](file:///c:/Users/jorge/Proyectos/Nokto/src/Nokto.LanServer/Embedded/pwa.html)):**
     - Embebida como recurso compreso dentro del ensamblado `Nokto.LanServer`.
     - Diseño responsive para smartphones (Mobile First) con fondo negro absoluto `#000000` (OLED-friendly) para consumo de batería nulo.
     - Interfaz con botones táctiles de 48px de área táctil mínima, feedback háptico vía `navigator.vibrate()`.
     - Sondeo asíncrono pasivo cada 2 segundos (`/api/status`), visor modal de captura de pantalla con botón de refresco y alarma visual en rojo titilante durante el periodo de gracia.
  3. **Generador y Emparejamiento por Código QR ([`QrCodeService`](file:///c:/Users/jorge/Proyectos/Nokto/src/Nokto.LanServer/QrCodeService.cs) & [`QrModalWindow`](file:///c:/Users/jorge/Proyectos/Nokto/src/Nokto.UI/Views/QrModalWindow.axaml)):**
     - Integración con paquete de alto rendimiento `QRCoder` (1.8.0).
     - Generación directa en memoria de bytes PNG del URL de emparejamiento con token inyectado (`http://<local-ip>:<port>/?auth=<token>`).
     - Modal de Avalonia UI con renderizado del QR en pantalla y botón para copiar URL al portapapeles.
  4. **Suite de Verificación Automatizada (Pruebas 9 y 10):**
     - Test de microservidor HTTP (PWA servida con texto clave, rechazo 401 sin auth, 200 OK autenticado, endpoints POST validados).
     - Test de generación de bytes PNG con validación de cabecera de imagen.
- **Resultado de la compilación:**
  - `dotnet build Nokto.sln`: 0 Advertencias, 0 Errores.
  - Verificación automatizada con `dotnet run --project tests/Nokto.ConsoleTest -- --verify`: 10 de 10 pruebas exitosas.

---

## [Fase 3: Motor de Flujos Encadenados y Persistencia] - 2026-10-01 18:38:00
- **Fase del roadmap:** Fase 3 (Pipeline & Presets, State Machine, Triggers & Persistence)
- **Archivos creados o modificados:**
  - `src/Nokto.Core/Engine/IWorkflowEngine.cs`
  - `src/Nokto.Core/Engine/WorkflowEngine.cs`
  - `src/Nokto.Core/Persistence/StorageResolver.cs`
  - `src/Nokto.Core/Persistence/PersistenceService.cs`
  - `src/Nokto.Core/Serialization/NoktoJsonContext.cs`
  - `src/Nokto.UI/App.axaml.cs`
  - `src/Nokto.UI/ViewModels/MainViewModel.cs`
  - `tests/Nokto.ConsoleTest/Program.cs`
  - `CHANGELOG.md`
- **Resumen técnico del cambio:**
  1. **Máquina de Estados Reactiva ([`WorkflowEngine`](file:///c:/Users/jorge/Proyectos/Nokto/src/Nokto.Core/Engine/WorkflowEngine.cs)):**
     - Orquestador desacoplado de ciclo de vida con estados formales: `Idle`, `WaitingTrigger`, `ExecutingActions`, `GracePeriod`, `Paused`, `Completed`, `Failed`.
     - Cero busy-waiting en todas las operaciones asíncronas utilizando `System.Threading.PeriodicTimer`.
  2. **Evaluación Asíncrona de Disparadores (Triggers):**
     - `Countdown`: Temporizador decreciente por segundo con porcentaje de progreso exacto.
     - `ProcessExit`: Monitoreo pasivo de procesos por nombre (`Process.GetProcessesByName`) con periodo de confirmación continua (Debounce) para evitar falsos positivos ante reinicios de subprocesos.
     - `SustainedLoad`: Muestreo continuo mediante ventana deslizante pasiva de CPU (<8% durante 90s).
     - `NetworkThroughput`: Evaluación combinada de velocidad de descarga y subida con umbral en KB/s.
     - `UserIdle`: Detección de inactividad de periféricos por hardware mediante `GetLastInputInfo`.
  3. **Ejecutor Secuencial de Acciones Intermedias:**
     - `CaptureScreenshot`: Captura de pantalla multi-monitor a través de GDI nativo con almacenamiento automático en `data/snapshots/` con sellado de fecha/hora.
     - `AudioFadeOut`: Atenuación perceptual WASAPI progresiva.
     - `MuteAudio` y `TurnOffMonitors`: Control inmediato de salidas de audio y vídeo.
     - `ExecuteCommand`: Ejecución de scripts (`.cmd`, `.bat`, `.ps1`, `.exe`) con validación de código de salida `expectedExitCode == 0`. Si un script falla y `IgnoreFailure = false`, interrumpe inmediatamente el flujo, aborta la acción terminal y escribe el error en `audit.jsonl`.
  4. **Persistencia Determinista y Modo Portable:**
     - [`StorageResolver`](file:///c:/Users/jorge/Proyectos/Nokto/src/Nokto.Core/Persistence/StorageResolver.cs): Detección automática de `portable.lock` o `config.json` para desviar almacenamiento a `./data/` o `%APPDATA%\Nokto\`.
     - [`PersistenceService`](file:///c:/Users/jorge/Proyectos/Nokto/src/Nokto.Core/Persistence/PersistenceService.cs): Carga y guardado de `config.json` y `presets.json` mediante contexto `NoktoJsonContext` sin reflexión.
     - Contexto especializado [`NoktoCompactJsonContext`](file:///c:/Users/jorge/Proyectos/Nokto/src/Nokto.Core/Serialization/NoktoJsonContext.cs) (`WriteIndented = false`) para registro append-only de auditoría en `audit.jsonl` bajo formato estricto JSON Lines (1 línea por evento).
  5. **Integración con Modo Studio y Consola de Verificación:**
     - Conexión de `WorkflowEngine` y `PersistenceService` con `MainViewModel` para ejecución de presets desde la interfaz gráfica.
     - Inclusión de pruebas automatizadas en `Nokto.ConsoleTest` para validar captura de pantalla, persistencia, resolución de rutas y aborto determinista ante scripts fallidos.
- **Resultado de la compilación:**
  - `dotnet build Nokto.sln`: 0 Advertencias, 0 Errores.
  - Verificación automatizada con `dotnet run --project tests/Nokto.ConsoleTest -- --verify`: 8 de 8 pruebas exitosas.

---

## [Fase 2: Interfaz de Escritorio Avalonia y Bandeja del Sistema] - 2026-10-01 18:32:00
- **Fase del roadmap:** Fase 2 (Desktop UI & System Tray)
- **Archivos creados o modificados:**
  - `Nokto.sln`
  - `src/Nokto.UI/Nokto.UI.csproj`
  - `src/Nokto.UI/app.manifest`
  - `src/Nokto.UI/App.axaml`
  - `src/Nokto.UI/App.axaml.cs`
  - `src/Nokto.UI/Program.cs`
  - `src/Nokto.UI/Tray/DynamicTrayIconRenderer.cs`
  - `src/Nokto.UI/ViewModels/MainViewModel.cs`
  - `src/Nokto.UI/Views/MainWindow.axaml`
  - `src/Nokto.UI/Views/MainWindow.axaml.cs`
  - `src/Nokto.UI/Views/GraceOverlayWindow.axaml`
  - `src/Nokto.UI/Views/GraceOverlayWindow.axaml.cs`
  - `src/Nokto.UI/Views/QrModalWindow.axaml`
  - `src/Nokto.UI/Views/QrModalWindow.axaml.cs`
  - `CHANGELOG.md`
- **Resumen técnico del cambio:**
  1. **Configuración de Avalonia UI v11 y Arquitectura MVVM:**
     - Integración del proyecto ejecutable `src/Nokto.UI` con `net8.0-windows10.0.19041.0`, FluentTheme en modo oscuro estricto (`RequestedThemeVariant="Dark"`), y fuentes Inter.
     - `app.manifest` con soporte para PerMonitorV2 DPI awareness y compatibilidad con Windows 10/11.
     - Implementación de `MainViewModel` mediante `CommunityToolkit.Mvvm` (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`).
  2. **Vista Simple (Modo Rápido 460x580 px) y Modo Studio (820x620 px):**
     - Diseño neominimalista industrial respetando los tokens de color (`#0E0F12`, `#16181D`, `#262930`, `#00D2FF`, `#00E676`, `#FFB300`, `#FF4B4B`).
     - Cuatro tarjetas operativas en Modo Rápido:
       * **Modo Trabajo:** Mantiene activo Teams/Slack con botón dinámico verde (`Activo (Jitter ON)`) y cancelación.
       * **Modo Dormir:** Temporizador de 45 minutos con activación automática de atenuación de volumen WASAPI (últimos 10 min) y corte de señal de monitor (últimos 5 min).
       * **Fin de Tarea:** Selector desplegable de procesos activos del sistema (`Process.GetProcesses()`) con vigilancia y apagado automático al cierre.
       * **Apagado Rápido:** Chips de selección inmediata (`30m`, `1h`, `2h`) y acción directa de apagado de monitores.
     - Panel de Modo Studio expandible con catálogo de presets y editor secuencial de tuberías (Disparador -> Acción Intermedia -> Acción Terminal).
     - Barra de telemetría inferior con monitor pasivo de CPU, RAM y Red en tiempo real.
  3. **Ventana de Gracia Flotante (`GraceOverlayWindow`):**
     - Ventana sin bordes de 380x110 px, `Topmost=true`, anclada en la esquina superior derecha.
     - Cuenta regresiva visual con barra ámbar decreciente (`#FFB300`).
     - Atajos de teclado: `Escape` para cancelar el apagado inmediatamente y `Espacio` para posponer 10 minutos (+600s).
  4. **Renderizado Dinámico en System Tray ([`DynamicTrayIconRenderer`](file:///c:/Users/jorge/Proyectos/Nokto/src/Nokto.UI/Tray/DynamicTrayIconRenderer.cs)):**
     - Generación de iconos en memoria de 32x32 píxeles con SkiaSharp sin necesidad de archivos `.ico` en disco.
     - Modos visuales: Reposo (glifo de luna/círculo Nokto blanco), En Progreso (anillo con arco dinámico proporcional en cian/ámbar y punto pulsante central) y Completado (rombo sólido verde neón).
     - Menú contextual nativo con opciones de abrir, apagar monitores, posponer 15 min, abortar y salir.
     - Intercepción del cierre (`Closing` de `MainWindow`): minimiza a la bandeja del sistema en lugar de destruirse.
- **Resultado de la compilación:**
  - `dotnet build Nokto.sln`: 0 Advertencias, 0 Errores.
  - Ejecución y arranque en memoria verificado con éxito.

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
