# NOKTO 2.0 DEV — Documento maestro del proyecto

> **Documento de continuidad, decisiones, estado técnico, riesgos y camino a la publicación.**
>
> **Fecha de corte:** 26 de octubre de 2026
> **Estado:** desarrollo y pruebas locales; **Nokto 2.0 NO publicado**.
> **Versión estable:** conservada en `master`; **no sustituirla ni modificarla sin autorización**.
> **Fuente de verdad para el avance:** este documento + [ROADMAP_2.0.md](ROADMAP_2.0.md) + código, pruebas y Git.
> **Criterio de estado:** «implementado» significa que existe código; **no** equivale a «aprobado por el usuario», «validado en producción» ni «publicado».

## 1. Resumen ejecutivo

Nokto es una aplicación de escritorio de Windows para operaciones, telemetría, audio, automatización y rutinas locales. La iniciativa **Nokto 2.0** surge para incorporar a la **misma aplicación** una **Isla Dinámica** flotante, situada preferentemente en la parte superior central de la pantalla. Debe complementar —no reemplazar ni romper— el dashboard y las funciones originales.

La isla es una pequeña cápsula que puede permanecer en reposo con ojos animados, indicadores minimalistas o **sin indicador**; se expande de modo fluido para mostrar herramientas útiles. El usuario decide su apariencia, tamaño, páginas, información visible y modalidad de arranque en Windows. Se toma inspiración de la experiencia de la Dynamic Island y los controles de Apple Music **como referencias de jerarquía e interacción**, sin copiar iconos, imágenes, diseños propietarios ni personajes de terceros.

Se acordó **desarrollar y probar primero una versión Portable Dev local** y **solo después de validar** preparar el instalador, el portable público, la actualización de los usuarios y la versión 2.0. Se permiten avances por fases, iteraciones visuales y correcciones según revisión del usuario. No está autorizada la publicación automática ni el cambio de la versión estable por el hecho de que una fase compile.

**Prioridades rectoras:** fluidez, interfaz premium e intuitiva, reposo realmente minimalista, consumo reducido de CPU/RAM/GPU, privacidad, configuración por página, retrocompatibilidad, pruebas reales y control del usuario.

## 2. Por qué se creó otro ambiente (aislamiento real)

La versión **estable** ya funciona y tiene usuarios, configuración, tareas y mecanismos de distribución propios. Probar una isla siempre visible, nuevas animaciones, notificaciones, multimedia y persistencia en ese árbol sería arriesgado. Por eso se separó el trabajo.

| Propósito | Ubicación / rama |
|---|---|
| Proyecto estable | `C:\Users\jorge\Proyectos\Nokto` |
| Rama estable | `master` (seguimiento de `origin/master`) |
| Ambiente 2.0 | `C:\Users\jorge\Proyectos\Nokto-v2-dev` |
| Rama de desarrollo | `feature/nokto-2-dev` |
| Ejecutable de pruebas | `artifacts\Nokto-Dev-Portable-x64\Nokto.Dev.exe` |
| Preparación alternativa si DEV está abierto | `artifacts\Nokto-Dev-Portable-staging\` |
| Documento de fases | [ROADMAP_2.0.md](ROADMAP_2.0.md) |
| Repositorio remoto del proyecto estable | `https://github.com/jmorgadodev/Nokto.git` |

**Importante:** no son dos clones completamente independientes. `Nokto-v2-dev` es un **Git worktree** asociado al repositorio `Nokto`, con árbol de trabajo y rama separados pero historial/objetos Git compartidos. El archivo `.git` del DEV apunta a `Nokto/.git/worktrees/Nokto-v2-dev`. **No borrar, mover ni renombrar manualmente estos árboles** sin gestionar correctamente el worktree con Git.

El ejecutable DEV se identifica por su nombre `Nokto.Dev.exe` mediante `BuildIdentity.IsDevelopment`. El script de construcción crea `portable.lock`, por lo que los datos se guardan en el `data/` **junto al ejecutable DEV** y no en los datos de la instalación estable. En modo instalado, el resolvedor puede usar `%APPDATA%\Nokto`; en modo portable, `<carpeta del ejecutable>\data`.

**Reglas de seguridad del ambiente:**
1. Trabajar únicamente sobre `feature/nokto-2-dev`; comprobar `git status --short --branch` en **ambos** directorios antes y después.
2. Ejecutar tests que creen datos, alarmas o ficheros en **copias temporales del portable**, no en datos personales o rutinas reales.
3. No terminar procesos ni modificar el audio, la reproducción, los monitores, las tareas ni el sistema del usuario sin motivo y validación específicos.
4. No hacer `merge`, `push`, crear etiquetas, releases o instaladores de distribución sin aprobación expresa.
5. El script DEV **no publica GitHub ni instala Windows**. Si el portable se está ejecutando, su salida va a `staging` para no sobrescribir el EXE en uso.
6. Mantener por separado la configuración estable, las pruebas DEV y los artefactos de publicación.

## 3. Contexto de Nokto original: qué se debe preservar

