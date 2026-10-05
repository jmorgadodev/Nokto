# Guía de Contribución y Compilación — Nokto

Gracias por tu interés en compilar o contribuir al desarrollo de Nokto. Este documento describe los requisitos del entorno y los pasos para compilar la solución desde el código fuente.

---

## 🛠 Requisitos del Sistema

- **Sistema Operativo:** Windows 10 (Build 19041+) o Windows 11 (x64)
- **SDK:** .NET 8.0 SDK (x64)
- **Herramientas opcionales:** Visual Studio 2022, JetBrains Rider, VS Code o terminal PowerShell con privilegios estándar.

---

## 🚀 Compilación desde el Código Fuente

Abre una consola de comandos o PowerShell en la raíz del proyecto clonado:

### 1. Restaurar dependencias y ejecutar pruebas
Ejecuta la suite completa de pruebas unitarias y de integración:
```powershell
dotnet test Nokto.sln -c Release
```

### 2. Generar Ejecutable Portable (Single-File)
Compila el binario único autocontenido con librerías nativas integradas:
```powershell
dotnet publish src\Nokto.UI\Nokto.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts\Nokto-Portable-x64\
```
El ejecutable resultante se ubicará en `artifacts\Nokto-Portable-x64\Nokto.exe`.

### 3. Generar Distribución para Instalador
Compila los binarios desplegados requeridos para el empaquetado del instalador:
```powershell
dotnet publish src\Nokto.UI\Nokto.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o artifacts\Nokto-Installer-x64\App\
```

---

## 🧪 Estándares de Calidad
- Todas las compilaciones deben completarse con 0 errores y 0 warnings bajo `TreatWarningsAsErrors=true`.
- Toda nueva funcionalidad de plataforma debe incluir su correspondiente prueba en el proyecto de test.
- Mantener el principio de cero dependencias externas y cero telemetría remota.
