# Plan técnico de auditoría y estrés — Nokto / Antigravity

Fecha de preparación: 2026-10-03. Plataforma de entrega: Windows x64, .NET 8 autocontenido, Avalonia 11.1.3. Ventana: **Nokto — Consola de Operaciones y Control Local**. Este documento es un protocolo ejecutable de QA basado en el código del repositorio; las campañas prolongadas y la instalación en máquinas limpias deben registrarse como pendientes hasta ejecutarlas. No presentar las instrucciones de estrés como resultados obtenidos.

## 1. Alcance, criterios y preparación

Auditar las dos distribuciones, la cabina Inicio, Control Manual, Rutinas, Ajustes, bandeja, control de audio, atajos y control móvil LAN. Mantener operativas las rutinas del sistema, la persistencia del usuario y los bindings de energía. La aplicación utiliza datos locales; el servicio móvil sirve peticiones HTTP en LAN cuando está habilitado, sin contactar con proveedores IA.

La aprobación requiere: compilación sin advertencias ni errores; suites automatizadas correctas; apertura de ambas distribuciones sin .NET preinstalado; tareas independientes sin zombies; recursos nativos estables tras recuperación; UI sin recortes ni saltos de alineación; cancelación y cierre reproducibles; instalación/actualización/desinstalación verificadas en VM. Un `429` deliberado durante sobrecarga no es por sí mismo un fallo. Una excepción no controlada, una acción de energía equivocada, corrupción persistente o crecimiento sostenido de handles sí bloquea la release.

Antes de cada campaña:

1. Registrar commit y diff local, SHA-256 de binarios, edición/build de Windows, CPU, RAM, GPU/driver, DPI, pantallas, dispositivos de audio, ruta ejecutada y permisos. Una copia de desarrollo puede contener cambios sin commit: el hash del binario y la copia del diff son necesarios para identificar la versión.
2. Duplicar el portable en una carpeta de pruebas. Respaldar sus datos y los datos instalados antes de manipular configuraciones. No distribuir claves de emparejamiento, nombres de redes personales, capturas ni `data/` del usuario.
3. Cerrar otras instancias para pruebas de hotkeys y puerto. Confirmar el proceso por `ExecutablePath`, no solamente por nombre. Registrar el puerto realmente configurado; las configuraciones antiguas pueden conservar un valor distinto del predeterminado 5050.
4. Ejecutar energía destructiva exclusivamente con `WorkflowTestAdapter` o en una VM preparada. `WindowsSystemAdapter.IsDryRunMode` simula apagado/reinicio/suspensión/hibernación, pero NO neutraliza todos los efectos nativos: bloqueo, monitores, audio o cierre de una aplicación pueden seguir siendo reales. La confirmación del navegador para energía no protege una petición REST directa.
5. Medir cinco minutos de reposo y dos minutos de calentamiento antes de cada carga. Registrar también la vuelta al reposo durante al menos cinco minutos. Conservar resultados de tres repeticiones.

Umbrales iniciales de investigación, no garantías medidas: latencia de interacción p95 < 250 ms en reposo y < 1 s bajo carga; después del enfriamiento, GDI/USER dentro de ±5 handles del nivel estabilizado; memoria privada no debe aumentar de forma monotónica entre tres ciclos equivalentes. Investigar una retención > 20% o > 50 MiB frente al reposo estabilizado, distinguiendo caches de fugas. Ajustar umbrales con el hardware y documentar el motivo; nunca aprobar una pendiente persistente solo porque el valor absoluto sigue bajo.

## 2. Mapa de arquitectura y módulos activos

| Módulo | Fuentes principales | Responsabilidad y límite |
|---|---|---|
| Dominio y contratos | `src/Nokto.Core/Models`, `Abstractions/ISystemAdapter.cs`, `Engine/IWorkflowEngine.cs` | Presets, tareas activas, métricas, perfiles y operaciones del sistema |
| Motor concurrente | `src/Nokto.Core/Engine/WorkflowEngine.cs`, `WorkflowRunner.cs` | Contextos independientes, disparadores, pipeline, cancelación y acción terminal |
| Adaptador Windows | `src/Nokto.Platform.Windows/WindowsSystemAdapter.cs`, `Interop` | Win32, energía, captura, audio, keep-alive y métricas |
| Audio y atajos | `Audio/WasapiAudio.cs`, `AudioDeviceProfileService.cs`, `DefaultAudioDeviceSwitcher.cs`, `Hotkeys/GlobalHotkeyService.cs` | Endpoint predeterminado, volumen/mute, selección explícita y hotkeys |
| Identidad y métricas | `Hardware/HardwareProfileService.cs`, `GraphicsAdapterDiscovery.cs`, `Metrics/PassiveMetricsCollector.cs`, `Network` | Inventario estático y muestreo pasivo |
| Control remoto | `src/Nokto.Core/Remote/LocalRemoteServerService.cs`, `RemoteDashboard.html`, `src/Nokto.UI/Remote/RemotePreviewEncoder.cs` | HTTP local, emparejamiento, API, navegador embebido y JPEG |
| Cabina y estado reactivo | `src/Nokto.UI/ViewModels/MainViewModel*.cs`, `Views/MainWindow.axaml`, `ActiveTasksView.axaml`, `HomeAudioView.axaml` | Colecciones compartidas, dispatcher, visibilidad modular y controles |
| IA local | `src/Nokto.Core/Services/AiQuotaService.cs`, `LocalAiToolDiscovery.cs`, `ViewModels/AiEnvironmentItem.cs` | Descubrimiento pasivo, lanzadores seleccionables y cuotas legibles localmente |
| Persistencia | `src/Nokto.Core/Persistence`, `Serialization/NoktoJsonContext.cs` | Configuración, presets y auditoría JSONL |
| Bandeja y arranque | `src/Nokto.UI/Tray`, `App.axaml.cs`, `Platform.Windows/Startup` | Recursos de iconos, minimizar/restaurar y arranque configurable |

### 2.1 Concurrencia de rutinas