Nokto es una consola Windows **offline-first**. Sus capacidades existentes incluyen el dashboard con telemetría CPU/RAM/GPU/red/batería; audio de entrada/salida mediante Windows/WASAPI, volumen y mute; atajos globales; tareas manuales y rutinas encadenadas, disparadores y ejecución concurrente; control de monitores, bloqueo/suspensión y mecanismos de cancelación; integración LAN local para control remoto y evidencias; auditoría y capturas; apariencia, idioma ES/EN, bandeja y comprobación de versiones.

La isla debe **reutilizar los servicios, adaptadores y modelos ya presentes** siempre que sea posible, sin duplicar sondeos, canales de audio, procesos, temporizadores o copias de preferencias. El gestor principal mantiene las configuraciones complejas; la isla ofrece **acciones rápidas y estados compactos**. No se pretende mantener un segundo backend o un segundo producto instalable.

La documentación histórica de la versión estable permanece en [README.md](README.md), [README.es.md](README.es.md) y [CONTRIBUTING.md](CONTRIBUTING.md). Se mantiene la licencia MIT del repositorio. **No asumir que las afirmaciones históricas «100 % offline» se mantienen literalmente** cuando el módulo de clima opt-in usa servicios externos de Open-Meteo.

## 4. Tecnología y estructura

| Capa | Ubicación y responsabilidad |
|---|---|
| Solución | `Nokto.sln`, .NET 8 |
| Núcleo | `src/Nokto.Core/` — modelos, persistencia, motor de rutinas, productividad, clima, serialización y control remoto |
| Integración Windows | `src/Nokto.Platform.Windows/` — audio, multimedia Windows, sistema, hardware |
| UI | `src/Nokto.UI/` — Avalonia 11.1.3, MVVM CommunityToolkit, tema y vistas |
| Ojos | `src/Nokto.UI/Controls/NoktoEyesControl.cs` y `NoktoEyesControl.Art.cs` |
| Isla flotante | `src/Nokto.UI/Views/IslandWindow.axaml` y archivos parciales `IslandWindow.*.cs` |
| Ajustes propios | `IslandSettingsView.axaml`, `IslandViewModel.cs`, `IslandViewModel.PagePreferences.cs` |
| Configuración | `IslandSettings` dentro de la configuración persistida del programa |
| Productividad | `IslandProductivityEngine`, `IslandProductivityState`, `IslandProductivityViewModel`, `ProductivityManagerView` |
| Clima | `IslandWeatherService` y `IslandWindow.Weather.cs` |
| Alertas | `WindowsBalloonNotifier` y avisos visuales/acústicos locales |
| Release | `.github/workflows/release.yml`, `build/inno-setup/nokto-setup.iss` |
| Pruebas | `tests/`, `build/smoke-*.ps1`, `build/capture-*.ps1` |

Objetivos y limitaciones: Windows 10 build 19041+ y Windows 11 x64; `Nokto.UI.csproj` aún tiene versión de ensamblado `1.0.1` heredada. **No se ha fijado ni publicado todavía un ensamblado público 2.0.0**. La versión DEV se distingue por identidad de ejecutable, no por un instalador público 2.0.

Dependencias de UI principales: Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Fonts.Inter, CommunityToolkit.Mvvm, SkiaSharp y QRCoder. El paquete `Microsoft.Data.Sqlite` también aparece en el proyecto del núcleo. El portable es una publicación autocontenida `win-x64` de archivo único, con bibliotecas nativas integradas.

## 5. Decisiones de producto cerradas

- La decisión de usar isla/dashboard/ambos/bandeja **pertenece al usuario** y se guarda en una pestaña independiente llamada **Isla Dinámica**, junto a Ajustes. No se exige iniciar con la isla.
- Se mantiene **una sola aplicación Nokto** y, cuando corresponda, **una distribución pública** con instalador y portable. `Nokto.Dev.exe` es solo para desarrollo.
- Galería de diseños **seleccionable por clic**, no obligar a recorrer un ComboBox uno por uno. Puede filtrarse por familia.
- La isla puede mostrarse **sin nada en la cápsula** (modo `None` / «Sin indicador»), con punto luminoso/oscuro u ojos; si está vacía debe continuar siendo interactiva.
- El **reloj no debe aparecer siempre** de forma obligatoria: opciones expandir/siempre/nunca; por defecto solo al expandir.
- **Tamaño configurable** al 80–140 % y animaciones adaptables al monitor activo, incluido 240 Hz; es un objetivo, no una promesa de FPS reales.
- En reposo la cápsula debe ser discreta y no ejecutar animación continua. La expansión debe ser suave y los controles accesibles.
- La isla tiene páginas **Resumen, Sonido y Accesos**; la rueda puede navegar, pero **no debe marear, cambiar repetidamente de sección ni interferir con el volumen**. Hay flechas como alternativa.
- Configuración independiente **por página** y **por elemento**: el usuario puede ocultar los botones «Nokto» y «Ocultar», y seguir mostrando la barra/botones de sonido.
- **Productividad y rutinas se gestionan en el dashboard** (`Rutinas → Productividad`), no mediante formularios extensos en la isla. La isla solo abre el gestor, muestra avisos y, si se elige, un indicador compacto.
- Explorador, carpetas, archivos, portapapeles y clima son funciones voluntarias. Nunca leer el portapapeles sin pulsación explícita.
- El lenguaje visual se inspira en la interacción de Apple Music, pero ha de ser **original, premium, coherente, intuitivo y aprobado tras revisión real**.
- Implementar por fases; **validar cada resultado** antes de considerarlo definitivamente aprobado. No publicar ni modificar la rama estable de forma implícita.


