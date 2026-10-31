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
| Único ejecutable portable DEV vigente | `artifacts\Nokto-Dev-Portable-miradas\Nokto.Dev.exe` |
| Si DEV está abierto | Cerrar desde la bandeja antes de reemplazar; no crear otra versión |
| Documento de fases | [ROADMAP_2.0.md](ROADMAP_2.0.md) |
| Repositorio remoto del proyecto estable | `https://github.com/jmorgadodev/Nokto.git` |

**Importante:** no son dos clones completamente independientes. `Nokto-v2-dev` es un **Git worktree** asociado al repositorio `Nokto`, con árbol de trabajo y rama separados pero historial/objetos Git compartidos. El archivo `.git` del DEV apunta a `Nokto/.git/worktrees/Nokto-v2-dev`. **No borrar, mover ni renombrar manualmente estos árboles** sin gestionar correctamente el worktree con Git.

El ejecutable DEV se identifica por su nombre `Nokto.Dev.exe` mediante `BuildIdentity.IsDevelopment`. El script de construcción crea `portable.lock`, por lo que los datos se guardan en el `data/` **junto al ejecutable DEV** y no en los datos de la instalación estable. En modo instalado, el resolvedor puede usar `%APPDATA%\Nokto`; en modo portable, `<carpeta del ejecutable>\data`.

**Reglas de seguridad del ambiente:**
1. Trabajar únicamente sobre `feature/nokto-2-dev`; comprobar `git status --short --branch` en **ambos** directorios antes y después.
2. Ejecutar tests que creen datos, alarmas o ficheros en **copias temporales del portable**, no en datos personales o rutinas reales.
3. No terminar procesos ni modificar el audio, la reproducción, los monitores, las tareas ni el sistema del usuario sin motivo y validación específicos.
4. No hacer `merge`, `push`, crear etiquetas, releases o instaladores de distribución sin aprobación expresa.
5. El script DEV **no publica GitHub ni instala Windows**. Si el portable está en uso, se detiene; solo sustituye el ejecutable canónico y conserva `data`. El staging de compilación es temporal y se elimina al terminar.
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
- **Clima:** `IslandWeatherService` consulta **Open-Meteo** por ciudad manual o, si se activa expresamente, por ubicación de Windows con su permiso. En el segundo caso se envían coordenadas a Open-Meteo. Acepta `Ciudad, País`; caché de 30 minutos, renovación manual y cancelación. Requiere internet; quedan pendientes pruebas reales de permiso, ubicación, conectividad y disponibilidad.
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

## 16. Iteración solicitada el 26 de octubre de 2026

- Implementado en DEV: orden intercambiable de Resumen, Sonido y Accesos en las posiciones 01–03; configuraciones anteriores conservan el orden original. Los controles propios permanecen asociados a su página.
- La rueda navega también desde Sonido, salvo sobre el deslizador de volumen; al salir físicamente de la isla se contrae aunque se haya abierto con clic o navegado.
- Botones de sonido con iconos vectoriales y estados de silencio y dispositivo no disponible; el estilo CrimsonRing incorpora iris rojo y tres marcas oscuras solicitadas por el usuario.
- Validación local: compilación Release de UI sin advertencias ni errores; pruebas existentes de clima manual y catálogo de estilos aprobadas. Aún falta revisar visualmente y comprobar interacciones reales, persistencia del orden, accesibilidad, DPI y animaciones en el equipo del usuario. No se declara aprobado el diseño.
- Ubicación automática de clima: autorización específica recibida el 27 de octubre de 2026. Implementada como opción desactivada por defecto; requiere permiso de Windows y envía coordenadas a Open-Meteo cuando se consulta el clima. Prueba con servicio simulado aprobada; pendiente verificar permiso y consulta reales. La ciudad manual sigue disponible.

### Sharingan — iteración 27 de octubre de 2026
- `CrimsonRing` conserva su identificador para configuraciones anteriores y se presenta como «Sharingan · tres tomoe». Render propio con párpados, iris, pupila y tomoe curvos; mirada y parpadeo existentes.
- Activación giratoria breve al expandir la isla y mediante botón de prueba en Ajustes. Se detiene en reposo y respeta movimiento reducido en la isla. El diseño se genera en vectores sin imágenes externas.
- Compilación y capturas estáticas fuera de pantalla a 145×68 y 74×34 verificadas. La prueba fuera de pantalla no ejecuta el bucle de eventos de una ventana: falta probar la animación y el comportamiento visual real en la sesión del usuario. La estética sigue pendiente de aprobación.

### Variantes Uchiha — iteración posterior
- Catálogo ampliado de manera aditiva a 53 apariencias y modo vacío: uno, dos y tres tomoe y variantes asociadas a Itachi, Obito/Kakashi, Shisui, Sasuke, Madara, Izuna y Sarada. Son interpretaciones vectoriales locales y no se declara que la colección sea exhaustiva ni una reproducción certificada de cada patrón.
- El patrón gira durante la activación al expandir o pulsar la cabecera de la isla; también se puede reproducir desde Ajustes. La activación respeta movimiento reducido y no mantiene un bucle continuo en reposo.
- Pruebas locales: compilación Release limpia, catálogo y render de las doce variantes a 145×68 y 74×34; con dispatcher activo se verificó un fotograma diferente durante la activación. Falta prueba interactiva en ventana real, revisión de fidelidad de cada patrón y aprobación visual del usuario.

### Ajuste de experiencia Sharingan
- Se retiraron las formas y líneas que rodeaban los iris, dejando solo los dos ojos en la cápsula. La activación de Mangekyō pasa por tomoe, giro y transición al patrón seleccionado, con destello breve; al pulsar los ojos expandidos vuelve a activarse sin contraer la isla.
- Referencias visuales consultadas: https://gifdb.com/gif/itachi-sharingan-mangekyo-switching-spinning-eyes-plrjabmph6mmzyj8.html y https://klipy.com/gifs/sharingan-mangekyou-sharingan-1. La implementación es vectorial y no incorpora esos GIF.
- Build Release y prueba del render de doce diseños aprobados; la prueba con dispatcher observa el cambio y regreso al estado de reposo. Queda pendiente validar clic y animación en la ventana real y aprobación estética.