`WorkflowEngine` contiene `_activeWorkflows`, un `ConcurrentDictionary<string, RunningWorkflowContext>` indexado por ID de rutina, y `_executions`, un diccionario por GUID de ejecución. `_lifecycleLock` coordina las transiciones compuestas. El diccionario concurrente por sí solo no vuelve atómica una secuencia de operaciones.

Cada contexto conserva un preset congelado, `WorkflowRunner` propio, `CancellationTokenSource` enlazado al token externo, cronómetro, estado y `TaskCompletionSource` con `RunContinuationsAsynchronously`. Los parámetros JSON se clonan: editar el preset después de comenzar no debería cambiar esa ejecución. `StartRoutine` devuelve la tarea existente para un ID ya activo. Las tareas manuales usan IDs únicos `manual_...`; iniciar dos veces una misma rutina persistente no debe crear dos ejecuciones activas.

El worker se lanza con `Task.Run`. Los estados se publican mediante lectura/escritura `Volatile`; los callbacks comprueban que el contexto todavía corresponde a ese ID. `StopRoutine` retira inmediatamente el ID activo y solicita cancelación; la ejecución puede seguir liberando recursos. `_executions` sigue registrándola hasta el `finally`. Esa diferencia es esencial: una lista visual vacía no demuestra que el worker haya terminado.

En la salida se detiene el cronómetro, se elimina el ID activo solo si continúa apuntando al mismo contexto, se liberan runner y CTS, se elimina el GUID de ejecución y se resuelve o falla la tarea de finalización. Esta comprobación evita que la limpieza de una ejecución antigua elimine una nueva con el mismo ID. `WorkflowRunner` convierte cancelación esperada en finalización/estado inactivo; un fallo distinto produce estado fallido y auditoría, y su tarea comunica el error. Auditar que un fallo de A no detenga B ni C. También inyectar un suscriptor `StatusChanged` que lance una excepción: los callbacks externos son una frontera que no debe darse por infalible.

`FinishAll` limpia los IDs activos y cancela las ejecuciones registradas, incluidas las que ya no aparecen en UI. `Dispose` solicita el cierre y espera hasta cinco segundos a las tareas pendientes. Es una espera limitada, no una garantía de finalización si una operación nativa o un adaptador ignora cancelación. Investigar tareas pendientes después de ese plazo y callbacks posteriores al dispose.

Los recursos físicos siguen siendo compartidos: dos rutinas pueden competir por volumen, monitor, keep-alive o energía aunque su estado esté aislado. Probar su arbitraje y liberar referencias: no inferir exclusión de hardware a partir del aislamiento del contexto. El foco de gracia favorece la rutina con menor tiempo restante; el footer y las cápsulas muestran el conjunto activo.

### 2.2 Core Audio / WASAPI y hotkeys

`IMMDeviceEnumerator` enumera dispositivos y obtiene el predeterminado por dirección: `eRender` para salida y `eCapture` para entrada. La lectura usa `eMultimedia`, con fallback a `eConsole` cuando falla el HRESULT. La identidad real es el ID del endpoint, nunca la posición en la lista ni su nombre comercial. Auditar orden diferente, nombres duplicados y desaparición del endpoint seleccionado.

`IAudioEndpointVolume.GetMasterVolumeLevelScalar` proporciona 0..1, mostrado como porcentaje; `GetMute` indica el mute del sistema. La conmutación lee el valor actual y llama a `SetMute`. Salida y micrófono deben afectar direcciones diferentes. Las referencias COM se liberan en `finally`; las operaciones recuperan el endpoint, evitando depender indefinidamente de un objeto desconectado. Registrar HRESULT en el depurador cuando el dispositivo no esté disponible. No confundir ausencia de dispositivo con mute verdadero.

`DefaultAudioDeviceSwitcher` cambia el predeterminado ante selección explícita. Los guards de sincronización de `MainViewModel.Audio.cs` evitan que poblar un ComboBox cambie el dispositivo o reinicie el volumen al abrir. `RefreshAudioPanel` notifica porcentaje, nombre, estado e icono. Validar cambios externos desde Windows, teclado multimedia y el móvil, además de los botones internos.

`GlobalHotkeyService` registra atajos Win32 con `RegisterHotKey`, recibe `WM_HOTKEY` en su hilo de mensajes y libera registros con `UnregisterHotKey`. Los callbacks de UI pasan por el dispatcher. Defaults: Ctrl+Shift+M micrófono y Ctrl+Shift+S salida; verificar la configuración real del usuario y el mecanismo de parada global. Un conflicto con otra aplicación debe mostrar fallo de registro sin secuestrar otro atajo. Repetir habilitar/deshabilitar y cierre para detectar registros huérfanos.

### 2.3 HTTP local, autenticación y concurrencia

`LocalRemoteServerService` intenta `HttpListener`/HTTP.sys en `http://*:{puerto}/`. Ante acceso denegado (código 5) usa Kestrel sin crear URL ACL ni modificar firewall. Si el puerto está ocupado o ocurre otro error, no asumir el mismo fallback. Verificar en UI el transporte, el error y recuperación al cambiar puerto.

Cada arranque genera 32 bytes aleatorios representados como 64 caracteres hexadecimales. El QR contiene `/#key=...`; el fragmento no forma parte de la petición HTTP inicial. El dashboard conserva la clave en memoria y envía `X-Nokto-Key`. Se compara en tiempo constante después de validar longitud. La raíz entrega el dashboard sin clave, pero únicamente tras validar origen de red/Host. La clave anterior deja de servir al reiniciar el servidor; no publicar claves en evidencias.

Se permiten loopback y rangos IPv4 privados 10/8, 172.16/12 y 192.168/16, incluyendo IPv4 mapeada. No se permite automáticamente CGNAT ni toda IPv6. `Host` debe ser una dirección local conocida y el puerto correcto; `Origin` debe estar ausente o coincidir con la autoridad HTTP. No hay CORS abierto. HTTP LAN no cifra tráfico: usar una red de pruebas controlada y comprobar que estas restricciones no se anuncien como TLS.

