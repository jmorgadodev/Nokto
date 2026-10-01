# 04_DEV_ROADMAP.md — Hoja de Ruta de Desarrollo, Criterios de Aceptación y CI/CD

**Proyecto:** Nokto  
**Versión de especificación:** 1.0.0  
**Stack de destino:** .NET 8/9 LTS (`net8.0-windows10.0.19041.0` / `net9.0`) + Avalonia UI (v11+)  
**Estrategia de ejecución para el Agente:** Desarrollo incremental desacoplado (Core Headless -> Desktop UI -> Engine Pipeline -> LAN Server -> Packaging).  

---

## 1. Estructura de la Solución y Árbol del Proyecto

El código fuente debe estructurarse en proyectos modulares para evitar el acoplamiento entre la lógica del sistema operativo y la interfaz visual:

```text
Nokto.sln
├── src/
│   ├── Nokto.Core/                     # Biblioteca de clases (netstandard2.1 / net8.0)
│   │   ├── Abstractions/               # ISystemAdapter, IWorkflowEngine, IMetricsCollector
│   │   ├── Models/                     # Presets, Config, Metrics, Events
│   │   ├── Serialization/              # NoktoJsonContext (AOT JSON Source Generator)
│   │   └── Engine/                     # Máquina de estados y evaluador de disparadores
│   │
│   ├── Nokto.Platform.Windows/         # Implementación Win32 / WASAPI nativa
│   │   ├── Interop/                    # P/Invoke signatures (Powrprof, User32, Kernel32)
│   │   ├── Audio/                      # WASAPI Endpoint Volume Fade-out
│   │   ├── KeepAlive/                  # SendInput + SetThreadExecutionState Jitter Engine
│   │   ├── Metrics/                    # PerformanceCounter + NetworkInterface readers
│   │   └── WindowsSystemAdapter.cs     # Implementación concreta de ISystemAdapter
│   │
│   ├── Nokto.Platform.MacOs/           # Stubs multiplataforma para compilación futura
│   │   └── MacOsSystemAdapter.cs       # Implementación base con IOKit/pmset
│   │
│   ├── Nokto.LanServer/                # Microservidor HTTP embebido
│   │   ├── Embedded/                   # PWA WebAssets (index.html, app.css, app.js minificados)
│   │   ├── Endpoints/                  # Handlers para status, preview, abort, postpone
│   │   └── HttpServer.cs               # System.Net.HttpListener liviano
│   │
│   └── Nokto.UI/                       # Aplicación Avalonia ejecutable
│       ├── Assets/                     # Fuentes e iconos vectoriales monocromos
│       ├── ViewModels/                 # MVVM CommunityToolkit (Main, Studio, Overlay)
│       ├── Views/                      # MainWindow, GraceOverlayWindow, QrModalWindow
│       ├── Tray/                       # Renderizador dinámico de mapa de bits del TrayIcon
│       ├── Program.cs                  # Punto de entrada y detector de modo portable
│       └── App.axaml                   # FluentTheme y estilos globales
│
├── build/
│   ├── inno-setup/                     # Script de empaquetado para instalador Windows
│   │   └── nokto-setup.iss
│   └── winget/                         # Plantilla de manifiesto para Winget
│       └── nokto.yaml
│
└── tests/
    ├── Nokto.Core.Tests/               # Pruebas unitarias de pipeline y JSON
    └── Nokto.Integration.Tests/        # Pruebas de disparadores en consola
```

---

## 2. Fases de Desarrollo Paso a Paso

El agente debe completar cada fase de forma atómica y verificar sus criterios de aceptación antes de escribir código de la siguiente.

```text
┌──────────────┐     ┌──────────────┐     ┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│   FASE 1     │     │    FASE 2    │     │    FASE 3    │     │    FASE 4    │     │    FASE 5    │
│ Core Headless│ ──▶ │  Desktop UI  │ ──▶ │   Pipeline   │ ──▶ │ Microservidor│ ──▶ │ Distribución │
│ & Win32 APIs │     │ & Tray Icon  │     │   & Presets  │     │  LAN + QR    │     │  CI/CD Winget│
└──────────────┘     └──────────────┘     └──────────────┘     └──────────────┘     └──────────────┘
```