## 6. Estado por fases (corte al 26-10-2026)

**Leyenda:** ✅ base implementada y con pruebas; 🟡 integrada pero por perfeccionar/validar; ⬜ pendiente. Las fases no se cierran solo porque las unidades o el build pasen.

| Fase | Objetivo | Situación real |
|---|---|---|
| 0 | Preparar Portable Dev aislado | ✅ Entorno/worktree/identidad/configuración de pruebas y script |
| 1 | Modos de inicio, núcleo, eficiencia | 🟡 Cuatro modos funcionales; memoria de «solo isla» todavía alta |
| 2 | Isla flotante, transiciones e interacción | 🟡 Motor funcional, escalado y rueda; rendimiento visual y DPI por certificar |
| 3 | Ojos, minimalismo, galería y personalidad | 🟡 42 diseños + «Sin indicador»; falta aprobación estética y pulido |
| 4 | Windows/sonido/batería/red/accesos rápidos | 🟡 Controles básicos operativos; compatibilidad hardware/estados pendientes |
| 5 | Reproducción multimedia Windows | 🟡 Sesiones, carátula, pista, controles; faltan casos difíciles y validación manual |
| 6 | Herramientas de productividad | 🟡 Motor local y gestor funcionales; complementos de productividad pendientes |
| 7 | Archivos, carpetas, clima, privacidad, notificaciones | 🟡 Funciones incorporadas; **no cerrada por QA de notificación y entorno real** |
| 8 | Ajustes, idiomas y accesibilidad | 🟡 Ajustes granulares/ES-EN presentes; traducciones y navegación accesible por cerrar |
| 9 | Optimización y QA integral | ⬜ No completada; prioridad alta |
| 10 | Empaquetado, migración, actualización, publicación | ⬜ No iniciada como lanzamiento público |

### 6.1. Isla, animaciones y tamaño (fases 1–2)

- Ventana de isla superior flotante, independiente del dashboard pero dentro del mismo proceso Nokto. Se puede expandir por puntero/clic según preferencia; escala cápsula y contenido a 80–140 %.
- Animación demand-driven: transición y cambios contextuales cuando corresponde, evitando bucles de alta frecuencia al permanecer quieta. Opciones de movimiento reducido, autoexpansión, parpadeos y movimiento ocular.
- Tasa elegible `Auto / 60 / 120 / 144 / 165 / 240`; en Auto se lee el modo activo del monitor. Se observó una pantalla con 240 Hz; **configurar 240 FPS objetivo no prueba 240 cuadros renderizados por segundo**.
- Cuatro modalidades de inicio: `Dashboard`, `Island`, `Both`, `Tray`; detección de configuración anterior y argumentos `--tray` / `--silent` para pruebas.
- Reloj configurable y ocultable, estados compactos de canción y temporizador solo si el usuario opta por ello.
- **Pendientes:** medir fluidez con 60/120/144/165/240 Hz reales; arrastre/reversión de la expansión, 100 ciclos de hover, pérdida de foco, fullscreen, recuperación tras suspensión, varios monitores, escalas DPI, accesibilidad y consumo GPU. Reducir RAM.

### 6.2. Apariencias, ojos y galería (fase 3)

- Catálogo de **42 diseños vectoriales** distribuidos en familias `Minimal`, `Nokto`, `Cyber`, `Anime`, `Creature`, `Cute`, `Dark`; se agrega **43.ª opción «None / Sin indicador»** (sin ojos ni punto).
- Galería con tarjetas clicables, selección visual, filtros por familias y previsualización; no depender de recorrer una lista lineal.
- Movimientos: pupilas que reaccionan al cursor, interpolación, parpadeos no mecánicos y sueño/inactividad según ajustes. Los estilos simples evitan efectos innecesarios.
- Se crearon ornamentaciones específicas vectoriales sin recursos gráficos de franquicias ni imágenes de terceros.
- **Pendiente importante:** aprobación real de la estética, mayor diferenciación de estilos a tamaño compacto, consistencia día/noche, expresión natural, efectos contextuales por música/alarma, accesibilidad y alta DPI. No confundir «42 estilos programados» con «42 diseños aprobados».

### 6.3. Controles Windows, páginas y personalización (fase 4 + ajustes de fase 8)

**Las páginas configurables son:**

| Página | Contenido configurable |
|---|---|
| Resumen | Reproductor, carátula, batería, estado de red, clima por ciudad, información compacta habilitada |
| Sonido | Barra de volumen maestro, +/- y botones de mute de micrófono y salida (independientes) |
| Accesos | Acceso a Rutinas, Explorador, carpetas personalizadas, archivos locales y portapapeles manual |