Hay un semáforo global de ocho peticiones API, adquirido con una espera máxima de 250 ms desde la corrección BUG-05 del 2026-10-04; si no se libera capacidad, la saturación responde `429`. Otro semáforo permite una sola captura simultánea. Los requests HTTP.sys se rastrean para el cierre; Kestrel limita conexiones a 32, cabeceras a diez segundos y cuerpo a 4096 bytes. Cada handler tiene cancelación enlazada y timeout de diez segundos. Estos son límites de concurrencia, no un rate limiter de 20 peticiones/segundo. Una operación nativa síncrona puede retrasar la observación de cancelación.

| Método / ruta | Cuerpo o resultado | Casos de QA |
|---|---|---|
| GET `/` | Dashboard HTML local con CSP nonce | LAN/Host/Origin, sin recursos externos |
| GET `/api/status` | JSON: métricas, batería, audio, uptime y tareas | Coherencia con escritorio, carreras al parar tareas |
| GET `/api/snapshot` | `image/jpeg`; preview habilitada | 20 rps, desconexión cliente, varios monitores |
| POST `/api/audio/volume` | `{"volume":45}`; número finito 0..100 | 0, 100, fracciones, negativos, cadenas, overflow |
| POST `/api/audio/toggle-output-mute` | Conmuta salida | Endpoint ausente, cambios simultáneos |
| POST `/api/audio/toggle-mic-mute` | Conmuta entrada | No cambiar salida por error |
| POST `/api/tasks/stop` | `{"taskId":"ID_REAL"}`; longitud 1..128 | ID desconocido, doble stop, reinicio mismo ID |
| POST `/api/power/monitors-off` | Apagar pantallas | Solo entorno controlado |
| POST `/api/power/lock` | Bloquear estación | No queda neutralizado por todo dry-run |
| POST `/api/power/sleep` | Suspender | Fake o VM; confirmar recuperación |
| POST `/api/power/shutdown` | Apagar | Fake o VM; confirmar cancelación previa |

Respuestas esperadas: `200` éxito; `401` falta/clave incorrecta; `403` LAN/Host/Origin inválidos o preview deshabilitada; `400` JSON o parámetro inválido; `404` tarea/ruta POST inexistente; `405` método no permitido; `413` cuerpo grande; `429` sobrecarga; `408` cancelación observada; `503` hardware/captura/operación no disponible. Actualmente un GET desconocido después de autenticar alcanza la rama `405`, no `404`: registrar el contrato real. Una desconexión o el límite de conexiones puede cerrar el socket en lugar de entregar JSON. Un `413` cierra la conexión para evitar reutilizar un cuerpo no consumido.

Las cabeceras incluyen no-store, no-cache, nosniff, DENY y política de contenido restrictiva. El dashboard consulta estado cada dos segundos y preview cada cinco, pausa cuando la página queda oculta y revoca URLs de blobs. Las tareas se insertan con `textContent`. Auditar navegación atrás/recarga, rotación de clave, múltiples móviles y ausencia de caches/capturas en disco.

### 2.4 Captura de pantalla y JPEG: recursos a observar

`WindowsSystemAdapter.CaptureScreenAsync` captura el escritorio virtual mediante `GetDC`, `CreateCompatibleDC`, `CreateCompatibleBitmap`, `SelectObject` y `BitBlt`. Obtiene BMP en memoria. El bloque `finally` restaura el objeto anterior antes de `DeleteObject`, después ejecuta `DeleteDC` y `ReleaseDC`. Auditar también los caminos de error a mitad de esta secuencia, no solo el caso exitoso.

`RemotePreviewEncoder` decodifica con Skia, escala manteniendo proporciones a 1280×720 sobre fondo negro y codifica JPEG calidad 70. Bitmap/surface/paint/image/data se liberan con `using`; no se escribe un JPEG temporal. El encoder activo no es GDI+ / System.Drawing, aunque la captura sí consume handles GDI. Medir tanto handles GDI/USER como memoria privada y handles totales: una fuga de objetos Skia puede aumentar memoria sin aumentar GDI.

Probar pantalla bloqueada, escritorio seguro, RDP, pantalla desconectada, resolución 4K/ultrawide, escala mixta, origen virtual negativo y dos monitores. `503` limpio cuando no se puede capturar es aceptable; nunca enviar un JPEG corrupto como éxito. Verificar dimensiones, firma JPEG y apertura con decoder independiente; no conservar imágenes personales como evidencia.

### 2.5 Telemetría e identidad del equipo

`PassiveMetricsCollector` protege el muestreo con `_syncLock`: CPU por deltas `GetSystemTimes`, RAM por `GlobalMemoryStatusEx`, red por contadores `NetworkInterface`, actividad de disco por `IOCTL_DISK_PERFORMANCE` y espacio disponible por consulta de volumen. La utilización GPU utiliza PDH con contador inglés de GPU Engine 3D; la query se cierra al disponer. Comprobar wildcard/instancias, primeras muestras, GPUs híbridas y driver sin contador: un nombre correcto de GPU no prueba que su utilización se esté leyendo correctamente.

`HardwareProfileService` lee SO/CPU desde Registry, RAM Win32, pantalla/monitores y refresh rate nativos; `GraphicsAdapterDiscovery` enumera DXGI, libera interfaces COM, ordena adaptadores y filtra software/remotos. El nombre DXGI corresponde a identidad estática, no a un muestreo de carga. No hay un colector WMI periódico activo en esta ruta: si QA propone WMI como comparación, usarlo externamente y no atribuirlo al funcionamiento de Nokto.

El VM usa `PeriodicTimer` de dos segundos; tareas más costosas de red/audio se refrescan con menor frecuencia (aproximadamente seis segundos). Medir latencia real; no asumir que cualquier opción guardada de intervalo modifica este timer. Probar suspensión/reanudación, reset de contadores de red, cambio Wi-Fi/Ethernet/VPN y adaptadores deshabilitados. El SSID/nombre de red debe concordar con la conexión real, sin usar el nombre de interfaz como sustituto silencioso.