---

### Fase 1: Motor Headless y Adaptadores de Sistema Operativo

#### Objetivos de implementación
1. Crear la solución y los proyectos base.
2. Definir contratos en `Nokto.Core/Abstractions/ISystemAdapter.cs`.
3. Implementar `WindowsSystemAdapter` en `Nokto.Platform.Windows`:
   * **Energía:** `ExitWindowsEx`, `InitiateSystemShutdownEx` (Graceful y Forzado), y `SetSuspendState` (Sleep/Hibernate).
   * **Monitores:** Envío de `WM_SYSCOMMAND (SC_MONITORPOWER, 2)`.
   * **Audio:** Manejo de `IAudioEndpointVolume` (WASAPI) para aplicar atenuación logarítmica sin librerías externas pesadas.
   * **Modo Trabajo (Keep-Alive):** Integrar bucle de jitter pseudoaleatorio (45-105s) con `SendInput` (evento nulo $\pm 1\text{px}$ o tecla `VK_F15`) combinado con `SetThreadExecutionState`.
   * **Métricas:** Recolección pasiva de CPU global y velocidad combinada de interfaces de red con temporizador de 2 segundos.
4. Crear ejecutable de consola temporal para pruebas funcionales directas.

#### Criterios de Aceptación (Definition of Done)
- [ ] El comando de apagado de monitor apaga las pantallas y no se reactiva con vibraciones leves de la mesa.
- [ ] El motor de Keep-Alive mantiene activo el temporizador sin interferir con la escritura de texto si el usuario está activo.
- [ ] La reducción de volumen de 100 a 0 sigue una curva perceptiblemente suave en la prueba de 15 segundos.
- [ ] El consumo de CPU del colector de métricas no supera el 0.1% sostenido.

---

### Fase 2: Interfaz de Escritorio Avalonia y Bandeja del Sistema

#### Objetivos de implementación
1. Configurar Avalonia UI v11 con `FluentTheme` en modo oscuro por defecto.
2. Implementar **Vista Simple (Modo Rápido)** con las cuatro tarjetas funcionales: Modo Trabajo, Modo Dormir, Fin de Tarea y Apagado Rápido.
3. Desarrollar la **Ventana de Gracia Flotante (`GraceOverlayWindow`)**:
   * Sin bordes, siempre visible arriba (`Topmost`), con cuenta atrás y soporte para cancelar con tecla `Esc`.
4. Diseñar e implementar el generador de iconos en memoria para el **System Tray**:
   * Dibujar el anillo de progreso en un mapa de bits de 32x32 píxeles con SkiaSharp y actualizar el icono del Tray de Avalonia en tiempo real.
5. Conectar notificaciones nativas de Windows Toast (`AdaptiveProgressBar`) con tag de actualización para no saturar el Centro de Actividades.

#### Criterios de Aceptación (Definition of Done)
- [ ] Al cerrar la ventana principal (`[✕]`), la app se minimiza al System Tray en lugar de destruirse.
- [ ] El icono del System Tray dibuja el porcentaje de progreso visual en tiempo real mientras un temporizador corre.
- [ ] La ventana de gracia aparece sobre cualquier aplicación en pantalla completa y se cancela al pulsar `Esc`.
- [ ] El consumo de RAM de la app minimizada en Tray es inferior a 30 MB.

---

### Fase 3: Motor de Flujos Encadenados y Persistencia

#### Objetivos de implementación
1. Implementar la máquina de estados reactiva en `Nokto.Core/Engine/WorkflowEngine.cs`.
2. Habilitar la evaluación asíncrona de disparadores (Procesos en curso con debounce, umbrales sostenidos de CPU, inactividad de periféricos).
3. Implementar el ejecutor secuencial de acciones intermedias:
   * Captura de pantalla multi-monitor estampada con fecha/hora y métricas.
   * Ejecución de scripts (`.bat`, `.ps1`) con validación de código de salida `Exit Code == 0`.
