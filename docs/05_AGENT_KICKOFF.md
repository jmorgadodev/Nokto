# 05_AGENT_KICKOFF.md — Guía Maestra de Inicialización y Prompt para Antigravity

**Proyecto:** Nokto  
**Destinatario:** Agente autónomo de desarrollo en Antigravity  
**Propósito:** Protocolo de arranque, configuración inicial de la solución y prompt maestro para ejecutar la Fase 1 sin intervención manual innecesaria.

---

## 1. Protocolo de Ejecución del Agente

Antes de generar o modificar cualquier archivo, el agente debe seguir estas directrices operativas:

1. **Lectura y contexto:** 
   * Leer en orden estricto los archivos de especificación: `01_PRD_CORE.md`, `02_UI_UX_SPEC.md`, `03_DATA_CONTRACTS.md` y `04_DEV_ROADMAP.md`.
2. **Desarrollo por fases cerradas:** 
   * Queda terminantemente prohibido avanzar a la interfaz gráfica (Fase 2) sin haber completado y probado los adaptadores Win32 y el motor sin cabeza (Fase 1).
3. **Restricción de dependencias:** 
   * No añadir paquetes NuGet que no figuren explícitamente en `04_DEV_ROADMAP.md` sin autorización.
   * Prohibido el uso de Chromium, Electron, WebView2 o navegadores embebidos en el Core.
4. **Verificación continua:** 
   * Tras cada bloque de código, compilar el proyecto (`dotnet build`) y ejecutar las pruebas de consola asociadas. Reportar errores con logs limpios antes de corregirlos.

---

## 2. Comandos CLI para Andamiaje Inicial

El agente debe ejecutar la siguiente secuencia en la terminal para crear la estructura de proyectos limpia:

```bash
# 1. Crear carpeta raíz y solución .NET
mkdir Nokto && cd Nokto
dotnet new sln -n Nokto

# 2. Crear carpetas de código fuente
mkdir src tests build docs

# 3. Crear proyectos modulares
dotnet new classlib -n Nokto.Core -o src/Nokto.Core -f net8.0
dotnet new classlib -n Nokto.Platform.Windows -o src/Nokto.Platform.Windows -f net8.0-windows10.0.19041.0
dotnet new classlib -n Nokto.Platform.MacOs -o src/Nokto.Platform.MacOs -f net8.0
dotnet new classlib -n Nokto.LanServer -o src/Nokto.LanServer -f net8.0
dotnet new console -o tests/Nokto.ConsoleTest -f net8.0-windows10.0.19041.0

# 4. Vincular proyectos a la solución
dotnet sln add src/Nokto.Core/Nokto.Core.csproj
dotnet sln add src/Nokto.Platform.Windows/Nokto.Platform.Windows.csproj
dotnet sln add src/Nokto.Platform.MacOs/Nokto.Platform.MacOs.csproj
dotnet sln add src/Nokto.LanServer/Nokto.LanServer.csproj
dotnet sln add tests/Nokto.ConsoleTest/Nokto.ConsoleTest.csproj

# 5. Configurar referencias entre proyectos
dotnet add src/Nokto.Platform.Windows/Nokto.Platform.Windows.csproj reference src/Nokto.Core/Nokto.Core.csproj
dotnet add src/Nokto.LanServer/Nokto.LanServer.csproj reference src/Nokto.Core/Nokto.Core.csproj
dotnet add tests/Nokto.ConsoleTest/Nokto.ConsoleTest.csproj reference src/Nokto.Core/Nokto.Core.csproj
dotnet add tests/Nokto.ConsoleTest/Nokto.ConsoleTest.csproj reference src/Nokto.Platform.Windows/Nokto.Platform.Windows.csproj

# 6. Añadir dependencias base requeridas
dotnet add src/Nokto.Platform.Windows/Nokto.Platform.Windows.csproj package CommunityToolkit.Mvvm --version 8.2.2
dotnet add src/Nokto.Platform.Windows/Nokto.Platform.Windows.csproj package SkiaSharp --version 2.88.8
```

---

## 3. Prompt Maestro para Copiar y Pegar en Antigravity