El footer tiene cápsulas de ancho estable y cifras tabulares `FontFeatures="+tnum"` con `Consolas, monospace`. El resumen central tiene anchura limitada y tooltip. Probar 0/9/10/99/100%, GB→MB, unidades de red y disco muy bajo: el cambio de cifras no debe desplazar iconos ni la barra. Números ausentes no deben inventarse como mediciones válidas.

### 2.6 Persistencia, IA y límites visuales

`StorageResolver` elige `<base>/data` si hay `portable.lock`, config adyacente o directorio escribible fuera de Program Files. En el resto usa `%APPDATA%/Nokto`. Una instalación por usuario en una carpeta escribible puede conservar datos junto al ejecutable: verificar la ruta real, no asumir AppData Roaming solamente por usar Setup. Snapshots y logs pertenecen a la misma raíz resuelta.

`PersistenceService` serializa con contextos System.Text.Json generados. Config y presets se escriben directamente con `File.WriteAllBytes`; NO hay reemplazo atómico ni lock general de escritura. Auditoría JSONL usa `_auditLock` por instancia, no un mutex entre procesos. Lecturas corruptas tienen fallback y presets pueden regenerarse. Probar interrupción de escritura, disco lleno, archivos readonly y dos instancias sobre la misma ruta: detectar pérdida de configuración en lugar de confundir fallback con integridad.

El catálogo IA detecta instalaciones locales y permite elegir accesos visibles. Ejecutables detectados no implican cuotas disponibles. La telemetría IA lee únicamente fuentes locales legibles sin autenticar; no valida cuentas ni obtiene tokens remotamente. Probar herramientas ausentes, nombres/versiones distintas, ejecutable desinstalado tras detectar, PATH duplicado, porcentajes 0/100 y archivos temporariamente bloqueados. Conservar el aviso de Ajustes y los badges compactos de herramientas sin métricas.

`ActiveTasksView` usa cápsulas 270×95 en WrapPanel con scroll. Las tarjetas Inicio forman una cuadrícula modular; el Radar tiene scroll interno limitado. Probar ventana inicial maximizada, restauración desde bandeja, ventanas estrechas, DPI 100/125/150/200%, tamaños extremos de texto y todas las combinaciones de visibilidad. Los porcentajes de Radar deben verse completos con su espacio reservado, sin recortar `100%`. Los controles deben ser accesibles por teclado y conservar tooltip cuando el nombre se acorta.

## 3. Protocolo de estrés: tareas concurrentes

### T01 — Saturación manual 5→10 tareas

1. Usar copia de datos de QA y acciones terminales inocuas/fake. Iniciar cinco cuentas atrás distintas de 60–120 s en Control Manual sin esperar la anterior. Repetir con diez, mezclando espera/proceso/inactividad solo en el harness controlado.
2. Fotografiar layout a diferentes anchos. Verificar IDs distintos, una cápsula por tarea y el mismo conteo en Inicio, Manual, Rutinas y `/api/status`. El WrapPanel debe reordenarse; no superponer ni aumentar el ancho horizontal fuera del viewport.
3. Parar únicamente la tarea del medio mientras las demás avanzan. Pulsar doble stop; terminar una desde móvil y otra desde escritorio. Verificar que cada contador restante continúa y que no reaparece la cápsula retirada.
4. Ejecutar 100 ciclos de inicio/stop, cancelación del token externo y finalizar todas. Esperar las tareas de finalización; verificar con el depurador `_executions.Count == 0` además de `_activeWorkflows.Count == 0`. En adaptador cooperativo, exigir limpieza dentro de cinco segundos; registrar cualquier worker que sobreviva.
5. Medir cronómetros, CPU, memoria privada, hilos, handles, GDI y USER en reposo/carga/recuperación. Revisar auditoría sin líneas mezcladas, finales duplicados o errores de una tarea atribuidos a otra.

### T02 — Carreras e inyección de fallos

| Escenario | Inyección | Invariante |
|---|---|---|
| Mismo ID | Lanzar 20 `StartRoutine` paralelos | Una ejecución activa, todos observan su finalización |
| Stop + restart | Cancelar A y reiniciar el mismo ID antes de su finally | El finally viejo no elimina B ni actualiza su estado |
| Fallo aislado | Adaptador fake lanza al ejecutar un paso de A | A falla; B/C continúan; recursos de A liberados |
| Cancelación temprana | Token cancelado antes de iniciar o durante espera/gracia | No acción terminal tardía, resultado consistente |
| Preset mutable | Editar parámetros/pipeline tras iniciar | Ejecución conserva snapshot congelado |
| Postponer | Varias llamadas mientras countdown expira | Tiempo válido, sin doble acción terminal |
| Cierre en carga | Dispose mientras hay triggers/pipelines/capturas | No callbacks sobre UI cerrada ni bloqueos indefinidos |
| Adaptador no cooperativo | Bloquear una operación > cinco segundos | Documentar espera acotada y worker pendiente; no afirmar limpieza completa |
| Hardware compartido | Dos pipelines de audio/keep-alive | No restaurar un recurso que otra tarea sigue necesitando |
| Evento fallido | Suscriptor de estado lanza | Identificar frontera de error y efecto sobre el resto |

Extender `tests/Nokto.ConsoleTest/ConcurrentWorkflowTests.cs` usando `WorkflowTestAdapter` y barreras controladas, no sleeps arbitrarios, cuando un hallazgo necesite regresión. No agregar tests que solo reflejen líneas de implementación sin verificar comportamiento observable.

## 4. Protocolo de estrés: LAN / snapshot 20 peticiones por segundo

### N01 — Campaña y medición

