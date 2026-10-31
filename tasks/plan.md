# Nokto 2.0 — QA integral y publicación

Solicitud del usuario: revisar toda la aplicación y la isla, corregir fallos, actualizar portable/instalable y landing, y publicar la versión 2.0 en GitHub. El desarrollo permanece en el worktree aislado hasta superar QA. Nunca probar apagado, reinicio, cierre de sesión, audio o monitores sobre el equipo real.

## Fases y aceptación

1. Inventario y aislamiento: rama feature/nokto-2-dev, master local intacto, GitHub y landing existentes identificados. Toda prueba debe usar Temp explícito sin fallback a datos personales.
2. Revisión y correcciones: persistencia y recuperación, ciclos/horarios/repeticiones/pausa/cancelación, acciones de energía, comandos, LAN, configuración, isla y recursos async. Cada fallo debe tener reproducción sintética y prueba de regresión.
3. QA consolidado: entrada explícita de release sin efectos de hardware; motores, perfiles, LAN, configuración, actualizador e isla. Reporte por función y límites de prueba física.
4. Distribución 2.0.0: una fuente de versión, pipeline con gates, portable e instalable coherentes, rutas de datos y actualización verificadas, licencias y checksums incluidos. No distribuir data personal ni preferencias DEV.
5. Landing y documentación: actualizar la landing real gh-pages preservando identidad, nuevas funciones y privacidad correcta; validar escritorio/móvil, enlaces y descargas.
6. Entrega GitHub: commit revisado, CI verde y assets 2.0 verificados antes de marcar la release pública como latest. Comprobar página pública y actualización desde 1.0.1. Mantener un único portable DEV vigente.

## Dependencias y límites

La lectura de StorageResolver precede a cualquier suite que construya carpetas temporales. Los arreglos preceden a los paquetes. Los paquetes y enlaces preceden al despliegue de landing y release latest. Las acciones nativas se prueban con backends falsos; instalar/desinstalar de verdad en el equipo del usuario no forma parte del QA. Los bloqueos por permisos, autenticación o disponibilidad de CI se documentan sin afirmar publicación completada.

GitHub: jmorgadodev/Nokto, rama principal master, landing en gh-pages y https://jmorgadodev.github.io/Nokto/. Última release al inicio: v1.0.1. La comprobación normal de gh estaba limitada por sandbox; lectura escalada de cuenta correcta.
