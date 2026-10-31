# Isla Dinámica de Nokto — criterios de diseño

La identidad existente es una cápsula oscura con ojos temáticos centrados, tinta clara y acentos menta. El ojo elegido, su cantidad y su seguimiento se conservan. Este documento fija el criterio para incorporar funciones sin mezclar estilos de controles.

## Sistema visual
- Fondo de la isla: #10151E. Superficies internas: #17212C, sin contornos fuertes, radio de 14 px y 14 px de relleno.
- Acción secundaria: #22343F. Acción principal: #CDEEE4 con tinta #11282C. Texto principal claro y secundarios azulados legibles; coral solo para silencio.
- IslandButton es la familia de acciones, navegación, transporte y utilidades. No usar la plantilla genérica de Windows dentro de la isla. IslandAudioButton es su variante circular de estado.
- Títulos de sección de 12–13 px; información principal de 14 px; texto de apoyo de 11–12 px. Reloj de 12 px. Cifras temporales pueden usar monoespaciada.
- Una superficie por grupo funcional. Reproductor y sus utilidades dentro del mismo grupo. Las funciones ocultas eliminan su espacio. Evitar tarjetas dentro de tarjetas, botones claros ajenos al tema y símbolos emoji de tamaños distintos.

## Composición e interacción
- Navegación y ojos conservan posiciones. Los módulos eligen contenido y orden; se adapta el contenido sin scroll vertical.
- Letras y Reconocer canción tienen ajustes de visibilidad independientes, persistentes, dentro de «Botones del reproductor». No son acciones obligatorias.
- Micrófono y salida ocupan izquierda/derecha con estado textual y vectorial. Silencio cambia tinta/icono sin modificar dimensiones.
- Avisos no urgentes no insertan filas en un menú abierto. Eventos propios de audio no reabren la cápsula al salir. La animación principal conserva continuidad; el feedback de botones solo anima la tinta durante 140–150 ms y respeta movimiento reducido.
- Rutinas, Explorador, carpetas, archivos, portapapeles, estado eléctrico, red, clima y avisos utilizan la misma paleta y escala de espacio. Selección, foco, disponibilidad, vacío y error deben leerse sin cambiar de lenguaje visual.
- El reconocimiento se abre **dentro de la isla**, sustituyendo temporalmente el módulo; «Volver al módulo» restaura contenido y navegación. No abrir una ventana centrada independiente. Contraer la isla cierra el panel y cancela una captura pendiente.
- El reconocimiento y las letras comparten material, tinta y familia de botones con la isla. Capturas, permisos, consultas externas y acceso a archivos mantienen sus acciones explícitas.
- Aprovechar la altura útil antes de añadir columnas. Cuando sean necesarias, equilibrar sus alturas conservando bloques contiguos y el orden elegido; no dejar una segunda columna con un único estado pequeño y gran espacio vacío.
- Si solo hay un módulo, eliminar la fila de navegación y recuperar sus 40 px; con varios, el denominador es el número real de módulos habilitados.
- El orden se modifica arrastrando desde un asa con línea de inserción; Alt + flechas ofrece una alternativa de teclado. No repetir dos botones de orden por cada elemento.
- Cabecera contextual y opcional: temporizador, cuota de Codex o estado de silencio junto al ojo; reloj como alternativa. Ninguna información debe desplazar el centro del ojo ni fingir valores desconocidos.
- Reconocimiento: onda alimentada por el nivel real del fragmento, barrido distinto durante la búsqueda y confirmación al terminar. Sin contador numérico ni selector de proveedor/claves. Solo anima durante la operación, respeta movimiento reducido y mantiene Cancelar accesible.
- Avisos individuales por evento de Windows. Los nuevos empiezan apagados y sus detectores no se abren si no están seleccionados. Ajustes solo presenta funciones implementadas; el trabajo pendiente se documenta fuera de la interfaz del usuario.

## Verificación
Revisar capturas de los tres módulos con datos sintéticos antes de entregar; verificar las casillas reales de Ajustes y su persistencia, teclado/arrastre del volumen sin audio real, selección de archivos y estabilidad del menú ante eventos. La aceptación estética corresponde al usuario. No iniciar rutinas ni capturar audio personal para probar el diseño.
