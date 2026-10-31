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
Ejecuta las pruebas de release con acciones nativas y proveedores online simulados, almacenamiento temporal explícito y reportes en `artifacts/qa-release/`:
```powershell
./build/test-release.ps1
```

### 2. Generar Ejecutable Portable (Single-File)
Compila los paquetes públicos desde el mismo binario autocontenido, sin datos DEV:
```powershell
./build/build-release.ps1 -PortableOnly
```
Los paquetes se ubican en `artifacts/release-2.0.0/`: ejecutable público, ZIP con licencias y `SHA256SUMS`. La versión se define en `Directory.Build.props`.

### 3. Generar Distribución para Instalador
Con Inno Setup 6 instalado o extraído en modo portable, genera también el instalador:
```powershell
./build/build-release.ps1 -IsccPath 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
```

---

## 🧪 Estándares de Calidad
- Todas las compilaciones deben completarse con 0 errores y 0 warnings bajo `TreatWarningsAsErrors=true`.
- Toda nueva funcionalidad de plataforma debe incluir su correspondiente prueba en el proyecto de test.
- Mantener procesamiento local para automatización y documentar cada servicio online opcional en `PRIVACY.md`.

La entrada `--release-qa` de ConsoleTest usa exclusivamente adaptadores falsos. Los modos históricos de diagnóstico manual, especialmente `--auto-test`, pueden actuar sobre audio, teclado o pantallas y no deben ejecutarse como QA desatendido. `dotnet test` de ConsoleTest invoca ahora solo `--release-qa`.

La landing se mantiene en `site/`. `python build/test-landing.py` comprueba escritorio/móvil, temas, imágenes y navegación con Playwright. Configura GitHub Pages para publicar con Actions antes de usar `.github/workflows/pages.yml`; la rama `gh-pages` anterior permanece como historial.

El pipeline de release exige QA antes de generar paquetes y crea un borrador de GitHub Releases. Verifica el instalador, las descargas y los checksums antes de publicar ese borrador como versión latest. No mezclar datos de pruebas, credenciales ni carpetas DEV con los paquetes públicos.
