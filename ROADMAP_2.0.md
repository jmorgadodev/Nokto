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

## Fase 3 — Ojos y personalidad (implementacion base; revision visual pendiente)
- Catálogo de 42 variantes vectoriales originales en 7 packs: Minimal, Nokto Classic, Cyber, Anime Inspired (sin copiar iconografía de franquicias), Creature, Cute y Dark.
- Selector con traducciones ES/EN y vista previa dentro de la pestaña independiente Isla Dinámica.
- Motor local de dibujo sin recursos externos: solo se renderiza el estilo seleccionado.
- Movimiento de pupilas vinculado a la posición del cursor, con suavizado corto, sensibilidad configurable, seguimiento opcional. Lectura con GetCursorPos, sin hooks ni almacenamiento de coordenadas.
- Parpadeo aleatorio breve, opcional, con ritmo no constante; sueño al pasar 60 s sin mover el cursor, si está habilitado.
- Movimiento reducido y estilos Minimal no generan bucles de interpolación ni sondeos de cursor. Los ojos normales sondean a baja frecuencia (180 ms activos/650 ms dormidos), y renderizan a alta frecuencia solo durante eventos.
- Pendientes para aprobación visual: asegurar que las 42 variantes realmente resulten suficientemente distintas en 26 px; pruebas de DPI/multimonitor, recuperación de suspensión, FPS reales, perfiles GPU, animaciones contextualizadas con música/alarmas (dependerán de fases 5/6).
- Pruebas de catálogo y de persistencia, cuatro modos de arranque y no regresión básica antes del siguiente avance.

## Fase 4 — Sistema Windows y controles rápidos
Audio/micrófono, trabajo/actividad, volumen, batería, conectividad, brillo si disponible y atajos.

## Fase 5 — Multimedia
Sesiones multimedia de Windows, música, portada, progreso, controles y letras cuando estén disponibles.

## Fase 6 — Productividad
Reloj, alarmas, temporizador, cronómetro, Pomodoro, tareas, notas, hidratación y agenda.

## Fase 7 — Archivos, clima y notificaciones
Shelf de archivos, portapapeles opcional, clima con caché, notificaciones, privacidad.

## Fase 8 — Ajustes y localización
Pestaña autónoma Isla Dinámica junto a Ajustes, ES/EN, persistencia, accesibilidad y preferencias de módulos.

## Fase 9 — Optimización y QA
Pruebas de regresión de Nokto actual, memoria, CPU, GPU, despertar, monitor adicional y seguridad.

## Fase 10 — Empaquetado y actualización
Portable e instalador, verificaciones de integridad, rollback, conservación de datos y publicación v2.0 autorizada.

**Nota:** La primera iteración del motor ya cuenta con transición de geometría bajo demanda; no equivale a las pruebas de rendimiento ni a la experiencia final de Apple. Pendientes: seguimiento de ojos, opciones de animación avanzadas, pantalla completa, DPI/multimonitor, selección de actividades y acceso completo por teclado.
