# Nokto ◐

> **Consola Determinista de Energía, Telemetría y Automatización de Escritorio**

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20x64-0078D6.svg)](https://microsoft.com/windows)
[![.NET: 8.0](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/)

Nokto es una suite determinista de alto rendimiento para Windows diseñada para automatizar el ciclo de vida del equipo, supervisar telemetría de hardware y cuotas locales de herramientas de IA, ejecutar rutinas encadenadas y gestionar tareas manuales con total seguridad y control.

---

## 🌟 Características Principales

### 1. Cabina de Control Diario (Inicio)
- **Telemetría en Vivo:** Supervisión continua y ligera de CPU (usuario/kernel), memoria RAM, GPUs híbridas (dedicada/integrada), I/O de disco y tráfico de red.
- **Detección de Hardware y Red:** Resolución de adaptadores de red, IP local, túneles VPN y perfil de hardware nativo.
- **Audio WASAPI y Periféricos:** Medición de picos de audio, silenciamiento bidireccional y monitor de inactividad de periféricos (`GetLastInputInfo`).
- **Radar de Herramientas IA:** Inspección pasiva local en disco de cuotas y estado de entornos como Antigravity IDE, Codex y OpenCode.

### 2. Control Manual Inmediato
- **Disparadores Directos:** Ejecución inmediata, temporizador regresivo, hora fija, inactividad, salida de proceso o desconexión de alimentación.
- **Gestión de Aplicaciones:** Selección rápida de programas instalados en el menú Inicio, argumentos, carpetas de trabajo y cierre limpio de ventanas/aplicaciones.
- **Control de Audio, Pantalla y Sesión:** Desvanecimiento gradual de volumen, pausa multimedia, bloqueo de estación y apagado de monitores.
- **Aviso Previo Cancelable (Grace Overlay):** Cuenta atrás visual antes de la acción final con opción de posponer 10 minutos o cancelar.

### 3. Configurador de Rutinas (Pipeline Determinista)
- **Constructor de Flujos:** Disparadores configurables (horario semanal recurrente, silencio prolongado de audio, inactividad de descargas de red, etc.).
- **Secuencia de Acciones Reordenables:** Apertura de programas, scripts (`.exe`, `.bat`, `.ps1`), control de volumen, espera, comandos y evidencias.
- **Sticky Footer Ergonómico:** Panel fijo inferior con botones directos `[ ▶ INICIAR RUTINA ]`, `[ ⏹ FINALIZAR ]` y `[ 💾 Guardar Rutina ]`.

### 4. Módulo de Actualizaciones del Sistema
- **Actualización Semver:** Consulta automática y asíncrona de releases públicas desde GitHub.
- **Detección Automática de Entorno:**
  - *Versión Instalable:* Descarga de `Nokto-Setup-x64.exe` y ejecución asistida.
  - *Versión Portable:* Mecanismo de swap en caliente sin bloqueo de archivo mediante script por lotes temporal desacoplado.

### 5. Control Remoto LAN Móvil
- **Microservidor Local Ligero:** Interfaz web PWA responsiva con diseño optimizado para OLED, accesible desde cualquier teléfono o tablet en la misma red Wi-Fi/LAN mediante código QR sin salir a internet.

---

## 📦 Distribución

Nokto se distribuye en dos formatos oficiales de 64 bits:

1. **Portable (Standalone):**
   - Ejecutable autocontenido único (`Nokto.exe`) en `artifacts/Nokto-Portable-x64/`.
   - Cero dependencias externas requeridas en el sistema de destino.
   - Datos y configuración guardados junto al ejecutable (`./data`).

2. **Instalador de Windows:**
   - Asistente de instalación estándar (`Nokto-Setup-x64.exe`) en `artifacts/Nokto-Installer-x64/`.
   - Integración con el menú Inicio y registro en Aplicaciones y características.

---

## 🛠️ Compilación y Pruebas

### Requisitos de desarrollo:
- Windows 10/11 x64 (Build 19041 o superior)
- .NET 8.0 SDK

### Ejecutar pruebas:
```powershell
dotnet test Nokto.sln -c Release
```

### Publicar binario portable (Single-File):
```powershell
dotnet publish src\Nokto.UI\Nokto.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts\Nokto-Portable-x64\
```

### Publicar binario para instalador:
```powershell
dotnet publish src\Nokto.UI\Nokto.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o artifacts\Nokto-Installer-x64\App\
```

---

## 📄 Licencia

Este proyecto está bajo la Licencia [MIT](LICENSE).
