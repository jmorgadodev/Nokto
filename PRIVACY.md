# Privacidad y servicios de Nokto 2.0

La automatización, las rutinas, la telemetría de hardware, las preferencias y la productividad se procesan localmente. Nokto no incluye un servicio de analítica de uso. Esta descripción distingue esas funciones de las consultas online.

| Función | Datos y destino | Cuándo |
|---|---|---|
| Clima por ciudad | Ciudad escrita a Open-Meteo | Al utilizar clima y cuando vence su caché |
| Clima por ubicación | Coordenadas de Windows a Open-Meteo; coordenadas aproximadas a Nominatim/OpenStreetMap para el nombre de ciudad | Solo si el usuario activa ubicación y Windows la permite; consultas con caché |
| Letras | Artista, título, álbum y duración a LRCLIB | Al pedir letras; disponibilidad y sincronización dependen de su catálogo |
| Reconocimiento | Captura de 8 segundos, procesada localmente para crear una huella acústica; la huella se consulta en el servicio de reconocimiento | Solo tras pulsar Reconocer; se puede cancelar. El audio no se sube ni se guarda como grabación. La integración puede dejar de estar disponible si cambia el servicio |
| Cuotas Codex | Lectura de límites mediante el app-server del Codex local ya autenticado | Cuando el usuario elige ese contenido; caché de cinco minutos. Nokto no lee archivos de credenciales ni conversaciones |
| Actualizaciones | Consulta y descarga desde GitHub Releases | Al comprobar manualmente o al iniciar si la opción está activada |
| Control remoto LAN | Estado, acciones y capturas disponibles en la red privada tras emparejar | Solo mientras el servidor local está habilitado. No se publica en Internet |
| Portapapeles y archivos | Lectura manual del texto copiado; referencias a archivos/carpetas seleccionados | Solo al solicitar esas acciones. No hay historial del portapapeles ni carga de archivos a un servicio |
| Avisos de teclado | Estado de Mayús/Bloq Num e idioma del teclado | Si se seleccionan esos avisos; no se guardan caracteres ni historial de teclas |

El reconocimiento desde el audio del PC incluye las aplicaciones de la salida predeterminada. El reconocimiento desde micrófono escucha el entorno y depende del permiso de micrófono de Windows.

Portable: preferencias, rutinas y auditoría en `data/` junto al ejecutable. Instalado: normalmente `%APPDATA%\Nokto`; instalaciones anteriores con datos adyacentes conservan esa ubicación. Los guardados de configuración y rutinas son atómicos, con respaldo `.bak`; los JSON dañados se conservan al reemplazarlos. Las capturas de rutinas y sus registros permanecen en almacenamiento local.

Los servicios externos pueden registrar las peticiones que reciben y aplicar sus propias políticas. No se garantiza catálogo completo de letras, identificación de todas las canciones ni disponibilidad permanente de esos proveedores. Los avisos Bluetooth, brillo y batería dependen del hardware y los controladores de Windows.