### Despertar progresivo — revisión solicitada por el usuario
- El usuario rechazó el giro anterior por no transmitir la identidad ni la evolución del Sharingan. Se investigaron como referencias la activación de Itachi (https://gifdb.com/gif/itachi-mangekyou-sharingan-activation-w8icdjzrp5tnxhqb.html), transiciones de Mangekyō (https://www.youtube.com/watch?v=1oQIYzKHrZc) y principios de anticipación y temporización (https://learn.microsoft.com/en-us/windows/win32/lwef/animation-principles). No se incorporaron archivos de esas fuentes.
- Implementado en DEV: anticipación con cierre, aparición progresiva de uno a tres tomoe, giro acelerado/desacelerado y transición al Mangekyō elegido. Dura 3,2 s, se activa al pulsar o expandir, respeta movimiento reducido y termina en reposo; mirada y parpadeo conservados.
- Pruebas: build Release y captura de cuatro fases distintas con dispatcher activo, retorno a reposo y doce diseños diferentes. Las capturas se inspeccionaron, pero sigue pendiente revisión de animación completa y fidelidad artística en la ventana real y aprobación del usuario.

### Cantidad de ojos y reacciones por familia
- Selector de uno o dos ojos aplicable al catálogo; configuración anterior conserva dos ojos. El ajuste afecta a la vista previa y a la cápsula, y se guarda en `EyeCount`; los indicadores sin ojos conservan su función y el modo vacío permanece vacío.
- Se añadió Rinnegan de Madara y un renderer felino con pupila vertical y parpadeo lento. Los estilos comparten reacciones diferenciadas por familia y pueden probarse desde Ajustes o al pulsar los ojos; movimiento reducido se respeta.
- Build Release y pruebas fuera de pantalla: 54 estilos reaccionan con uno y dos ojos, persistencia JSON y valor anterior verificados. Esto no demuestra que las 54 animaciones tengan una personalidad individual aprobada: faltan revisión visual, uso real, accesibilidad y fidelidad de Madara.

### Seguimiento binocular y referencias de anime
- La mirada se calcula desde la posición de cada ojo hacia el puntero, con convergencia independiente, interpolación y sensibilidad existente. El modo de un ojo usa su posición central. No se añade un hook de entrada ni almacenamiento de coordenadas.
- Se añadieron interpretaciones vectoriales de Byakugan, Geass, Seis Ojos y Luna demoníaca, con reacciones propias; se conservan los identificadores y opciones previas. Referencias consultadas: https://naruto-official.com/en/anime/naruto1/list/01_270, https://geass.jp/R-geass/character.php, https://jujutsukaisen.jp/character/category6.php y https://kimetsu.com/anime/mugenjyohen_movie/character/.
- Validación local: build Release, render de 58 estilos con una y dos unidades, y pruebas matemática y de render del seguimiento independiente. Pendiente verificar el puntero en ventana real, especialmente DPI/multimonitor, y la fidelidad y aceptación artística de las nuevas apariencias.

### Centrado, contenido libre y accesos de carpeta
- La cabecera centra los ojos con una o dos unidades, y el seguimiento calcula su nueva posición central. La cápsula amplía su ancho en compacto cuando hay información lateral para no solaparla.
- Matriz por página para reproductor, batería, red, clima, volumen, micrófono, salida, rutinas, Explorador, carpetas, archivos y portapapeles. Cada elemento puede aparecer en varias páginas; seleccionar una casilla habilita su función general. El orden de páginas y las opciones existentes se conservan; las configuraciones anteriores sin matriz mantienen su distribución previa.
- La isla limita la altura del contenido y ofrece desplazamiento cuando se combinan muchos elementos. Las carpetas se abren pulsando su nombre; la X elimina exclusivamente el acceso guardado.
- Validación local: build Release; distribución, combinación, orden y persistencia probados en datos temporales; controles renderizados con ojos centrados, carpeta y volumen junto a reproductor; X verificada sin borrar la carpeta; página llena con 622 px de contenido y 430 px de área desplazable. Pendiente validar clic de apertura del Explorador y rueda/touchpad en ventana real, DPI y aceptación visual.

### Revisión de usabilidad: un ojo y módulos libres
- El usuario informó que no encontraba un ojo y que la personalización seguía sin resultar clara. Causa visual identificada: las tarjetas de galería siempre dibujaban dos ojos; además el selector quedaba separado de la galería y los títulos seguían sugiriendo páginas fijas. El portable DEV habitual encontrado seguía siendo una compilación anterior; no se demuestra qué ejecutable utilizó el usuario.
- La galería ahora refleja la cantidad elegida y tiene su propio selector Un ojo / Dos ojos, sincronizado con Apariencia. Los módulos se identifican como 01–03, con visibilidad independiente, matriz de funciones y botón para mover reproductor, volumen, micrófono y silencio de salida juntos a cualquiera de ellos; se pueden repetir mediante las casillas. El orden y opciones generales se mantienen en secciones desplegables.
- Se probaron los controles reales de Ajustes con datos temporales: selector y previsualizaciones de un ojo, casilla de volumen, botón de audio completo, combinación por página y persistencia. La comprobación es fuera de pantalla; falta validación interactiva con la instancia real y aceptación del usuario.

### Reproductor y revisión de capturas del usuario
- Las capturas del usuario muestran el menú anterior sin matriz de funciones, el ojo alineado a la izquierda y controles multimedia representados como emoji; por ello esa captura no refleja la interfaz DEV más reciente. Se observó abierta una carpeta de la compilación anterior `Nokto-Dev-Portable-miradas`; no se identificó una instancia Nokto activa ni se cambió su ejecutable o datos.
- El reproductor ahora usa iconos vectoriales centrados, etiquetas Anterior / Reproducir o Pausar / Siguiente, estado de reproducción y tiempos transcurrido y total. Las acciones no disponibles se deshabilitan y la barra de progreso sigue siendo de lectura, no un control para buscar en la canción.
- Build Release y prueba de estados multimedia simulados (reproducción, pausa, tiempo y botón no disponible) aprobados; vista renderizada inspeccionada. Matriz de módulos y centrado de uno o dos ojos con reloj comprobados fuera de pantalla. Pendiente probar una sesión multimedia y el puntero reales, rueda/touchpad y aceptación visual.

---

**Estado final de esta documentación:** refleja el proyecto y el código inspeccionados al **26 de octubre de 2026**. El documento no autoriza por sí solo publicar 2.0. Si se cambia cualquier comportamiento esencial —datos, seguridad, instalación, política de permisos, actualizaciones o fases—, actualizar este archivo y registrar la justificación y validación.

### Ajuste de reproductor y mirada — 28-10-2026
Se centraron las etiquetas, botones y metadatos del reproductor; el seguimiento calcula los centros desde el control en coordenadas de pantalla y aumenta el desplazamiento de los iris Sharingan. Compilación y pruebas de diseño y animaciones simuladas aprobadas; queda pendiente validar el cursor real y DPI en el escritorio. El portable miradas recibió el ejecutable nuevo conservando data y respaldo. La comprobación detectó el ejecutable en uso, pero el comando de reemplazo continuó por un error no terminante de PowerShell: la instancia en ejecución debe cerrarse y reiniciarse para cargarlo. En futuras actualizaciones se debe detener ante ese error y usar staging.

### Propuesta temática MAGI y EVA — 29-10-2026
Primera versión vectorial: MAGI con tres unidades iluminadas secuencialmente al expandir y al hacer clic en controles de la isla; EVA 00, 01 y 02 con uno o dos ojos, mirada y reacción. Se respeta reducir movimiento. Es un prototipo inspirado, no una reproducción completa de todas las animaciones de la serie ni un sistema real de votación; falta validación visual e interacción en escritorio. Build y prueba de reacción de 62 estilos correctos. Portable miradas actualizado con respaldo y data conservada.

### Orden de artefactos — 29-10-2026
La copia de uso habitual es artifacts/Nokto-Dev-Portable-miradas/Nokto.Dev.exe, con su data original. Las versiones anteriores y respaldos se conservaron en artifacts/Historico-portables; las capturas se trasladaron a artifacts/Capturas-pruebas. Las rutas x64 y staging siguen en su lugar por compatibilidad con scripts. Las rutas históricas a capturas y portables previos en este documento pueden referirse a su ubicación anterior.

### Limpieza de versiones — 29-10-2026
Se eliminaron los ejecutables portables anteriores a petición del usuario. La única versión portable está en artifacts/Nokto-Dev-Portable-miradas; sus datos se conservan. Se preservaron solo configuraciones anteriores en artifacts/Respaldo-configuraciones-anteriores. Las rutas antiguas a x64, staging y versiones históricas en secciones anteriores quedan obsoletas. Los scripts de desarrollo apuntan a miradas y la construcción usa una carpeta temporal, sin sustituir un ejecutable bloqueado.

### MAGI, apariencia iPhone y módulos — 29-10-2026
Se reemplazó la matriz de asignación por tres tarjetas de módulos con selección de contenido y botón para reunir audio. La compatibilidad de preferencias se conserva. MAGI pasa a un diagrama triangular ampliable, con nombres de módulos, deliberación y variante de terminal cinematográfica; iPhone añade cápsula negra, radios redondos y transición de 420 ms. Las imágenes de pruebas se generan en Capturas-pruebas.
Referencias consultadas: https://japan.norton.com/evangelion_cp/ y https://developer.apple.com/design/human-interface-guidelines/live-activities . También se buscaron referencias de The End of Evangelion y Rebuild. Las referencias de terceros se consultaron visualmente; no se descargaron ni adquirieron animaciones originales de las películas ni recursos de Apple. El código genera los gráficos localmente. No se ha verificado una equivalencia 1:1, ni se han reproducido todas las animaciones originales; la variante de cine es una interpretación y la apariencia iPhone es una adaptación para Windows.
Compilación y pruebas de configuración y reacción de 64 diseños aprobadas antes del ajuste final de estado cinematográfico. La fidelidad visual y las interacciones reales requieren validación adicional.

### Material visual MAGI/EVA y movimiento de isla — 29-10-2026
MAGI usa captura en reposo y GIF descargado de Tenor al interactuar, con los fotogramas y tiempos locales compartidos. El antiguo dibujo se eliminó. Los EVA 01 y 02 renderizan fragmentos de imágenes de referencia; EVA00 conserva geometría de sensor monocular. El movimiento de las imágenes de EVA no equivale a animación del iris original. Procedencia en Assets/Themes/SOURCES.md; material para pruebas DEV, pendiente de revisar antes de distribución pública.
El modo iPhone afecta a la ventana completa: resorte amortiguado con continuidad al retarget, radio variable, contenido con aparición gradual y cápsula flotante sin indicador decorativo. No se afirma equivalencia 1:1 con las animaciones del sistema iOS; validación real pendiente.

### Depuración de catálogo y accesos del módulo — 29-10-2026
Retirados MAGI, iPhone y EVA y los recursos relacionados, además de variaciones básicas de punto/color; quedan 29 diseños más Sin indicador. Las preferencias antiguas de estilos retirados se resuelven a Clásico. Se añaden texturas transparentes generadas para Sauron, Desdentao y T-800 con seguimiento independiente y reacciones. Prompts y referencias en Assets/Themes/SOURCES.md.
El resorte de expansión se aplica a todas las apariencias. Conserva velocidad al invertir y evita reiniciar la misma transición ante refrescos; el fondo se conserva durante la contracción hasta volver al reposo.
Explorador y Carpetas pasan a un panel independiente al principio del contenido de cualquier módulo. Prueba sintética de accesos + reproductor + audio verifica visibilidad efectiva y posición dentro del viewport. Las preferencias del usuario no se modificaron manualmente. Abrir carpetas reales, comportamiento del puntero y fidelidad visual pendientes de validación de escritorio.

### Contenido adaptable y orden por módulo — 29-10-2026
Se incorpora IslandSettings.ContentOrder por página lógica y flechas ↑ ↓ en cada tarjeta de módulo. El orden admite todos los contenidos independientemente: reproductor, volumen, micrófono, salida, batería, red, clima, rutinas, Explorador, carpetas, archivos y portapapeles. Las preferencias previas reciben el orden predeterminado; no se editan los datos de usuario manualmente.
La isla mide los contenidos activos sin límite fijo de 430 px y no permite desplazamiento vertical. Usa columnas si hace falta y ajusta la escala del contenido al espacio útil de la pantalla en combinaciones extremas. La altura objetivo usa el contenido completo y la animación existente; evita reparentar controles si el plan de distribución no cambia.
Carpetas usa ItemsControl sin selección ni scroll, tarjeta con título y Añadir, filas con nombre de apertura y X independiente. La bandeja de archivos tampoco usa scroll interno.
Validación: build sin errores; prueba UI de flechas de orden, persistencia, reproductor antes del Explorador, nombre de carpeta y X, combinación con todos los elementos y ocho carpetas, ancho adaptable y Offset.Y bloqueado en cero. Pendiente validación en escritorio con datos reales y diferentes escalas/DPI.

### Suavizado coordinado de apertura y cierre — 29-10-2026
Resorte común de frecuencia 14 y amortiguación 0,94, con captura de posición y velocidad al invertir. La aparición ya no depende de la altura final del contenido: un estado continuo de revelado gobierna opacidad de fondo, borde, reloj, controles y una traslación leve de 8 px. El contenido mantiene el ancho final durante la expansión para evitar reajustes. Salida del puntero con tolerancia de 180 ms, comprobada cada 50 ms solo mientras está desplegada. Reducir movimiento conserva su comportamiento inmediato.
Validación: compilación sin advertencias; prueba Avalonia de fondo inicialmente transparente, fotograma de opacidad intermedia, ancho estable, inversión sin apagado y cierre transparente. Siguen pasando las comprobaciones de orden por módulo y contenido sin scroll. Fluidez percibida y rendimiento en escritorio real pendientes de validación.

### Audio, alineación y localidad del clima — 30-10-2026
Micrófono y salida se reúnen en una tarjeta de dos columnas cuando ambos están activos; cada función sigue pudiendo activarse por separado. La fila se coloca en la primera posición de audio correspondiente en el orden del módulo. Explorador centra su texto; alimentación centra la información y añade un símbolo de energía.
El usuario autorizó explícitamente consultar coordenadas aproximadas en Nominatim/OpenStreetMap para resolver la localidad. La consulta usa coordenadas redondeadas a 0,01°, zoom de localidad, User-Agent identificable, caché de 30 min y separación mínima de 1 s entre consultas. El proveedor puede inyectarse en el constructor. El clima sigue consultándose en Open-Meteo. Se muestra atribución OSM; si el nombre no se resuelve, el pronóstico se conserva con coordenadas aproximadas como referencia. Los ajustes informan de ambos proveedores.
Pruebas Avalonia de posición izquierda/derecha y centrado aprobadas; pruebas HTTP simuladas de localidad, caché, atribución, coordenadas inválidas y fallo de geocodificación aprobadas. No se consultaron las coordenadas reales del usuario durante estas pruebas.

### Letras, reconocimiento preparado y avisos de sistema — 30-10-2026
El usuario pidió acercar la continuidad de animaciones a Nusa conservando ojos, colores y módulos de Nokto. Se consultó la web y su demostración pública, no el código de su aplicación nativa. Referencias: https://rama2555.github.io/nusa/ y https://github.com/rama2555/nusa. No se incorporó código ni material gráfico de Nusa.

- Letras bajo pulsación explícita: consulta título, artista y duración en LRCLIB; panel compañero con entrada/salida amortiguada, resaltado por marcas LRC, avance según posición y velocidad de Windows Media Control y salto por línea si la sesión lo permite. Cambio de pista cancela la consulta anterior; modo privado cierra las letras. Sin sincronización se indica una vista previa; también hay estados instrumental, sin letra y error. Caché acotada, tiempo máximo y respuesta limitada a 1 MB. Documentación: https://lrclib.net/docs.
- El usuario eligió reconocimiento desde micrófono y audio del PC, pero no dispone de clave: ambas fuentes quedan preparadas y la función desactivada sin clave. La ventana exige pulsar «Escuchar 8 segundos y enviar a AudD» por cada consulta. WASAPI captura exclusivamente durante la acción y mantiene el WAV en memoria; la salida del PC incluye todas las aplicaciones de la salida predeterminada. Permite cancelar captura/consulta. Clave en PasswordVault de Windows, separada por identidad DEV, nunca en el JSON de configuración. No se incluye clave ni se invocó una captura real. Documentación: https://docs.audd.io/ y https://github.com/naudio/NAudio/blob/v2.2.1/Docs/WasapiLoopbackCapture.md.
- Avisos transitorios de volumen, brillo compatible WMI, Bloq Mayús, idioma del teclado, cargador, pérdida/recuperación de Internet y conexión/cambio de salida de audio. No cambia volumen ni brillo; observa eventos y retorna al ojo a los 2,6 s. Las categorías se desactivan desde «Avisos y música». Teclado consulta solo estado de Mayúsculas e idioma activo: no registra caracteres ni historial. Dispositivos/red se ocultan en privacidad. El brillo depende de eventos WmiMonitorBrightnessEvent; no cubre monitores externos sin ese soporte. La detección de auriculares requiere que Windows los exponga como dispositivo de audio activo, no un aviso Bluetooth genérico de emparejamiento.
- Dependencias: NAudio.Wasapi 2.2.1 y System.Management 8.0.0, observadores liberados al cerrar la isla. Se sincroniza inicio/cierre para evitar suscripciones que sobrevivan a la ventana.

Validación local: compilación Release; pruebas HTTP totalmente simuladas de letras, caché, cancelación, AudD sin clave, multipart, sin coincidencia, errores y enlaces; prueba Avalonia de línea activa, destino de clic, salto no disponible, dos fuentes inactivas, HUD y caducidad con movimiento reducido, categorías y privacidad. Regresión de módulos libres, orden, centrado, audio y contenido sin scroll. Capturas sintéticas en artifacts/Capturas-pruebas. Estas pruebas no demuestran captura/reconocimiento reales, exactitud de sincronización de cada reproductor, eventos del hardware ni fluidez percibida en escritorio; requieren validación del usuario. No se grabó audio, se envió audio ni se alteró reproducción/volumen/brillo del equipo durante las pruebas.

Portable canónico actualizado el 30-10-2026 a las 03:46 (hora local), sin crear otra versión. La huella SHA-256 del conjunto de archivos de data fue idéntica antes y después del reemplazo. El árbol estable permaneció limpio en master. La aplicación del usuario no se inició ni se terminó para esta actualización.

### Reconocimiento integrado sin clave del usuario — 30-10-2026
El usuario pidió eliminar la necesidad de configurar una clave. Esto sustituye la elección previa de dejar el reconocimiento inactivo sin AudD: ahora «Sin clave · Shazam experimental» es el proveedor predeterminado. AudD queda como alternativa avanzada voluntaria; nunca se cambia de proveedor ni se sube WAV a AudD automáticamente.

Se investigó ShazamKit oficial (https://developer.apple.com/shazamkit/), que ofrece SDK de Apple/Android y requiere configuración de desarrollador para el catálogo; no es una API nativa de Windows disponible directamente para Nokto. Se implementó el camino sin clave como cliente C# no oficial del endpoint público de coincidencias de Shazam, siguiendo la referencia de https://github.com/AlekseyMartynov/shazam-for-real. Requiere Internet y depende de la disponibilidad y estabilidad de ese servicio; no se presenta como SDK oficial ni reconocimiento global offline.

- Captura explícita de 8 s desde micrófono o salida predeterminada. Abrir el panel no inicia captura ni consulta credenciales. El botón explica qué se enviará. Generación local de huella mediante FFT y picos espectrales, normalización WAV PCM/float multicanal a mono de 16 kHz. Solo se envía la firma acústica; no se envía el WAV en este modo. Sin Python, servicio local, instalador ni nueva dependencia NuGet.
- Análisis de picos adaptado de Analysis.cs y PeakFinder.cs de shazam-for-real, licencia ISC. FFT y serialización propias. Atribución/licencia incluidas como recurso del ensamblado y en ThirdPartyNotices del portable. No se ha incorporado ni usado el SDK de Apple.
- Sin ID persistente, coordenadas ni escucha continua. Cancelación de captura/análisis/consulta, respuesta limitada, timeout y estados de silencio, sin coincidencia, cuota y servicio no disponible. Sin reintentos automáticos para eludir límites. AudD solo consulta la bóveda si el usuario lo selecciona.

Verificación: compilación sin advertencias; pruebas de generación determinista, firma binaria, cancelación, silencio sin conexión, peticiones sin credenciales/WAV, formatos de respuesta y fallos simulados. PCM16 y float32 estéreo de 48 kHz normalizados con señales sintéticas. UI comprueba ambas fuentes, botón activo sin clave, cero lecturas de credenciales al abrir y AudD opcional bloqueado sin clave. Regresión de letras, HUD y módulos aprobada.

Además se envió al endpoint real una huella sintética, aceptada sin credenciales. Se descargó a una carpeta temporal la pequeña muestra pública TestData/test.mp3 y su firma de referencia, sin reproducirla. Windows Media Foundation y NLayer (usado por la referencia) difieren en padding MP3: la comparación alcanza 90,8 % de picos con alineación y tolerancia de una ventana de 8 ms. El reconocimiento real de la muestra completa y de un fragmento de exactamente 8.000 ms devolvió «Doo Uap, Doo Uap, Doo Uap (feat. Stefano di Battista)» de Gabin. Solo se enviaron huellas; no se grabó micrófono ni audio del PC, ni se leyeron archivos personales. La muestra no se incluye en el portable. Pendientes: permisos/compatibilidad de capturas reales, ruido de micrófono y validación voluntaria del usuario.

Entrega del modo sin clave: el primer reemplazo se detuvo correctamente porque el portable estaba abierto; el usuario confirmó haber salido desde la bandeja. Se actualizó después el único Nokto.Dev.exe canónico, con licencia incluida y huella de data idéntica antes/después. El árbol estable sigue limpio en master. No se abrió ni terminó la aplicación del usuario para la prueba.

### Corrección de las caídas registradas al refrescar audio — 30-10-2026
Se revisó el crash.log del portable canónico: dos InvalidCastException de las 04:17:09 y 04:17:52 (hora de Chile), en AudioDeviceProfileService.Capture → GetDefaultAudioProfile → RefreshAudioPanel. El registro corresponde a una compilación anterior al reemplazo de las 04:18, pero el defecto seguía presente en el código actual.

La prueba de solo lectura reprodujo el mismo fallo al mantener activo un MMDeviceEnumerator de NAudio y llamar a Capture: Windows reutiliza el componente COM y su RCW puede quedar asociado a la coclase administrada de NAudio. La creación tipada del módulo anterior intentaba convertir ese wrapper a Nokto.Platform.Windows.Audio.MMDeviceEnumeratorComObject y lanzaba InvalidCastException antes de consultar los dispositivos. No era un fallo de identificación de canciones ni de los datos del usuario.

Se añadió AudioCom.CreateEnumerator con CoCreateInstance solicitando directamente IMMDeviceEnumerator y conversión a la interfaz, sin convertir a una coclase propia. Se usa en captura de perfil, listas, volumen/silencio, inicialización del medidor y lectura previa al cambio de endpoint; se eliminó la coclase duplicada. No se añadió un catch genérico para ocultar el fallo. Referencia de activación: https://learn.microsoft.com/en-us/windows/win32/coreaudio/mmdevice-api; antecedente del conflicto de coclases: https://github.com/naudio/NAudio/issues/902.

Verificación: reproducción fallida antes de la corrección y exitosa después; lecturas repetidas en STA y MTA, ambos órdenes de inicio (NAudio antes/después del módulo anterior), correspondencia de IDs predeterminados cuando están disponibles y enumerador NAudio aún utilizable tras liberar las lecturas. No se ejecutaron Set, Toggle, reproducción ni grabación de audio. Build Release sin advertencias y regresiones de módulos, letras, reconocimiento y HUD aprobadas. El crash.log original se conserva como evidencia; no se borra el historial para simular la solución.

Portable canónico actualizado con la corrección de interoperabilidad de audio. Huellas SHA-256 de data y crash.log idénticas antes/después; ninguna otra versión portable creada. El árbol estable sigue limpio en master. La aplicación del usuario no fue iniciada ni terminada por la prueba.

### Pulido de audio y continuidad del menú — 30-10-2026
El usuario rechazó los botones rectangulares genéricos y el cambio de apariencia al silenciar/subir volumen. Se añadió IslandAudioButton, derivado de Button con plantilla propia: disco de 48×48, iconos vectoriales de igual trazo, micrófono y salida, tachado en silencio, tonos coral/mint, foco visible y transiciones de tinta de 150 ms. No depende de la plantilla genérica del tema. Reducir movimiento desactiva las transiciones. Nombres y estados se separan, centrados en dos columnas izquierda/derecha con divisor sutil. Comandos y disponibilidad conservados.

La presentación de avisos ya no inserta filas ni cambia geometría durante el menú abierto. Los estados de audio se actualizan en sus controles existentes. Los avisos externos siguen disponibles con la isla compacta; los no urgentes que llegan durante el menú se descartan. Se registra solo el instante de interacción sobre volumen/micrófono/salida mediante eventos de puntero/teclado, sin capturar teclas ni texto: se ignoran eventos de volumen propios durante 2 s y eventos durante los 650 ms posteriores al cierre del menú, evitando reaperturas por notificaciones WASAPI tardías. Los vistazos automáticos no activan la animación de despertar del ojo. Entrar en un vistazo lo sustituye por un menú limpio, sin duplicar la fila de aviso.

Verificación: build Release sin advertencias; prueba de menú abierto estable ante volumen, mute y teclado, conservación de página/dimensiones, evento tardío tras cierre y protección local, vistazo externo permitido en compacto y promoción limpia al menú. Prueba real de plantilla, enlaces de estado y dimensiones de 48 px; captura del bloque inspeccionada en artifacts/Capturas-pruebas/qa-island-audio-controls.png. Regresiones de módulos, centrado, reproductor, letras, reconocimiento y HUD aprobadas. No se alteró audio real; queda pendiente revisión de la interacción y fluidez percibida por el usuario en su escritorio.

Entrega del pulido: el usuario cerró Nokto desde la bandeja al detectarse el archivo en uso. Se actualizó el único portable canónico, con hashes de data y crash.log idénticos antes/después. Captura revisada y pruebas UI aprobadas; no se inició ni terminó la aplicación del usuario mediante automatización.

### Revisión completa de armonía y opciones de música — 30-10-2026
El usuario pidió analizar toda la isla y eliminar el enfoque de añadir funciones con estilos diferentes. Se revisaron conjuntamente reproductor, navegación, volumen, micrófono/salida, Explorador, carpetas, archivos, portapapeles, batería, red, clima, rutinas, pie, avisos y paneles de letras/reconocimiento. La evaluación encontró mezcla de plantillas claras de Windows con superficies oscuras, contornos y tamaños distintos y utilidades musicales fuera de la tarjeta.

Se creó IslandButton como familia de acciones con plantilla propia, versiones principal/secundaria/discreta, transporte y navegación; feedback de tinta de 140 ms, foco visible y disponibilidad efectiva correcta. Se aplicó a todas las acciones de la isla y paneles musicales. Superficies de #17212C, radio/relleno de 14 px y jerarquía tipográfica común. Se retiraron los estilos parciales previos. Transporte vectorial, álbum de 56 px, utilidades integradas en el reproductor; volumen usa una plantilla Track/Thumb coherente, conservando binding bidireccional y teclado según el contrato de Avalonia 11.1.3. Fuente consultada: https://github.com/AvaloniaUI/Avalonia/blob/11.1.3/src/Avalonia.Themes.Simple/Controls/Slider.xaml.

Rutinas, archivos y portapapeles reciben agrupación y títulos; carpetas conservan apertura por nombre y X. La bandeja usa una lista no virtualizada para mostrar todas las referencias sin scroll; selección con el mismo material, acciones deshabilitadas hasta seleccionar y actualización visual al cambiar la disponibilidad efectiva. Red y estado eléctrico mantienen centrado; el clima recompone altura al terminar la consulta. HUD con símbolos vectoriales. Letras y reconocimiento comparten fondo, botones y cierre; reconocimiento conserva confirmación de captura y proveedor avanzado.

Nuevas preferencias persistentes ShowLyricsButton y ShowRecognitionButton, independientes de EnableOnlineLyrics y del proveedor. Casillas «Mostrar Letras» / «Mostrar Reconocer canción» en Isla Dinámica → Avisos y música → Botones del reproductor. Si se ocultan ambas, desaparece la fila; si queda una, ocupa su espacio sin columna vacía. Ocultar Letras cierra su panel y ocultar Reconocer cierra/cancela el panel abierto. Configuraciones antiguas conservan ambas visibles por defecto; el usuario decide desde los ajustes. Los criterios se documentan en DESIGN.md para futuras funciones.

El crash.log fue leído de nuevo: contenía exclusivamente las dos caídas de audio de las 04:17 ya reproducidas y corregidas. Se eliminó ese archivo a petición expresa, sin apagar el registro de errores nuevos ni borrar datos de usuario.

Validación: build sin advertencias; casillas reales, persistencia, visibilidad individual/combinada, ajuste de altura, binding Track→Slider→modelo simulado, teclado, lista completa y habilitación visual de acciones. Capturas de los tres módulos y paneles inspeccionadas. La escena final se adjunta a una ventana sintética fuera de pantalla, sin activación ni observadores, para resolver recursos de idioma como en producción. Regresiones de módulos, contenido sin scroll, apertura/cierre, estados de audio, eventos tardíos, letras, HUD y reconocimiento aprobadas. No se modificó volumen, micrófono o reproducción reales ni se consultaron datos personales. Pendiente aceptación estética y uso interactivo por el usuario.

Entrega de la revisión integral: único portable canónico reemplazado con éxito, huella de data sin cambios y crash.log antiguo ausente. Rama estable limpia en master. No se inició Nokto con preferencias reales para comprobarlo; las escenas usadas para QA fueron sintéticas y no ejecutaron rutinas ni capturas.

### Composición y reconocimiento integrado — 30-10-2026
Las capturas del usuario revelaron dos regresiones: el límite del 70 % de la altura forzaba columnas antes de aprovechar el área útil y el algoritmo llenaba la primera dejando la segunda casi vacía; Reconocer abría una ventana centrada independiente y la isla se contraía al salir hacia ella. Ambas situaciones se reprodujeron antes de corregirlas.

La distribución aprovecha la altura útil, equilibra secciones contiguas minimizando la columna más alta y conserva el orden elegido. El caso reproducido pasa de 579/233 px a 457/355 px. La anchura se mide antes de iniciar la transición para evitar usar dimensiones obsoletas tras cambiar de contenido. Los mensajes de carpetas vacíos ya no reservan una fila.

SongRecognitionView sustituye temporalmente el contenido del módulo dentro de la misma isla; no crea una segunda ventana. Fuentes Micrófono/Audio del PC seleccionables con la familia IslandButton, aviso de envío explícito, proveedor avanzado plegado y sin espacios de estados vacíos. Volver/Escape restaura el módulo; contraer o desactivar Reconocer dispone el panel y cancela la petición. Resultados, proveedores y errores ajustan la altura con la transición de la isla. Mostrar o elegir una fuente no captura audio ni consulta credenciales.

Prueba de composición con ocho funciones, escala 100/140 %, captura de panel integrado, selección sin captura, proveedores, restauración de navegación/anchura y cierre al contraer. Pruebas sintéticas fuera de pantalla, sin observadores ni hardware de audio. Capturas en Capturas-pruebas/qa-island-composition-*.png; revisión visual del usuario sigue pendiente.

Entrega: único portable canónico actualizado correctamente; huella SHA-256 de data idéntica antes/después. Build sin errores ni advertencias; pruebas de composición, armonía, módulos, audio, letras y reconocimiento aprobadas. Rama estable limpia. No se inició la aplicación del usuario ni se capturó audio personal.

### Letras: fallo específico de búsqueda de PXNDX — 30-10-2026
El usuario mostró «Los Malaventurados No Lloran», PXNDX, 3:27, con un aviso genérico de conexión mientras otras canciones sí cargaban. Reproducción real mediante metadatos de la captura: /api/get devuelve HTTP 503; /api/search devuelve HTTP 200 y registros sincronizados con duración compatible. El cliente se detenía en el primer error y culpaba a la conexión sin distinguir el estado del proveedor.

Se añade una sola búsqueda alternativa ante 404, error transitorio de red/servidor o agotamiento del presupuesto de 5 s del endpoint directo, con límite total de 15 s. Coincidencias por título/artista normalizados (acentos, mayúsculas, puntuación), duración ±2 s según documentación oficial, preferencia de sincronizadas y después duración más próxima. No quitar etiquetas de versiones en vivo para forzar coincidencias ni aceptar otro artista. Las consultas son secuenciales, espaciadas, cancelables, con User-Agent y JSON limitado a 1 MB; se respeta Retry-After de 503 y 429. El 429 no provoca búsqueda alternativa y bloquea peticiones nuevas durante el plazo. Solo se guardan resultados válidos en la caché; un fallo temporal no se convierte en ausencia permanente.

La UI distingue servicio temporalmente no disponible, límite de consultas, timeout, fallo de red y respuesta inválida. Botón Reintentar dentro del panel, oculto al cargar; se evitan errores/respuestas de solicitudes canceladas que ya no pertenecen al panel actual. Fuente oficial de parámetros y límites: https://lrclib.net/docs.

Validación: prueba que falla antes del cambio con el 503, y pasa con búsqueda, caché, cancelación, límites, duración fraccionaria, acentos, descarte de otro intérprete/versión y error de formato. Verificación real con el servicio compilado: /api/get HTTP 503 → /api/search HTTP 200 → 42 líneas sincronizadas para la pista de la captura, sin imprimir la letra ni acceder al audio de Spotify. Pruebas UI de reintento y mensajes, más regresiones de servicios musicales y clima. No se reprodujo ni grabó audio personal.

Entrega de letras: único portable canónico reemplazado con éxito y huella de data sin cambios. Build sin advertencias/errores y pruebas aprobadas; estado visual de error/reintento inspeccionado con texto sintético. La versión estable continúa limpia en master. No se inició la aplicación con preferencias reales.

### Reconocimiento integrado con movimiento y avisos diarios — 30-10-2026
Se retiraron el selector de proveedor, el formulario de clave, el acceso a PasswordVault y el cliente AudD. El reconocimiento usa la opción sin clave ya validada, con nombre de función Nokto y aviso explícito de envío de huella por Internet; las atribuciones y limitaciones técnicas del proveedor permanecen documentadas y su licencia se conserva. No se presenta como reconocimiento offline ni servicio oficial de Windows.

RecognitionSignal dibuja historial de nivel RMS del buffer WASAPI ya capturado (sin otra escucha), progreso visual de los ocho segundos y un barrido distinto al buscar. Fases de éxito, ausencia y error; durante la operación se priorizan señal/estado/Cancelar. Fotogramas a 60 Hz solo mientras está activo y visible; detención en resultado/cierre y movimiento reducido. No hay cuenta atrás numérica. Medidor acotado a 2048 muestras, sin copias persistentes ni archivo.

Catálogo de trece opciones con persistencia individual: volumen/silencio, brillo, Bloq Mayús, idioma, Internet, cargador, auriculares/salida, silencio del micrófono, Bloq Num, batería 20/10/completa, ahorro, Bluetooth emparejado clásico/LE y unidades montadas/retiradas. La configuración previa migra sus tres grupos; los seis nuevos avisos empiezan apagados. Solo se instancian observadores seleccionados; Mayús/Num/idioma comparten el temporizador existente de 300 ms. Batería, red, energía, WASAPI, DeviceWatcher y volumen de almacenamiento usan eventos. Bluetooth establece una base inicial sin anunciar todos los dispositivos, y batería usa umbrales/histéresis. Unidades informa letras de volúmenes locales, sin inspeccionar archivos ni prometer detectar cualquier periférico USB. Se conserva la política de no empujar el menú abierto ni reabrirlo por eventos de audio propios.

Aplicaciones y correos figuran como integraciones pendientes, sin casillas ficticias: el listener de Windows requiere capacidad de manifiesto y permiso del usuario; no se ha empaquetado/registrado esta versión portable ni conectado una cuenta de correo. Fuentes primarias: https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/notification-listener, https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/enumerate-devices, https://learn.microsoft.com/en-us/uwp/api/windows.system.power.powermanager, https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-volumechangeevent.

Pruebas: onda/RMS PCM y muestras no finitas, ciclo del temporizador, movimiento reducido, trece casillas reales y guardado, migración, ausencia de WMI/audio/timers si todo está apagado, umbrales, base de Bluetooth, filtros/privacidad y presentación de los trece vistazos. Capturas sintéticas revisadas. No se grabó audio real ni se probaron conexiones físicas Bluetooth/unidades; su compatibilidad con los dispositivos del usuario sigue pendiente de uso real.

### Cuota Codex, orden por arrastre y cabecera contextual — 30-10-2026
Se añadió Cuota de Codex como contenido independiente asignable a cualquier módulo, apagado por defecto para conservar las preferencias del usuario. También puede seleccionarse en la cabecera. CodexQuotaService detecta el codex.exe instalado y realiza únicamente initialize/initialized/account/rateLimits/read por stdio. Usa la sesión local de ChatGPT de Codex; no abre chats, inicia turnos, consume restablecimientos ni pide claves. El proceso hijo de consulta se cierra al terminar; nunca termina la aplicación Codex del usuario. No se extraen tokens ni leen historiales, y stderr se descarta.

Parser prefiere rateLimitsByLimitId.codex y convierte usedPercent en restante. Duración real, cuota corta/semanal y fecha/hora local de restablecimiento; datos nulos son Sin datos y el paso del tiempo no inventa una recarga. Consulta cada cinco minutos solo si la función está habilitada, refresco manual y comprobación al pasar el restablecimiento; privacidad oculta los datos. Se reutiliza el pulso de minuto de la cabecera. Lectura oficial de referencia: https://learn.chatgpt.com/docs/app-server#6-rate-limits-chatgpt. La fuente inicial del cliente también se contrastó con la herramienta de límites de Codex y se verificó el lector C# compilado con la sesión real, sin guardar identidad ni credenciales.

El orden se cambia desde ⋮⋮ con arrastre nativo, línea de inserción, validación de origen/módulo, guardado al soltar y Alt + flechas como alternativa. Se quitaron las flechas repetidas de la UI. Referencia de API de la versión 11: https://reference.avaloniaui.net/api/Avalonia.Input/DragDrop/. Con un módulo se oculta toda la navegación y se recuperan 40 px; con varios se cuenta el total habilitado, no siempre tres.

Información junto al ojo permite elegir ambos lados: cuota corta/semanal, restablecimiento, temporizador, silencio, reloj o nada. Automático prioriza una actividad en curso y la cuota habilitada, conservando el ojo centrado y los ajustes del reloj. Referencia de jerarquía contextual: https://developer.apple.com/design/human-interface-guidelines/live-activities. No se copian recursos de Apple.

Verificación: parser y nulos, hora local/restablecimiento pasado, caché, lectura real compilada, módulo único/dos módulos, recuperación de altura, centrado y cabecera ocultable, casillas de cuota y guardado; DragOver/Drop sobre la fila real, persistencia, teclado y rechazo de arrastres externos. Captura de cuota usa valores sintéticos y se remide después de adjuntar las plantillas para comprobar que la última línea cabe completa. Regresiones de armonía, módulos, audio, música y avisos aprobadas. Aprobación visual/gesto de arrastre en uso habitual pendiente del usuario.

Entrega completada de esta ampliación: tras confirmar el usuario que cerró Nokto desde la bandeja, se reemplazó el único ejecutable canónico con éxito. Huella SHA-256 de todos los archivos de data idéntica antes/después; staging eliminado, sin una segunda versión. Se actualizó el LEEME para identificar la entrega vigente. No se inició Nokto con preferencias reales ni se terminaron procesos del usuario.

### Limpieza final de Ajustes — 30-10-2026
Por petición del usuario se eliminó «Otras integraciones · pendientes», sus dos entradas de catálogo y la propiedad que las exponía a la vista. Ajustes presenta únicamente los trece avisos implementados; el seguimiento de correo/notificaciones de aplicaciones queda solo en la documentación de desarrollo. No se incorporaron nuevos observadores para esta limpieza.

Build sin errores ni advertencias, prueba existente de avisos/guardado/privacidad aprobada con comprobación de que no se muestran integraciones pendientes y captura revisada. Único portable canónico actualizado con éxito, huella de data sin cambios, sin staging restante y rama estable limpia. No se inició la aplicación del usuario.