- Cada página puede habilitarse/deshabilitarse; hay un fallback de navegación a Sonido si todas se desactivan, que requiere prueba UX específica.
- El pie de la isla permite **habilitar por separado** «Nokto» y «Ocultar»; ambos pueden estar ocultos sin eliminar controles superiores.
- Hay flechas y rueda; en la página Sonido se intercepta la rueda sobre controles y panel para evitar modificaciones accidentales del slider o cambios de página. También existe limitación temporal entre movimientos de rueda.
- Se reutiliza el volumen y mute ya soportado por el adaptador Windows. No se debe implementar otro controlador de sonido duplicado.
- **Pendientes:** indicar visualmente el estado real de mute/deshabilitado, pulir símbolos e iconos vectoriales, garantizar que navegación/foco permitan volver de un único panel activo y verificar rueda con ratones/touchpads de distintas resoluciones; configuración sensible a DPI.

### 6.4. Multimedia Windows (fase 5)

- `WindowsMediaSessionService` accede a sesiones multimedia de Windows (GSMTC): título, artista, reproducción/pausa, pista anterior/siguiente, duración/progreso y carátula cuando el reproductor lo proporciona.
- Integración basada principalmente en eventos. Servicio conectado solo cuando la isla y la funcionalidad lo requieren. Carátula limitada a tamaño de memoria de entrada; progreso actualiza al estar expandida, reproduciendo y en Resumen.
- Interfaz de reproductor con carátula, título y controles propios, inspirada en una distribución limpia tipo Apple Music.
- **Verificado:** Windows detectó una sesión real y la carátula; UI capturada visualmente. **No se probaron destructivamente todos los botones contra la música activa del usuario**.
- **Pendiente:** permisos/limitaciones por reproductor, varios reproductores abiertos, cambios de sesión, canciones sin carátula, barra de búsqueda interactiva de reproducción, estados de buffering, teclado, rendimiento de miniaturas y letras (solo fuentes con autorización/derechos adecuados). La compatibilidad no está garantizada con cada app.

### 6.5. Productividad en el gestor de Rutinas (fase 6)

- Núcleo `IslandProductivityEngine` / modelo persistido `IslandProductivityState`, separado del render. Temporizador de 5/15/25 minutos, pausa y continuación; cronómetro; Pomodoro con enfoque 1–180 minutos y descanso 1–60.
- El cambio enfoque/descanso **no inicia automáticamente el período siguiente**: lo decide el usuario.
- Alarmas únicas y diarias en hora local (`HH:mm`), hasta 12; tareas hasta 30 con texto de 120 caracteres y recordatorios a 15/60 minutos.
- Alertas visuales en isla y aviso sonoro de Windows configurable; preferencias guardadas localmente, tiempos de vencimiento almacenados como UTC.
- Interfaz administrativa dentro de `Rutinas → Productividad`, junto al gestor de rutinas preexistente; botón desde la isla abre ese gestor. La isla **no debe crecer con formularios de productividad**.
- Pruebas del motor: vencimiento, pausa, continuidad, Pomodoro, alarmas diarias, recordatorios y JSON; también se probó desde gestor crear una tarea y un temporizador en copia temporal.
- **Limitación:** ni el sistema actual despierta Windows suspendido, ni muestra alertas mientras Nokto está cerrado; debe comunicarse claramente. Revisar horarios de verano/reloj del sistema, avisos al regresar de suspensión y múltiples tareas/alertas simultáneas.
- **Pendientes de alcance:** notas persistentes, hidratación, agenda/calendario, gestión más rica de alarmas/tareas, notificación real y accesibilidad. Algunas ideas (Google/Outlook, letras, servicios externos) son **opcionales no aprobadas para la release**.

### 6.6. Archivos, carpetas, clima, privacidad y notificaciones (fase 7)

- **Carpetas:** hasta ocho accesos locales añadidos explícitamente por selector; abrir en Explorador, quitar acceso sin borrar carpeta. Botón para lanzar Explorador de Windows.
- **Bandeja de archivos:** hasta seis rutas locales, selector del sistema, abrir con app predeterminada al pulsar, quitar solo la referencia. No se escanean ni suben contenidos.
- **Portapapeles:** apagado por defecto; lectura **solo bajo pulsación**, previsualización limitada y efímera, sin historial ni lectura en segundo plano. Se borra vista al salir/cerrar o entrar en privacidad.
- **Clima:** `IslandWeatherService` usa **Open-Meteo** (geocodificación de nombre de ciudad y consulta de temperatura/humedad/código) solo si está habilitado y se proporcionó una ciudad. Acepta `Ciudad, País`; caché de 30 minutos, renovación manual y cancelación. **No utiliza GPS ni ubicación automática**. Esto **sí requiere internet**: revisar aviso de privacidad, errores de red, límites de servicio, datos compartidos con proveedor y disponibilidad.
- **Notificaciones Windows:** se incorporó `WindowsBalloonNotifier`, aviso opcional por icono de notificación/bandeja al activarse productividad. **No afirmar que son toast moderno/Action Center con entrega garantizada**: dependen de restricciones del shell y aún **falta una prueba end-to-end de un aviso visible real**.
- **Modo privacidad:** oculta datos sensibles de medios, clima, portapapeles, listas/rutas y estado de red. Revisar exhaustivamente compartición de pantalla, imágenes en caché y retorno a estado normal.
- **Pendiente:** comprobar picker con teclado, rutas inexistentes/protegidas, caducidad cache, duplicados, desconexión Wi-Fi, rendimiento, notificaciones reales, privacidad y opciones opcionales de drag-and-drop.