4. Implementar **Modo Studio** en la interfaz para añadir, reordenar y eliminar pasos.
5. Gestionar la persistencia con detección de modo portable (`./data/presets.json` vs `%APPDATA%`).

#### Criterios de Aceptación (Definition of Done)
- [ ] Un flujo configurado para esperar a que termine un proceso (ej. `notepad.exe`) detecta el cierre, ejecuta la captura de pantalla y entra en fase de gracia de forma autónoma.
- [ ] Si un script falla (código de salida $\ne 0$), el apagado se aborta de inmediato y el error se escribe en `audit.jsonl`.
- [ ] Al copiar la carpeta de Nokto a otro directorio junto a su `portable.lock`, todas las configuraciones se cargan intactas.

---

### Fase 4: Microservidor LAN y Control Remoto Web

#### Objetivos de implementación
1. Implementar `HttpServer` embebido con `System.Net.HttpListener` en `Nokto.LanServer`.
2. Servir los activos estáticos de la PWA (HTML, CSS, JS) incrustados en memoria (`EmbeddedResource`).
3. Implementar autenticación por token temporal y validación en headers/queries.
4. Implementar endpoints: `/api/status`, `/api/screen-preview`, `/api/action/abort`, `/api/action/postpone`.
5. Integrar generador de códigos QR en el modal de escritorio mediante `QRCoder`.

#### Criterios de Aceptación (Definition of Done)
- [ ] Al escanear el código QR con un teléfono en la misma red Wi-Fi, la interfaz web móvil carga en menos de 500 ms.
- [ ] El botón *"Ver Captura"* en el móvil muestra la pantalla del PC en formato WebP con un payload menor a 150 KB.
- [ ] Pulsar *"Abortar Tarea"* desde el celular detiene el flujo en el PC al instante.
- [ ] Si el token no coincide, el servidor rechaza la conexión con código HTTP 401.

---

### Fase 5: Empaquetado, Publicación y CI/CD

#### Objetivos de implementación
1. Ajustar el archivo `.csproj` para compilación en archivo único (`PublishSingleFile`).
2. Redactar el script de Inno Setup (`nokto-setup.iss`) para generar el instalador formal.
3. Configurar el pipeline de GitHub Actions para compilar y publicar artefactos automáticamente ante cada tag de versión.
4. Generar el manifiesto de publicación para Windows Package Manager (Winget).

---

## 3. Configuración del Proyecto para Binario Portable (`Nokto.UI.csproj`)

El archivo de proyecto debe configurarse de la siguiente manera para garantizar que no dependa de runtimes externos:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <BuiltInComInteropSupport>true</BuiltInComInteropSupport>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>

    <!-- Configuración para Binario Único Portable -->
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
    
    <!-- Optimización de Tamaño y Memoria -->
    <PublishTrimmed>false</PublishTrimmed>
    <TieredCompilation>true</TieredCompilation>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.1.0"/>
    <PackageReference Include="Avalonia.Desktop" Version="11.1.0"/>
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.1.0"/>
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.2.2"/>
    <PackageReference Include="QRCoder" Version="1.6.0"/>
    <PackageReference Include="SkiaSharp" Version="2.88.8"/>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Nokto.Core\Nokto.Core.csproj"/>
    <ProjectReference Include="..\Nokto.Platform.Windows\Nokto.Platform.Windows.csproj"/>
    <ProjectReference Include="..\Nokto.LanServer\Nokto.LanServer.csproj"/>
  </ItemGroup>