1. Habilitar control remoto/preview en la copia de QA. Confirmar GET status autenticado y un JPEG válido. Carga desde loopback y después desde otro equipo LAN; separar rendimiento de captura del Wi-Fi.
2. Ejecutar 20 rps durante diez minutos, tres ciclos. Después efectuar soak de 30–60 minutos. Intercalar `/api/status` cada dos segundos y cancelación de clientes. No llamar endpoints de energía en el PC de trabajo.
3. Registrar tasa ofrecida REAL, completada, 200/429/408/503, excepciones de transporte, bytes recibidos, latencias p50/p95/p99 y backlog cliente. No exigir 100% de respuestas 200 cuando el servidor aplica backpressure. Investigar 503 sostenidos en sesión capturable y 408 constantes bajo carga moderada.
4. Muestrear recursos cada cinco segundos. La memoria debe estabilizarse después de GC/caches; GDI/USER debe volver cerca del baseline. Probar cancelación mientras espera `_snapshotGate` y a mitad de captura. La liberación de cada semáforo debe quedar garantizada.
5. Deshabilitar preview en carga: nuevos requests deben devolver 403 y la UI seguir operativa. Desactivar servidor y cerrar aplicación con peticiones pendientes; no dejar puerto ocupado ni claves anteriores válidas al reiniciar.

Ejemplo de generador acotado para **PowerShell 7**. Guardarlo fuera de datos de producción y ejecutar en la sesión QA. Lee la clave sin mostrarla, no guarda capturas y envía solo GET snapshot. El ritmo objetivo es 50 ms; si alcanza el límite cliente, contabiliza envíos omitidos en vez de crear una cola ilimitada. Una máquina generadora saturada no demuestra rendimiento del servidor.

```powershell
$base = [uri](Read-Host 'URL local sin fragmento ni clave, ej. http://127.0.0.1:5050/')
if ($base.Scheme -ne 'http' -or $base.UserInfo -or $base.Fragment -or $base.Query) { throw 'URL no válida' }
$targetIp = $null
if ($base.Host -ne 'localhost') {
    if (-not [Net.IPAddress]::TryParse($base.DnsSafeHost, [ref]$targetIp)) { throw 'Usar IP local o localhost' }
    $isPrivate = $targetIp.ToString() -match '^(10\.|192\.168\.|172\.(1[6-9]|2[0-9]|3[01])\.)'
    if (-not [Net.IPAddress]::IsLoopback($targetIp) -and -not $isPrivate) { throw 'Destino fuera de LAN' }
}
$keySecret = Read-Host 'Clave de sesión QR (no se registrará)' -AsSecureString
$keyPtr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($keySecret)
try { $key = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($keyPtr) }
finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($keyPtr) }
if ($key -notmatch '^[0-9A-Fa-f]{64}$') { throw 'Formato de clave incorrecto' }
$http = [Net.Http.HttpClient]::new()
$http.Timeout = [TimeSpan]::FromSeconds(12)
$http.DefaultRequestHeaders.Add('X-Nokto-Key', $key)
$key = $null
$durationSeconds = 600
$pending = [Collections.Generic.List[object]]::new()
$records = [Collections.Generic.List[object]]::new()
$clock = [Diagnostics.Stopwatch]::StartNew()
$slot = 0
$skipped = 0
try {
    while ($slot -lt $durationSeconds * 20 -or $pending.Count) {
        for ($i = $pending.Count - 1; $i -ge 0; $i--) {
            $p = $pending[$i]
            if (-not $p.Task.IsCompleted) { continue }
            $response = $null
            $status = 'transport-error'
            $bytes = 0
            try {
                $response = $p.Task.GetAwaiter().GetResult()
                $status = [int]$response.StatusCode
                $body = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
                $bytes = $body.Length
                $body = $null
            } catch { $status = 'transport-error' }
            finally { if ($response) { $response.Dispose() } }
            $records.Add([pscustomobject]@{ Status=$status; Bytes=$bytes;
                LatencyMs=[math]::Round($clock.Elapsed.TotalMilliseconds - $p.Start, 1) })
            $pending.RemoveAt($i)
        }
        $now = $clock.Elapsed.TotalMilliseconds
        if ($slot -lt $durationSeconds * 20 -and $now -ge $slot * 50) {
            if ($pending.Count -lt 32) {
                $pending.Add([pscustomobject]@{ Start=$now;
                    Task=$http.GetAsync([uri]::new($base, '/api/snapshot')) })
            } else { $skipped++ }
            $slot++
        } else { Start-Sleep -Milliseconds 2 }
    }
} finally {
    $http.Dispose()
    # GetAsync usa lectura completa por defecto; liberar cualquier respuesta final pendiente.
    foreach ($p in $pending) {
        try { $p.Task.GetAwaiter().GetResult().Dispose() } catch { }
    }
    $keySecret.Dispose()
}
$records | Export-Csv './snapshot-results.csv' -NoTypeInformation -Encoding utf8
$records | Group-Object Status | Select-Object Name, Count
[pscustomobject]@{ Slots=$slot; SkippedClientLimit=$skipped;
    Completed=$records.Count; ElapsedSeconds=$clock.Elapsed.TotalSeconds }
```

Para inspección de recursos, ejecutar en otra consola con el PID verificado de Nokto. `GetGuiResources` devuelve 0 si falla/no tiene acceso: no interpretar cero como prueba de ausencia de fugas. No forzar GC del proceso durante la campaña de aceptación.

```powershell
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class NoktoQaGuiResources {
    [DllImport("user32.dll", SetLastError=true)]
    public static extern uint GetGuiResources(IntPtr process, uint flags);
}
'@
$noktoPid = [int](Read-Host 'PID de Nokto verificado por ruta')
$samples = for ($sample = 0; $sample -lt 240; $sample++) {
    $proc = Get-Process -Id $noktoPid -ErrorAction Stop
    $proc.Refresh()
    [pscustomobject]@{ Time=[DateTimeOffset]::Now; Pid=$noktoPid;
        GDI=[NoktoQaGuiResources]::GetGuiResources($proc.Handle,0);
        USER=[NoktoQaGuiResources]::GetGuiResources($proc.Handle,1);
        Handles=$proc.HandleCount; Threads=$proc.Threads.Count;
        PrivateMiB=[math]::Round($proc.PrivateMemorySize64/1MB,2);
        WorkingSetMiB=[math]::Round($proc.WorkingSet64/1MB,2);
        CpuSeconds=$proc.TotalProcessorTime.TotalSeconds }
    $proc.Dispose()
    Start-Sleep -Seconds 5
}
$samples | Export-Csv './nokto-resources.csv' -NoTypeInformation -Encoding utf8
```