## 7. Configuración, persistencia y compatibilidad

Los datos principales de la instalación portable se encuentran en:

```text
Nokto-Dev-Portable-x64/
├── Nokto.Dev.exe
├── portable.lock
└── data/
    ├── config.json      # preferencias, Isla Dinámica, productividad
    ├── presets.json     # configuración de presets/rutinas
    ├── audit.jsonl      # auditoría local
    └── snapshots/       # evidencias cuando apliquen
```

Los servicios `StorageResolver` y `PersistenceService` deciden dónde guardar, usan serialización `System.Text.Json` y un contexto JSON tipado. La configuración de la isla admite valores por defecto de forma que instalaciones antiguas con datos incompletos puedan continuar abriendo.

**Principios para cambios de esquema:**
- No borrar ni resetear `config.json` de usuarios al agregar campos. Probar configuraciones vacías, anteriores, parcialmente corruptas y valores fuera de rango.
- Crear migraciones explícitas y documentadas si cambia la estructura, las rutas o el significado de un campo. No asumir que leer JSON sin excepción equivale a migración correcta.
- Conservar rutinas, tareas, alarmas, presets, atajos, temas, estado de inicio y registros de usuario. Hacer copia de seguridad antes de actualizaciones destructivas.
- La app instalada y la portable **no deben pisarse los datos** accidentalmente; concretar comportamiento deseado de sincronización/actualización en la fase 10. No mezclar carpeta de DEV con carpeta estable.
- El modo privacidad visual **no equivale a cifrado de rutas/configuración en disco**. La preferencia de ciudad y las rutas de archivos/carpetas se guardan en datos locales cuando el usuario las activa.

## 8. Compilación y trabajo diario en DEV

**Requisitos:** Windows 10 (19041+) / Windows 11 x64 y SDK .NET 8, PowerShell. El SDK del equipo puede estar en `%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe`; el script local detecta alternativa en `PATH`.

Antes de cualquier trabajo:

```powershell
git -C "C:\Users\jorge\Proyectos\Nokto-v2-dev" status --short --branch
git -C "C:\Users\jorge\Proyectos\Nokto" status --short --branch
git -C "C:\Users\jorge\Proyectos\Nokto-v2-dev" worktree list
```

Construir **solo Portable Dev** desde PowerShell:

```powershell
cd "C:\Users\jorge\Proyectos\Nokto-v2-dev"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\build\build-dev-portable.ps1"
```

Salida prevista: `artifacts\Nokto-Dev-Portable-x64\Nokto.Dev.exe`, o `artifacts\Nokto-Dev-Portable-staging\Nokto.Dev.exe` cuando otro `Nokto.Dev.exe` esté abierto. **No asumir que recompilar en staging actualiza automáticamente el ejecutable abierto**: cambiarlo solo tras cerrarlo, verificar rutas y respaldar datos.

Ejecución manual en ambiente autorizado:

```powershell
.\artifacts\Nokto-Dev-Portable-x64\Nokto.Dev.exe
# Para un arranque de prueba en bandeja:
.\artifacts\Nokto-Dev-Portable-x64\Nokto.Dev.exe --tray
```

**Precaución:** abrir una instancia de prueba con datos permanentes puede ejecutar temporizadores, mostrar avisos o rutinas ya configuradas; preferir copia temporal con `portable.lock` y `data\config.json` sintético. Las pruebas con comandos de audio o energía deben evitar afectar al equipo.

## 9. Pruebas disponibles y evidencia

| Herramienta | Responsabilidad |
|---|---|
| `tests/Nokto.ConsoleTest/` | Suite principal/histórica de la app |
| `tests/Nokto.TrayTest/` | Comportamiento bandeja |
| `tests/Nokto.DisplayProbe/` | Monitor/frecuencia y catálogo de apariencias |
| `tests/Nokto.MediaProbe/` | Lectura de sesiones multimedia de Windows |
| `tests/Nokto.ProductivityProbe/` | Cálculos de temporizador, cronómetro, Pomodoro, alarmas, tareas y JSON |
| `tests/Nokto.WeatherProbe/` | Clima con `HttpClient` simulado, ciudad/país, errores, caché y refresco |
| `build/smoke-dev-portable.ps1` | Arranque y aislamiento del portable DEV |
| `build/smoke-island-dev.ps1` | Preferencias, galería, multimedia, modos de inicio, FPS |
| `build/smoke-island-pages-qa.ps1` | Resumen/Sonido/Accesos, navegación y rueda |
| `build/smoke-productivity-ui.ps1` | Regresión de productividad desde gestor |
| `build/capture-*.ps1` | Capturas visuales en `artifacts/qa-*.png` (artefactos ignorados por Git) |

