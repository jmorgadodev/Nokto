# Nokto 2.0 — Desarrollo local y criterios de la isla dinámica

Estado: desarrollo en la rama local `feature/nokto-2-dev`. Sin publicación pública hasta validación y autorización.

## Fase 0 — Portable Dev (hecha)
Entorno aislado de `master`, configuración local, identidad `Nokto.Dev.exe`, compilación portable y primeras pruebas.

## Fase 1 — Inicio, núcleo y rendimiento (en progreso)
Inicio configurable: dashboard, isla, ambos o bandeja. No cargar servicios de UI innecesarios al usar la isla. Telemetría compartida y sin actualizaciones automáticas DEV.

## Fase 2 — Motor visual y de interacciones de Isla Dinámica (en progreso)
Referencias oficiales: Apple HIG Live Activities (https://developer.apple.com/design/human-interface-guidelines/live-activities) y ActivityKit (https://developer.apple.com/documentation/ActivityKit/displaying-live-data-with-live-activities).

- Presentaciones propias adaptadas a Windows: mínima, compacta y expandida; no copiar recursos gráficos de Apple.
- Transiciones continuas al expandir/contraer; conservar posición de elementos y evitar saltos.
- Expandir por hover opcional y/o clic; acceso por teclado; controles interactivos en la vista expandida.
- Permitir invertir una animación a medio recorrido desde su geometría actual.
- Movimiento reducido configurable y estado en reposo totalmente estático.
- Motor de animación activado solamente durante transiciones/eventos; sin bucle de renderizado permanente.
- Frecuencia adaptable de actualización de animaciones: detección automática del modo ACTIVO del monitor que contiene la isla, mediante MonitorFromPoint/GetMonitorInfo/EnumDisplaySettings de Windows.
- Ajuste propio de la pestaña Isla Dinámica > Rendimiento y animaciones: Automático (predeterminado), 60, 120, 144, 165 y 240 FPS objetivos. Normalizar modos reportados de 239/240 y 59/60 Hz.
- El temporizador de animación se activa solo durante la transición; al contraerse y entrar en reposo se detiene. Jamás mantener 240 actualizaciones por segundo en reposo.
- Objetivo: transición fluida ~200–350 ms a la frecuencia detectada; el objetivo de ticks en la UI no garantiza FPS renderizados, ya que Avalonia/Windows pueden imponer límites de compositor. Medir FPS reales/latencia antes de dar por aprobada la fase.
- Soportar pantallas múltiples, escalado DPI, cambios de monitor, ocultación en pantalla completa, accesibilidad y prioridad de eventos.
- Pruebas: 100 alternancias hover/leave/clic, múltiples resoluciones, pantalla completa, latencia y medición CPU/GPU/RAM en reposo.

## Fase 3 — Ojos y personalidad (iteración gráfica ampliada; revisión visual pendiente)
- La galería es un selector clicable en cuadrícula: 42 tarjetas con contorno del estilo activo, filtro por siete familias y selección guardada inmediatamente. La lista anterior se conserva como alternativa.
- Segunda pasada vectorial: firma específica por cada uno de los 36 estilos de ojos (ornamentos, visores, órbitas, rasgos, luces y geometrías), más seis indicadores minimalistas.
- Tamaño de isla configurable del 80 al 140 % en la pestaña Isla Dinámica, escalando ventana y contenido junto con la posición anclada.
- Catálogo de 42 variantes vectoriales originales en 7 packs: Minimal, Nokto Classic, Cyber, Anime Inspired (sin copiar iconografía de franquicias), Creature, Cute y Dark.
- Selector con traducciones ES/EN y vista previa dentro de la pestaña independiente Isla Dinámica.
- Motor local de dibujo sin recursos externos: solo se renderiza el estilo seleccionado.
- Movimiento de pupilas vinculado a la posición del cursor, con suavizado corto, sensibilidad configurable, seguimiento opcional. Lectura con GetCursorPos, sin hooks ni almacenamiento de coordenadas.
- Parpadeo aleatorio breve, opcional, con ritmo no constante; sueño al pasar 60 s sin mover el cursor, si está habilitado.
- Movimiento reducido y estilos Minimal no generan bucles de interpolación ni sondeos de cursor. Los ojos normales sondean a baja frecuencia (180 ms activos/650 ms dormidos), y renderizan a alta frecuencia solo durante eventos.
- Pendientes para aprobación visual: asegurar que las 42 variantes realmente resulten suficientemente distintas en 26 px; pruebas de DPI/multimonitor, recuperación de suspensión, FPS reales, perfiles GPU, animaciones contextualizadas con música/alarmas (dependerán de fases 5/6).
- Pruebas de catálogo y de persistencia, cuatro modos de arranque y no regresión básica antes del siguiente avance.

## Fase 4 — Sistema Windows y controles rápidos (primera integración)
- Integrado en la expansión: mute de micrófono y salida, subir/bajar volumen en pasos de 5 %, información de batería y estado de conexión; reutiliza el mismo MainViewModel y WindowsSystemAdapter de la consola (sin controlador WASAPI duplicado).
- Opciones independientes para mostrar u ocultar controles de audio, batería y red desde Isla Dinámica.
- Pendiente: iconos refinados y estado real de cada control, brillo compatible, accesos rápidos, integración con modo trabajo y validación en varios dispositivos.

## Correcciones visuales tras revisión del usuario (Fases 2 y 3)
- Reloj configurado por defecto para mostrarse solo en expansión; opciones Siempre / Expandir / Nunca, con migración automática del bool antiguo.
- Catálogo de 42 estilos con nueva geometría vectorial por familia: adornos, cejas, anillos, sensores, párpados y pupilas de mayor recorrido.
- Corrección de arranque de animación: la captura de ratón se activa al abrir la ventana, sin depender de IsVisible prematuro.
- Seguimiento más sensible y reacción animada de miradas; botón Probar animación dentro del panel expandido. Nada de animaciones continuas en estilos mínimos o con movimiento reducido.
- Galería de 42 diseños accesible desde la pestaña, con previsualización grande para revisión.
- Requiere todavía aprobación visual en equipo real; pruebas de fluidez/FPS, contraste en ambos modos y consumo son pendientes y no se deben presentar como aprobadas.

## Fase 5 — Multimedia (integración funcional en prueba)
- Servicio Windows Media Control (GSMTC) sin dependencias externas ni captura de audio. Se conecta solo si la isla está abierta y Multimedia habilitado; se desconecta al cerrar/deshabilitar.
- Observador por eventos para sesiones actuales, metadatos, reproducción y línea de tiempo, sin sondeo permanente de Windows.
- Cuando el reproductor informa datos: título, artista, carátula en memoria limitada a 1,5 MB, progreso y estado, anterior/reproducir-pausar/siguiente según permisos del reproductor.
- Al expandir, altura adaptativa: solo muestra el panel si existe sesión. Progreso local cada segundo solamente si se reproduce y la isla está expandida; cero temporizadores de progreso en reposo.
- Selector independiente Isla Dinámica > Multimedia: mostrar controles al expandir; título en compacto solo cuando está reproduciendo (opt-in, apagado por defecto).
- Validación preliminar: API de Windows detectó una sesión real, cargó carátula y se desconectó correctamente sin cambiar reproducción ni volumen.
- Pendiente: aprobar diseño en pantalla, estados ante cambios de reproductor, multimedia con varios reproductores a la vez, barra de búsqueda interactiva, letras (fuentes, permisos y derechos), sincronización de eventos complejos, consumo e interacción teclado; no presentar pendiente como completado.

## Fase 6 — Productividad (núcleo implementado; pruebas visuales pendientes)
- Estado de productividad local aislado en `IslandProductivityState`, serializado en el archivo de configuración del portable.
- Cuenta atrás de 5/15/25 min, pausar, continuar y reiniciar; cronómetro con acumulación en pausas.
- Pomodoro con minutos de enfoque (1–180) y descanso (1–60) configurables. Al vencer se cambia a la siguiente fase **detenida**; el usuario decide si la inicia.
- Alarmas de una sola vez o diarias en hora local HH:mm (máx. 12); reprogramación diaria al completar sin duplicar alertas.
- Tareas locales (máx. 30) con avisos opcionales a 15/60 min, completar primera pendiente y límite de 120 caracteres.
- Avisos visuales en isla activa y aviso sonoro de Windows configurable; no capturan pantallas, ubicación ni red.
- Reloj de servicio independiente del tamaño de la isla, activo al haber un temporizador/cronómetro/Pomodoro o alarmas/recordatorios futuros; parado sin trabajo.
- En reposo la isla permanece compacta; el panel de productividad se despliega bajo demanda. Mostrar cuenta atrás en compacto es opt-in.
- **Limitaciones honestas:** no puede despertar Windows suspendido ni avisar si la aplicación está cerrada; una alarma sin isla visible puede sonar, pero no aparece una notificación del sistema. Falta notificación nativa de Windows, notas persistentes, hidratación, agenda/calendario, gestión de múltiples tareas y alarmas desde UI, accesibilidad y pruebas con suspensión/horarios de verano.
- Pruebas: motor con reloj simulado para pausas, reinicios, vencimientos, alarmas diarias, tareas, serialización JSON; UI automatizada en copia DEV temporal; pendiente QA final de ventanas y regresión.


## Fase 7 — Archivos, clima y notificaciones (inicio)
- Bandeja de archivos locales opt-in en la expansión de la isla: botón para elegir archivo del equipo con selector nativo, máximo seis referencias, abrir solo al pulsar y quitar sin borrar el archivo.
- Persistir exclusivamente rutas de archivos seleccionadas; sin copiar contenidos, sincronización remota, escaneo de carpetas ni vigilancia de archivos.
- Pendiente: arrastrar y soltar si el usuario lo desea, portapapeles explícito opt-in con privacidad, clima consultado con ciudad/caché y frecuencia controlada, notificaciones nativas de Windows y modos de privacidad (pantalla compartida).
- QA inicial: galería por clic y persistencia verificados desde UIA; ajuste 130 % guardado; escalado 80 % (compacta 88×39), 100 % (110×48) y 140 % (154×67) capturados, hover a 140 % reparado con verificación física del cursor. El 140 % expande a 464×464 con reproductor activo.
- QA shelf: archivo sintético en copia temporal; bandeja oculta al no habilitar, expandida con selección de archivo, abierta sin recorte. No se abrió ningún archivo real durante pruebas.
- Pendiente antes de cerrar: picker con teclado real, archivos inexistentes, rutas protegidas, DPI/multimonitor, privacidad al compartir pantalla, clima y notificaciones nativas, medición de recursos y aceptación visual del usuario.

## Fase 8 — Ajustes y localización
Pestaña autónoma Isla Dinámica junto a Ajustes, ES/EN, persistencia, accesibilidad y preferencias de módulos.

## Fase 9 — Optimización y QA
Pruebas de regresión de Nokto actual, memoria, CPU, GPU, despertar, monitor adicional y seguridad.

## Fase 10 — Empaquetado y actualización
Portable e instalador, verificaciones de integridad, rollback, conservación de datos y publicación v2.0 autorizada.

**Nota:** La primera iteración del motor ya cuenta con transición de geometría bajo demanda; no equivale a las pruebas de rendimiento ni a la experiencia final de Apple. Pendientes: seguimiento de ojos, opciones de animación avanzadas, pantalla completa, DPI/multimonitor, selección de actividades y acceso completo por teclado.