Copia el siguiente bloque y entrégaselo como primer mensaje al agente en su entorno de trabajo:

```text
Actúa como un Ingeniero de Software Principal especializado en arquitectura de sistemas Windows, desarrollo en C# (.NET 8 LTS) y aplicaciones nativas de alto rendimiento.

Vas a construir "Nokto", una aplicación de escritorio ligera (<25 MB RAM), portable y determinista orientada a la gestión de estados de energía, automatización de tareas encadenadas y mantenimiento de actividad (Modo Trabajo anti-ausente).

Toda la especificación técnica del producto se encuentra dividida en 4 documentos en la carpeta docs/:
- docs/01_PRD_CORE.md (Especificación funcional de sistema y bajo nivel)
- docs/02_UI_UX_SPEC.md (Diseño, layouts, System Tray y web remota)
- docs/03_DATA_CONTRACTS.md (Esquemas JSON AOT y APIs locales)
- docs/04_DEV_ROADMAP.md (Estructura de fases y criterios de aceptación)

TU TAREA ACTUAL: FASE 1 (CORE HEADLESS & WINDOWS SYSTEM ADAPTER)

Ejecuta únicamente el alcance de la Fase 1:
1. Crea la solución .NET y los proyectos modulares (Nokto.Core, Nokto.Platform.Windows, Nokto.ConsoleTest) según la estructura indicada en 04_DEV_ROADMAP.md.
2. En Nokto.Core, define la interfaz ISystemAdapter y todos los modelos de datos de entrada/salida (PowerAction, SystemMetrics, KeepAliveMode).
3. En Nokto.Core/Serialization, implementa el contexto AOT JsonSerializerContext según 03_DATA_CONTRACTS.md.
4. En Nokto.Platform.Windows, implementa WindowsSystemAdapter con llamadas nativas Win32:
   - Apagado limpio y forzado (ExitWindowsEx / InitiateSystemShutdownEx).
   - Apagado de pantallas (WM_SYSCOMMAND SC_MONITORPOWER).
   - Atenuación logarítmica de volumen mediante WASAPI (IAudioEndpointVolume).
   - Modo Trabajo (Keep-Alive): bucle con jitter pseudoaleatorio (45-105s) emitiendo pulsación VK_F15 o movimiento neto de mouse +-1px vía SendInput, combinado con SetThreadExecutionState.
   - Recolección pasiva de métricas de CPU y red.
5. En tests/Nokto.ConsoleTest, crea un menú interactivo en consola para verificar cada función del adaptador en vivo.

REGLAS DE TRABAJO:
- No crees la interfaz gráfica en Avalonia todavía; el motor debe ser 100% testeable desde consola.
- No uses busy-waiting; utiliza PeriodicTimer para temporizadores y monitoreo pasivo.
- Compila con dotnet build y asegúrate de que no existan advertencias ni errores antes de entregar el informe de avance.
- Al terminar, muéstrame el resumen de lo implementado y el resultado de la compilación para proceder con las pruebas.
```

---

## 4. Checklist de Validación para el Supervisor (Tú)

Cuando el agente reporte que ha terminado la Fase 1, abre la terminal y valida estos 5 puntos antes de autorizarle a pasar a la Fase 2:

1. **Compilación limpia:**
   ```bash
   dotnet build
   ```
   *Debe arrojar `0 errores` y `0 advertencias`.*
2. **Prueba de apagado de monitor:** 
   * Corre el proyecto de consola (`dotnet run --project tests/Nokto.ConsoleTest`), selecciona la opción de apagar monitores y confirma que las pantallas queden en negro sin que el PC se suspenda.
3. **Prueba de Modo Trabajo (Keep-Alive):** 
   * Abre Microsoft Teams o un procesador de texto, activa el modo trabajo en la consola de prueba y comprueba que no escribe caracteres basura ni desvía el cursor.
4. **Prueba de desvanecimiento de volumen:** 
   * Pon música o un video y ejecuta la prueba de fade de 15 segundos; comprueba que el volumen desciende de forma suave y no a saltos bruscos.
5. **Consumo de recursos:** 
   * Abre el Administrador de Tareas de Windows; el proceso de consola debe figurar con `0.0%` de CPU sostenido.