</Project>
```

---

## 4. Script de Empaquetado Inno Setup (`nokto-setup.iss`)

```pascal
[Setup]
AppId={{C8E1D942-7F3A-4B2E-9D1B-8A7E4F2C1B0D}
AppName=Nokto
AppVersion=1.0.0
AppPublisher=Nokto Project
AppPublisherURL=[https://github.com/nokto/nokto](https://github.com/nokto/nokto)
AppSupportURL=[https://github.com/nokto/nokto/issues](https://github.com/nokto/nokto/issues)
DefaultDirName={autopf}\Nokto
DefaultGroupName=Nokto
DisableProgramGroupPage=yes
OutputDir=..\..\artifacts\installer
OutputBaseFilename=Nokto-Setup-x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startup"; Description: "Iniciar Nokto con Windows"; GroupDescription: "Opciones de inicio:"

[Files]
Source: "..\..\publish\win-x64\Nokto.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Nokto"; Filename: "{app}\Nokto.exe"
Name: "{autodesktop}\Nokto"; Filename: "{app}\Nokto.exe"; Tasks: desktopicon
Name: "{userstartup}\Nokto"; Filename: "{app}\Nokto.exe"; Tasks: startup

[Run]
Filename: "{app}\Nokto.exe"; Description: "{cm:LaunchProgram,Nokto}"; Flags: nowait postinstall skipifsilent
```

---

## 5. Pipeline de Compilación Continua (`.github/workflows/release.yml`)

Automatiza la compilación del binario portable y el instalador ante la creación de un tag:

```yaml
name: Nokto Build & Release Pipeline

on:
  push:
    tags:
      - 'v*.*.*'

jobs:
  build-windows:
    runs-on: windows-latest
    steps:
      - name: Checkout del repositorio
        uses: actions/checkout@v4

      - name: Instalar .NET SDK
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Compilar Binario Único Portable (Self-Contained)
        run: >
          dotnet publish src/Nokto.UI/Nokto.UI.csproj
          -c Release
          -r win-x64
          --self-contained true
          -p:PublishSingleFile=true
          -p:IncludeNativeLibrariesForSelfExtract=true
          -o ./publish/win-x64

      - name: Crear archivo marcador para versión portable
        run: New-Item -Path ./publish/win-x64/portable.lock -ItemType File

      - name: Comprimir versión Portable (.zip)
        run: Compress-Archive -Path ./publish/win-x64/* -DestinationPath ./artifacts/Nokto-v${{ github.ref_name }}-Portable-x64.zip

      - name: Compilar Instalador con Inno Setup
        uses: Minionguyjpro/Inno-Setup-Action@v1.2.2
        with:
          path: build/inno-setup/nokto-setup.iss

      - name: Publicar Release en GitHub con Assets
        uses: softprops/action-gh-release@v2
        with:
          files: |
            ./artifacts/*.zip
            ./artifacts/installer/*.exe
          draft: false
          prerelease: false
          generate_release_notes: true
```

---

## 6. Manifiesto para Windows Package Manager (`winget`)

Una vez publicado el instalador en GitHub Releases, se envía la solicitud al repositorio oficial de Microsoft (`winget-pkgs`):

```yaml
# yaml-language-server: $schema=[https://aka.ms/winget-manifest.singleton.1.6.0.schema.json](https://aka.ms/winget-manifest.singleton.1.6.0.schema.json)
PackageIdentifier: Nokto.Nokto
PackageVersion: 1.0.0
PackageName: Nokto
Publisher: Nokto Project
License: MIT
ShortDescription: Gestor de estados de energía, tareas encadenadas y mantenimiento de actividad para Windows.
Moniker: nokto
Tags:
  - shutdown
  - sleep
  - timer
  - keep-alive
  - automation
  - productivity
Installers:
  - Architecture: x64
    InstallerType: inno
    InstallerUrl: [https://github.com/usuario/nokto/releases/download/v1.0.0/Nokto-Setup-x64.exe](https://github.com/usuario/nokto/releases/download/v1.0.0/Nokto-Setup-x64.exe)
    InstallerSha256: PEGAR_AQUI_EL_HASH_SHA256_DEL_INSTALADOR
    UpgradeBehavior: install
ManifestType: singleton
ManifestVersion: 1.6.0
```

Para enviar la actualización en un solo comando:
```powershell
wingetcreate submit [https://github.com/usuario/nokto/releases/download/v1.0.0/Nokto-Setup-x64.exe](https://github.com/usuario/nokto/releases/download/v1.0.0/Nokto-Setup-x64.exe)
```