### N02 — Contrato negativo y resiliencia

Probar sin clave, clave malformada/anterior, Host no local, Origin externo, IPv6/IPv4 mapeada, puerto ocupado y permiso HTTP.sys denegado. Probar JSON truncado, root array, NaN/cadenas, longitud >4096 con Content-Length y chunked, taskId vacío/129 caracteres. Tras 413 usar una conexión nueva y comprobar status. Verificar que la respuesta no revela trazas, configuración o rutas personales. Probar cliente que envía cabeceras lentamente y cierre durante request. Rotar clave apagando/encendiendo el servicio; comprobar que el dashboard antiguo recibe 401.

## 5. Protocolo de hardware y audio

### A01 — Mute, volumen y selección

1. Anotar endpoint y estado previo para restaurarlos. Probar salida 0/1/45/99/100% y mute on/off desde Windows; verificar actualización en Home, estación y móvil tras el intervalo de refresco.
2. Cambiar mute desde botón y hotkey 100 veces, dejando al menos un intervalo de refresco entre cambios observables. Comparar `GetMute` nativo, icono y texto; no basta con escuchar silencio a volumen 0.
3. Verificar entrada y salida por separado. Pulsar micrófono no debe mutear salida. Comparar con panel Windows y llamada/grabador de pruebas. Los nombres largos deben acortarse visualmente manteniendo identidad y tooltip.
4. Abrir/cerrar Home/Ajustes sin tocar selección: el dispositivo predeterminado y volumen no deben cambiar. Reordenar dispositivos y duplicar nombres comerciales en el harness.
5. Cambiar dispositivo desde Windows mientras se refresca y mientras llega un comando móvil. Registrar doble lectura/SetMute concurrente: un toggle es read-modify-write, no asumir una transacción atómica entre clientes.

### A02 — Desconexión y recuperación

Desconectar auriculares USB/Bluetooth y micrófono durante enumeración, volumen y mute. Probar retirada del predeterminado, ningún endpoint disponible, dispositivo deshabilitado, servicio Windows Audio reiniciado en VM, cambio HDMI al retirar monitor y reconexión con ID nuevo. Repetir 50 ciclos cuando el hardware lo permita.

Resultado esperado: sin crash del hilo UI ni excepción COM no controlada; endpoint ausente representado limpiamente; comando remoto puede devolver 503; selección recuperada del sistema, no del primer elemento. Tras reconectar se puede controlar nuevamente sin reiniciar Nokto. Comparar COM/handles/hilos/memoria al reposo. Registrar cuánto tarda en detectarse el cambio; no exigir feedback instantáneo a una ruta que refresca cada seis segundos.

### A03 — Atajos y bandeja

Probar conflicto RegisterHotKey con segundo proceso, reasignación, deshabilitar audio, cerrar ventana a bandeja, restaurar, salida completa y relanzar. El cierre completo debe liberar hotkeys; minimizar no debe duplicar registros. Verificar que una instancia secundaria no robe atajos ni produzca una falsa indicación de registro exitoso. Alternar 750 estados y 10.000 renders en harness de bandeja, comprobando recursos GDI estables. No cambiar el estado inicial real de audio al ejecutar tests.

## 6. Límites de datos y UI

| Área | Casos | Resultado a comprobar |
|---|---|---|
| Countdown | 0; 1 s; 59 s; 99 h 59 m 59 s; pegado negativo/enorme/fracción | UI limita/normaliza; motor no desborda ni dispara acción errónea |
| Entradas programáticas | `int.MaxValue`, TimeSpan negativo/enorme, JSON alterado | No overflow silencioso; error/fallback explícito y aislado |
| Trigger/steps | Inactividad 1..1440 min; debounce 0..60 s; silencio 5..300 s; fade 1..300 s; wait 1..3600 s | Coherencia límites, cancelación durante espera |
| Gracia | 0 y 300 s, expirar/postponer/stop a la vez | Una sola acción terminal; foco correcto |
| Disco | 1 GB, 0.99/0.1/0 GB, volumen ausente y umbral decimal | Cifra con unidad legible, alerta sin deformar footer; ausencia distinta de cero |
| Persistencia | Disco lleno/read-only, JSON incompleto, auditoría muy grande | Sin crash ni pérdida silenciosa; documentar fallback/regeneración |
| IA | 0/100%, reset ausente/vencido, herramienta sin cuota, nombre largo | Porcentajes completos; no inventar cuotas ni diagnósticos crudos |
| Cabina | Todas tarjetas visibles/ocultas; 10+ accesos IA; 10 tareas | Orden y navegación coherentes; scroll local, sin colisiones |
| Tipografía | DPI mixto, `100%`, uptime 99+ días, CPU larga | Alineación, ellipsis/tooltip, cifras tabulares |
| Ventana | Inicio maximizado, restaurado estrecho, tray, RDP | Estado correcto, porcentajes visibles al abrir |

Los límites del NumericUpDown no prueban validación de JSON ni API. Inyectar parámetros extremos solo en harness con acciones fake. Para disco <1 GB usar VHD/carpeta de QA o métricas fake; no llenar deliberadamente el disco de trabajo. Registrar redondeo: mostrar `0 GB` para 0.4 GB puede requerir unidades MB aunque no sea un fallo de consulta.

## 7. Verificación automatizada reproducible

```powershell
Set-Location 'C:\Users\jorge\Proyectos\Nokto'
$dotnetExe = 'C:\Users\jorge\AppData\Local\Microsoft\dotnet\dotnet.exe'
$env:DOTNET_ROOT = 'C:\Users\jorge\AppData\Local\Microsoft\dotnet'
& $dotnetExe build Nokto.sln -c Release -p:TreatWarningsAsErrors=true
if ($LASTEXITCODE) { throw 'Build fallido' }
& $dotnetExe test Nokto.sln -c Release --no-build -p:TreatWarningsAsErrors=true
if ($LASTEXITCODE) { throw 'Verificación fallida' }
```