Ejemplo de ejecución no destructiva sobre el árbol DEV:

```powershell
cd "C:\Users\jorge\Proyectos\Nokto-v2-dev"
dotnet build .\src\Nokto.UI\Nokto.UI.csproj -c Release --nologo
dotnet run --project .\tests\Nokto.ProductivityProbe\Nokto.ProductivityProbe.csproj -c Release
dotnet run --project .\tests\Nokto.WeatherProbe\Nokto.WeatherProbe.csproj -c Release
dotnet run --project .\tests\Nokto.DisplayProbe\Nokto.DisplayProbe.csproj -c Release
```

**Última evidencia conocida (26-10-2026):**
- Build Release/portable DEV: **sin errores ni advertencias** en ejecuciones registradas.
- Automatización de Ajustes: selección por clic, «Sin indicador», tamaño 130 %, multimedia, bandeja, reloj y 60/240 FPS validados.
- Capturas en páginas a 100 %: Resumen `332×330`, Sonido `332×287` y Accesos `332×402` durante la prueba de esa configuración. Son tamaños contextuales **no medidas contractuales**.
- Prueba de rueda en Sonido: **no cambió de página ni modificó volumen**; no demuestra que todo touchpad funcione igual.
- Productividad: cálculos y persistencia pasaron; clima: proveedor simulado, caché y selección `Ciudad, País` pasaron.
- Una prueba anterior confirmó cuatro arranques (Island, Dashboard, Both, Tray), pero **debe repetirse íntegra contra el último commit** antes de declarar aprobada la regresión final.
- Notificación visual nativa de Windows **todavía no se verificó en uso real**. Clima debe verificarse conectado al proveedor real, sin deducir éxito solo de tests con respuestas simuladas.
- Capturas disponibles en `artifacts/qa-island-*.png`, `qa-settings-grid.png`, `qa-manager-productivity.png`. No son recursos públicos de producto.

**Observación de rendimiento:** en pruebas de arranque se registraron aproximadamente **300–360 MB de RAM según modo/instancia** (por ejemplo, ~308 MB solo isla en una regresión); son medidas puntuales, no un benchmark normalizado. La RAM se considera **problema abierto** frente al objetivo de una isla ligera. La supuesta fluidez a 240 Hz debe certificarse con mediciones efectivas CPU/GPU/FPS y no solo con que el sistema detecte 240 Hz.

## 10. Riesgos y deuda técnica priorizada

| Prioridad | Riesgo / pendiente | Aceptación esperada |
|---|---|---|
| P0 | Consumo de memoria elevado en solo isla | Establecer presupuesto y mediciones reproducibles de arranque, reposo y uso activo; reducirlo |
| P0 | Regresión de Nokto estable al integrar 2.0 | Suite completa, regresión de rutinas, audio, actualizador, bandeja, LAN y compatibilidad de config |
| P0 | Pérdida de datos en migración portable/instalado | Respaldos, migración reversible, prueba de upgrade/downgrade y rollback |
| P0 | Notificación real no validada | Comprobar avisos visibles con Windows, foco asistido, bandeja, app oculta y sin permisos |
| P1 | Fluidez, hover, rueda y accesibilidad | Medir FPS/latencia, touchpad, interacción teclado, navegación con un panel activo, sin mareos |
| P1 | Compatibilidad DPI, multimonitor y fullscreen | QA en 100/125/150/200 % escala, 60/240 Hz, resoluciones y segunda pantalla |
| P1 | Validación estética premium | Revisión con usuario de estados vacío/activo/audio/accesos, consistencia día/noche, iconos |
| P1 | Multimedia en múltiples apps | Verificar play/pausa/siguiente/anterior, carátulas, timeline, sesiones cambiantes, errores |
| P1 | Notificaciones y alarmas en suspend/reinicio | Aclarar límites, prueba DST, reprogramación, avisos atrasados y múltiples alertas |
| P1 | Privacidad y seguridad | Clima opt-in, datos visibles al compartir pantalla, secretos y rutas, acceso remoto existente |
| P2 | Localización completa | Eliminar textos de interfaz hardcodeados ES en versiones EN; textos, accesibilidad, tooltips |
| P2 | Nuevas ideas opcionales | Notas/hidratación/agenda, Outlook/Google y letras **solo tras evaluación y aprobación separadas** |

La prioridad P0/P1/P2 es **recomendación de planificación** a validar con el usuario, no una promesa de calendario.


## 11. Plan de continuación recomendado (orden de trabajo)

### Tramo A — Cierre funcional real de Fase 7
1. Probar aviso de una alarma/timer real en Windows con la isla activa, minimizada, solo bandeja, modo privacidad y notificaciones activadas/desactivadas. Registrar captura, resultado y limitación de `WindowsBalloonNotifier`; decidir si hacen falta notificaciones toast modernas con identidad de aplicación.
2. Probar consulta real de clima con ciudad introducida por el usuario, estado desconectado y recuperación de red; nunca activar ubicación automática sin consentimiento.
3. Probar portapapeles sensible y rutas locales seleccionadas: no lectura sin pulsación, no visualización en modo privacidad, rutas eliminadas/inaccesibles y selector mediante teclado.
4. Repetir regresión de los cuatro arranques y de todas las opciones por página con la última compilación. Corregir defectos detectados. Recién entonces cerrar Fase 7 como **validada**.

