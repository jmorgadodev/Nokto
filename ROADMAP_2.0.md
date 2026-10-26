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
- Objetivo: transiciones perceptualmente fluidas (~60 FPS en equipos de 60 Hz), ~200–350 ms, sin garantizar FPS sin medirlos.
- Soportar pantallas múltiples, escalado DPI, cambios de monitor, ocultación en pantalla completa, accesibilidad y prioridad de eventos.
- Pruebas: 100 alternancias hover/leave/clic, múltiples resoluciones, pantalla completa, latencia y medición CPU/GPU/RAM en reposo.

## Fase 3 — Ojos y personalidad
Packs completos Minimal, Nokto Classic, Cyber, Anime Inspired (diseños originales), Creature, Cute y Dark/Horror.
Movimiento de pupilas según cursor / eventos, parpadeo natural espaciado, expresiones y sueño.
Todas las animaciones cortas terminan en un estado estático y respetan movimiento reducido.

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
