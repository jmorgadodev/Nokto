# Cambios

## 2.0.3 — respuesta inmediata al cambiar de pista

- El título y los controles de Spotify se actualizan antes de cargar la carátula; los títulos y artistas largos se desplazan para poder leerlos completos.
- La isla conserva la pista visible y reintenta lecturas brevemente cuando Windows interrumpe una sesión al cambiar de tema.


## 2.0.2 — corrección de visibilidad en reposo

- La isla conserva el ojo visible al pausar sus animaciones tras 60 segundos sin movimiento.

## 2.0.1 — corrección de errores

- La isla inicia según la preferencia elegida y no pierde el foco de las aplicaciones a pantalla completa.
- La reproducción no desaparece durante los cambios breves entre pistas y el seguimiento ocular filtra el temblor del cursor.
- El ejecutable estable consulta la versión publicada; el portable DEV indica claramente que no admite actualizaciones públicas.

## 2.0.0

Isla dinámica, módulos y cabecera configurables, galería de indicadores, música/letras/reconocimiento, avisos elegibles, clima, productividad y cuota Codex. Mejoras de persistencia, motor de rutinas y distribución. Detalles en [notas 2.0](releases/v2.0.0.md).

### Correcciones

- El inicio configurado de la isla ya no queda anulado por argumentos silenciosos heredados; elegir la isla también la activa.
- La isla conserva el foco de la aplicación a pantalla completa al usar sus controles.
- Los cambios breves de sesión o pista de Windows Media/Spotify ya no borran momentáneamente la reproducción.
- El seguimiento ocular filtra pequeñas variaciones del cursor y evita reiniciar la animación en cada movimiento.

La fecha de publicación se confirma en GitHub Releases; preparar estos archivos no implica que la versión esté publicada.