### Tramo B — Cierre UX/visual de fases 2, 3, 4, 5 y 8
1. Sesión de aprobación visual con usuario: ojos, modo vacío, controles de audio, reproductor, navegación, pie opcional y galería en día/noche.
2. Diseñar y documentar estados visuales: normal, hover, press, disabled, mute on/off, reproducción/pausa, alarma, error de red, actividad y reposo. Sustituir emojis/símbolos inconsistentes por iconografía vectorial propia cuando beneficie a la interfaz.
3. Verificar que el scroll de ratón/touchpad en Sonido **no provoca mareo, saltos, reinicios de expansión ni cambios accidentales de volumen**. Navegación con flechas y teclado siempre disponible.
4. Revisar configuración **por página y por control**, incluida ausencia de botones inferiores, ocultación de una o dos páginas y caso sin páginas seleccionadas.
5. Confirmar traducciones ES/EN, contraste, tamaños de fuente y lectores de pantalla. Afinar minimización y fullscreen.

### Tramo C — Fase 9: optimización y QA
1. Medición comparativa de ejecutable estable y DEV con idéntico perfil: RAM privada/working set, CPU reposo, GPU, handles, arranque y latencia (solo dashboard, solo isla, ambos, bandeja).
2. Analizar qué servicios se inicializan aunque la isla esté compacta; cancelar timers/eventos no usados. No duplicar servicios gráficos, telemetría ni medios; liberar carátulas/caché.
3. Medir transiciones y refresh real bajo 60, 120, 144, 165, 240 Hz y modo Auto. Adaptar tasa a la pantalla y respetar movimiento reducido.
4. Pruebas de larga duración, suspensión/reanudación, pérdida de monitor, cambio de sesión Windows, cierre forzoso y reinicio.
5. Ejecutar regresión completa de rutinas, acciones destructivas con simuladores, actualizaciones, LAN, telemetría, audio y compatibilidad con datos previos.
6. Registrar resultados reproducibles, defectos, cambios y aprobación en MD. No ocultar pruebas faltantes tras «build successful».

### Tramo D — Fase 10: preparación y publicación (solo con aprobación)
1. Congelar alcance de 2.0 y checklist de aceptación firmado por el usuario.
2. Respaldar archivos y verificar migración de configuración **estable → versión 2.0**, para portable e instalado; probar rollback.
3. Corregir metadatos de versión (actualmente `Nokto.UI.csproj` conserva `1.0.1`), ensamblado, changelog, textos de descarga, documentación y captura de novedades.
4. Revisar flujo de GitHub Actions `.github/workflows/release.yml`, Inno Setup y compatibilidad con el mecanismo de actualización integrado de Nokto. **No suponer que un solo tag resuelve automáticamente todos los métodos de actualización.**
5. Verificar qué distribución espera cada usuario: el workflow actual crea ZIP portable e instalador; el README estable describe además EXE portable directo. **Alinear nombres/artefactos/URLs con el actualizador real antes de publicar.**
6. Probar desde versiones previas: actualización de instalación y portable; conservación de datos, permisos, rutas, accesos, reinicio de aplicación, antivirus/SmartScreen, descarga incompleta, rollback.
7. Generar release candidate local, pruebas manuales de usuario, comparación hash/integridad, notas de versión y backup final.
8. Recién tras autorización: integrar DEV en `master` mediante revisión y estrategia Git, crear `v2.0.0` (o etiqueta aprobada) y publicar en GitHub. Supervisar actualización inicial y plan de retirada/corrección.
9. Mantener dev y estable separados hasta confirmar que los usuarios actualizan correctamente. **No borrar el worktree como parte automática del lanzamiento.**

## 12. Publicación: qué funciona y qué todavía NO está garantizado

El repositorio contiene `.github/workflows/release.yml`, que puede activarse con etiquetas `v*.*.*` y publica artefactos de release GitHub; construye ejecutable portable autocontenido, genera un ZIP y utiliza Inno Setup para el instalador. La versión estable también tiene mecanismos propios de comprobación y actualización.

**Pero al 26-10-2026:**
- La 2.0 **no tiene release, instalador público ni etiqueta de publicación**.
- La construcción local `build-dev-portable.ps1` **no es el pipeline de release de producción**.
- Debe verificarse la actualización automática tanto del canal instalado como del portable; no hay evidencia suficiente para declararla ya resuelta para v2.
- El DEV no debe autoactualizarse hacia una versión pública ni sobrescribir los datos de la estable. Revisar la identidad y los canales de actualización en QA.
- El repo estable en `master` se revisó limpio respecto a estas implementaciones.
- No se debe crear un segundo instalador permanente o mantener una «Nokto Island» como app distinta: todo formará parte del mismo paquete final.

### Criterios de salida / definición de «2.0 lista»