Los proyectos de test son runners de consola: targets `AfterTargets="VSTest"` ejecutan explícitamente las suites. No confundir ausencia de tests VSTest tradicionales con ausencia de verificaciones. `Nokto.ConsoleTest` ejecuta concurrencia, servidor remoto, descubrimiento IA, regresiones y auto-test nativo; `Nokto.TrayTest` ejecuta recursos de bandeja y regresiones del VM/UI. Revisar salida y códigos de retorno de cada runner. Las pruebas con adaptador fake no demuestran estabilidad de hardware físico ni reemplazan las campañas N01/A02.

Para localizar una regresión ejecutar `Nokto.ConsoleTest.exe --concurrency-test`, `--remote-test`, `--ai-tools-test`, `--regression-test` o `--auto-test` desde su carpeta Release. El auto-test toca primitivas nativas: registrar/restaurar audio y evaluar su alcance antes de ejecutar en una llamada de trabajo.

## 8. Distribución dual, instalación y rollback

Compilar/publicar secuencialmente: ambas publicaciones usan el mismo proyecto/obj y diferentes propiedades de bundling. No ejecutar los dos publish en paralelo sobre la misma carpeta intermedia.

```powershell
& $dotnetExe publish src\Nokto.UI\Nokto.UI.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:TreatWarningsAsErrors=true -o artifacts\Nokto-Portable-x64\
if ($LASTEXITCODE) { throw 'Publish portable fallido' }
& $dotnetExe publish src\Nokto.UI\Nokto.UI.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:IncludeNativeLibrariesForSelfExtract=false `
    -p:TreatWarningsAsErrors=true -o artifacts\Nokto-Installer-x64\App\