- [ ] Diseño visual y experiencia premium aprobados en portátil/escritorio, día/noche y tamaños 80–140 %.
- [ ] Inicio Dashboard/Island/Both/Tray y comportamiento tras reiniciar Windows aprobados.
- [ ] Selección de ojos, modo sin indicador, reloj, página, rueda, botones ocultables y privacidad aprobados.
- [ ] Sonido, multimedia y acciones de Windows probadas sin alteraciones involuntarias.
- [ ] Gestor de productividad y avisos reales, persistencia, suspensión y límites comunicados.
- [ ] Clima, rutas, portapapeles y notificaciones validados en escenarios reales y sin red.
- [ ] Regresión de Nokto estable, seguridad, LAN, rutinas y sistema aprobada.
- [ ] RAM/CPU/GPU/FPS medidos y optimizados frente a presupuesto acordado.
- [ ] Localización ES/EN, accesibilidad, DPI/multimonitor y rendimiento de batería aprobados.
- [ ] Migración y actualización portable/instalado probadas con respaldo y rollback.
- [ ] Versionado, documentación, changelog, integridad, nombres de artefactos y release candidate correctos.
- [ ] Autorización explícita del usuario para merge, publicación y activación de actualizaciones.

## 13. Registro de hitos Git (trazabilidad)

| Commit DEV | Hito |
|---|---|
| `ebe1727` | Worktree y portable DEV aislado |
| `388f0e9` | Isla independiente y modalidades de inicio |
| `1e7b511` | Interacción animada y reposo compacto |
| `2fde267` | Detección de frecuencia del monitor |
| `a8035f3` | Catálogo de 42 ojos y animaciones |
| `16821fb` | Pulido ocular, reloj y controles de sistema |
| `236f428` | Sesiones multimedia Windows y carátula |
| `81e6bfb` | Temporizadores, Pomodoro, alarmas, tareas |
| `16f766d` | Galería por clic, escala, archivos |
| `d432fb2` | Modo vacío, rueda/páginas, carpetas, gestor |
| `ab2f256` | Paneles premium, configuración granular, clima y notificación opt-in |

Los hashes se verificaron en la rama `feature/nokto-2-dev` al generar este documento. Los hitos describen **commits existentes**, no niveles de aceptación de lanzamiento.

## 14. Procedimiento para continuar en una sesión nueva

1. Abrir y leer **este archivo completo**, [ROADMAP_2.0.md](ROADMAP_2.0.md) y, para cada fase afectada, el código de la funcionalidad. El roadmap conserva anotaciones históricas que pueden haber quedado superadas por iteraciones posteriores; **priorizar el estado del código verificado y las aclaraciones de este documento**.
2. Preguntar al usuario solo si hace falta una decisión nueva de producto; **no repetir decisiones ya tomadas**. Al iniciar una implementación explicar qué se va a modificar y qué se revisará después.
3. Comprobar ambos `git status` y worktrees. Jamás implementar en `master` antes de publicación aprobada.
4. Tomar **una fase o defecto verificable por vez**, hacer cambios minimizados, compilar, probar en copia temporal y revisar visualmente antes de avanzar. Se puede trabajar en frentes paralelos cuando lo autorice el usuario, registrándolos.
5. Conservar derechos del usuario: si la prueba toca música, volumen, rutinas, archivos privados o energía, no ejecutar acciones reales sin motivo/permiso. Conectar servicios externos solo cuando el usuario los habilite.
6. Documentar cada avance y regresión con fecha, archivos, pruebas, resultados y riesgos; actualizar este MD y el roadmap tras cada cierre aprobado.
7. No decir «terminado» si solo existe código. Clasificar `Implementado`, `Probado localmente`, `Pendiente de validación`, `Aprobado por usuario`, `Publicado` de manera separada.
8. **Siguiente paso concreto recomendado:** verificar la notificación real (P0), cerrar QA de Fase 7 y corregir el alto uso de RAM, mientras se revisa estéticamente la UI premium por página.

## 15. Referencias internas y externas

- [ROADMAP_2.0.md](ROADMAP_2.0.md): historial del alcance por fases y decisiones técnicas acumuladas.
- [README.es.md](README.es.md): producto original en español y capacidades estables.
- [README.md](README.md): producto original en inglés, distribución y arquitectura.
- [CONTRIBUTING.md](CONTRIBUTING.md): requisitos y comandos base.
- `.github/workflows/release.yml`: build y publicación GitHub del canal público.
- `build/inno-setup/nokto-setup.iss`: instalador del proyecto.
- Apple Human Interface Guidelines / Live Activities: https://developer.apple.com/design/human-interface-guidelines/live-activities
- Apple ActivityKit: https://developer.apple.com/documentation/ActivityKit/displaying-live-data-with-live-activities
- Open-Meteo (clima de uso voluntario): https://open-meteo.com/
- Repositorio público de Nokto: https://github.com/jmorgadodev/Nokto

---

**Estado final de esta documentación:** refleja el proyecto y el código inspeccionados al **26 de octubre de 2026**. El documento no autoriza por sí solo publicar 2.0. Si se cambia cualquier comportamiento esencial —datos, seguridad, instalación, política de permisos, actualizaciones o fases—, actualizar este archivo y registrar la justificación y validación.