if ($LASTEXITCODE) { throw 'Publish completo fallido' }
# Usar ISCC.exe instalado o el compilador oficial portable de herramientas.
& $isccExe build\inno-setup\nokto-setup.iss
if ($LASTEXITCODE) { throw 'Compilación Setup fallida' }
```

`EnableCompressionInSingleFile` es condicional al single-file para que la distribución estructurada no intente comprimir un bundle inexistente. Portable integra runtime y bibliotecas nativas con extracción; comprobar permisos/espacio del directorio de extracción .NET, antivirus y arranque desde copia nueva. Estructurado incluye ejecutable, ensamblados, runtime, ASP.NET Core y bibliotecas nativas dentro de `App/`; mover el directorio completo, no solo `Nokto.exe`.

El script Inno Setup conserva AppId, usa Windows 10 build 19041 como mínimo y arquitectura x64, empaqueta recursivamente `App/`, y excluye `data/` y `portable.lock`. Tiene privilegios mínimos y opciones de accesos directos/inicio con Windows. No instalar el propio compiler ni incluirlo dentro de la distribución Nokto. Fuente oficial del compilador: [descargas Inno Setup](https://jrsoftware.org/isdl.php); soporte portable verificado en [isportable.iss del editor, tag 6.7.3](https://github.com/jrsoftware/issrc/blob/is-6_7_3/isportable.iss). Verificar Authenticode del compiler antes de ejecutarlo. La firma del compilador no firma automáticamente el instalador Nokto.

Matriz de instalación en VM:

1. Windows 10 19041+ y Windows 11 x64 sin .NET preinstalado, usuario estándar. Abrir portable y distribución completa; confirmar título/metadatos, pantalla Inicio, audio, IA local y cierre.
2. Ejecutar Setup en español/inglés con/sin icono y startup. Registrar ruta real, permisos, datos y entradas de desinstalación. No exigir elevación por defecto. Comprobar que startup es opcional y apunta al archivo instalado.
3. Actualizar sobre una versión anterior manteniendo AppId. Confirmar cierre previo de la instancia, archivos reemplazados, datos intactos y hotkeys/puerto libres. Probar archivos bloqueados y cancelación del instalador.
4. Desinstalar; retirar binarios/accesos/registros creados y conservar datos de usuario conforme al comportamiento observado. No aprobar una instalación que se lleve datos privados dentro del paquete.
5. Rollback: cerrar proceso de la ruta exacta, recuperar binarios anteriores y backup de datos en una copia aislada. Verificar compatibilidad del JSON antes de devolver al usuario. No restaurar una copia que reactive energía o remoto sin revisar su configuración.

Antes de sobrescribir el portable en uso, cerrar únicamente la instancia de esa ruta y registrar hashes de `config.json`/`presets.json`. Compararlos después del publish y antes del relanzamiento. El audit log puede crecer cuando se ejecuta la aplicación; distinguir cambios del uso de cambios hechos por packaging. Para un ZIP de distribución usar una selección explícita de archivos publicados limpios, excluyendo los datos existentes del usuario.

## 9. Evidencias, clasificación y salida de QA

Para cada caso guardar: ID, fecha y zona horaria, binario/hash/diff, condiciones iniciales, pasos mínimos, resultado esperado/real, reproducción n/N, transporte HTTP, métricas, stack/HRESULT cuando exista, cambios de estado e IDs de tareas. Capturas solo anonimizadas. No almacenar claves QR, credenciales de proveedores ni archivos `auth.json` personales.

Clasificar P0: pérdida de datos o energía inesperada; P1: crash/deadlock/zombie/fuga sostenida/auth bypass; P2: estado audio/red equivocado, porcentaje recortado o control inaccesible; P3: defecto cosmético menor. Una saturación deliberada con 429 y recuperación no es P1. Diferenciar fallo de infraestructura (driver/audio/red/permiso) de fallo de recuperación de Nokto.

| Campaña | Estado inicial al entregar este plan | Evidencia requerida para cerrar |
|---|---|---|
| Build Release / publish dual | Registrar salida de esta release | 0 errores/warnings, rutas y hashes |
| Runners automatizados | Registrar ejecución de esta release | Código 0 y detalle por suite |
| T01/T02 carga 5–10 + carreras | Pendiente de campaña QA prolongada | Snapshots estado, cleanup y recursos |
| N01 20 rps / soak / GDI | Pendiente | CSV latencia/status y serie recursos |
| N02 negativos LAN | Suite más campaña física | Respuestas y recuperación |
| A01/A02 audio / retirada física | Pendiente de hardware real | Estado nativo/UI y HRESULT |
| Límites/UI/DPI/disco bajo | Pendiente de matriz completa | Capturas y normalización |
| Instalación/upgrade/uninstall VM | Pendiente | VM limpia, logs de Setup y datos |

Informe final QA: casos aprobados/fallidos/pendientes, hallazgos con severidad, máxima carga estable observada, pendiente de memoria/handles después del enfriamiento y decisión de release con limitaciones concretas. Abrir una regresión automatizada por fallo reproducible antes de marcarlo resuelto; volver a ejecutar solo las campañas afectadas y el conjunto necesario para demostrar que no se alteraron rutinas, energía, audio ni persistencia.

## 10. Evidencia de preparación de esta entrega

Verificación ejecutada el 2026-10-03, zona local UTC−03:00, sobre el árbol de trabajo existente (incluye cambios locales previos; no es un tag inmutable de Git):

- `build Nokto.sln -c Release -p:TreatWarningsAsErrors=true`: código 0, **0 advertencias y 0 errores**.
- `test Nokto.sln -c Release --no-build -p:TreatWarningsAsErrors=true`: código 0. Pasaron runners de concurrencia, LAN/Kestrel, descubrimiento IA, regresiones y bandeja; auto-test nativo **14/14, 0 fallos**.
- Bandeja en harness: 750 refrescos en reposo GDI 39→39 / USER 34→34; 10.000 renders variados GDI 546→546 / USER 203→203. Estos datos no sustituyen el soak de snapshots.
- Ambos publish autocontenidos: código 0, sin warnings/errores. Portable conserva exactamente los hashes de los tres archivos de datos existentes comprobados antes y después de publicar.
- Compilación Inno Setup 6.7.3: correcta, sin warnings, 65,265 s. El compilador oficial usado tuvo firma Authenticode válida de Pyrsys B.V.; SHA-256 del archivo descargado `9C73C3BAE7ED48D44112A0F48E66742C00090BDB5BEF71D9D3C056C66E97B732`.
- Smoke test de copias aisladas, sin datos personales: portable y distribución completa abrieron su ventana con **Nokto — Consola de Operaciones y Control Local**. Se verificaron `AssemblyTitle`, `AssemblyDescription` y metadatos PE. La distribución completa contiene 367 archivos / 149,19 MiB y runtime Microsoft.NETCore.App / Microsoft.AspNetCore.App 8.0.31 incluido. No se instaló Setup en el equipo del usuario.
- Los cuatro bloques PowerShell de este documento pasaron análisis sintáctico; los scripts de carga no se ejecutaron como una campaña de estrés de esta entrega.

Binarios generados y SHA-256:

| Archivo | Bytes | SHA-256 |
|---|---:|---|
| `C:\Users\jorge\Proyectos\Nokto\artifacts\Nokto-Portable-x64\Nokto.exe` | 65229959 | `F804CEE29457B9453E6CE625245D10034C83E76A8792394E5B79BA6DB0E280A3` |
| `C:\Users\jorge\Proyectos\Nokto\artifacts\Nokto-Installer-x64\App\Nokto.exe` | 174080 | `80A2B99CB0F8898C926FDD0FC922DA7EB5C94F94649995FDE1A9E883A8757CA7` |
| `C:\Users\jorge\Proyectos\Nokto\artifacts\Nokto-Installer-x64\Nokto-Setup-x64.exe` | 44984252 | `695CE7C89186BE3F6C78584BB58CCE0EEE9BD045C3780422B92CBB8E9685AC68` |

Manifest de los tres binarios: `artifacts/Nokto-Installer-x64/release-binaries.json`. Cualquier reconstrucción puede cambiar estos hashes; recalcularlos y conservar su relación con el diff antes de iniciar QA. Permanecen pendientes las campañas prolongadas de este protocolo, desconexiones físicas exhaustivas y la matriz de instalación/upgrade/desinstalación en VM limpia.

## 11. Correcciones posteriores al informe QA — 2026-10-04

Se aplicaron BUG-01 a BUG-05 de `QA_REPORT_ANTIGRAVITY.md`: restauración de tray con validación de plataforma/estado de salida y captura de `InvalidOperationException`; cierre condicionado al ajuste persistido y overlay liberado; adquisición GDI dentro de `try` y liberación condicional en `finally`; excepción UIPI restringida a cero entradas con error 5; espera HTTP acotada a 250 ms. Ajustes expone ahora «Minimizar a la bandeja al cerrar la ventana», enlazado al valor real de configuración. Un callback de overlay encolado se ignora una vez iniciada la salida.

Las nuevas regresiones de tray y HTTP reprodujeron los fallos antes de corregirlos. Pasó `dotnet test Nokto.sln -c Release -p:TreatWarningsAsErrors=true`, incluidas las nuevas pruebas de ventana, persistencia de cierre, cola/backpressure y UIPI simulado; otras fallas de SendInput e inyección parcial siguen rechazándose. El auto-test nativo obtuvo 14/14, incluida captura BMP válida. Las pruebas no indujeron agotamiento real de memoria GDI; los caminos de asignación parcial se revisaron en el código.

Portable y distribución completa se republicaron con 0 errores/warnings; Setup se recompiló correctamente en 62,782 s sin warnings. Smoke tests sobre copias aisladas confirmaron arranque y salida completa con código 0 al cerrar con minimizar desactivado. Los tres archivos de datos del portable conservaron sus hashes. Los hashes de la sección 10 documentan la entrega anterior: los actuales se encuentran en `artifacts/Nokto-Installer-x64/release-binaries.json`, y `release-files.sha256` identifica los 367 archivos de la distribución estructurada. El informe original de Antigravity se conserva como evidencia histórica.
