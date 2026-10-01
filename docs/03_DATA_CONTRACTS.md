# 03_DATA_CONTRACTS.md — Especificación de Contratos de Datos, Esquemas y APIs

**Proyecto:** Nokto  
**Versión de especificación:** 1.0.0  
**Serialización:** `System.Text.Json` con Generación de Código en Tiempo de Compilación (`JsonSerializerContext` compatible con Native AOT / Single-File)  
**Codificación:** UTF-8 estricto sin BOM  

---

## 1. Estrategia de Persistencia y Resolución de Rutas

Nokto opera bajo dos modos de almacenamiento según el contexto de despliegue:

### 1.1. Regla de Detección de Modo Portable
Al arrancar, la aplicación ejecuta la siguiente comprobación de inicialización:
1. Verifica si existe un archivo denominado `portable.lock` o `config.json` en el mismo directorio del binario (`AppDomain.CurrentDomain.BaseDirectory`).
2. **Si existe (Modo Portable):** Establece el directorio base de datos en `./data/` adyacente al ejecutable. Si la carpeta no existe, la crea.
3. **Si no existe (Modo Instalador):** Establece el directorio base de datos en `%APPDATA%\Nokto\`.

```text
Directorio Base/
├── Nokto.exe
├── portable.lock            <-- Bandera de activación portable
└── data/
    ├── config.json          <-- Configuración global de la aplicación
    ├── presets.json         <-- Catálogo de rutinas y flujos encadenados
    ├── audit.jsonl          <-- Registro histórico secuencial de ejecuciones
    └── snapshots/           <-- Capturas de pantalla diagnósticas (.webp / .png)
```

---

## 2. Esquema de Configuración Global (`config.json`)

Almacena las preferencias del sistema, interfaz y seguridad del microservidor LAN.

```json
{
  "$schema": "[https://raw.githubusercontent.com/nokto/schemas/v1/config.schema.json](https://raw.githubusercontent.com/nokto/schemas/v1/config.schema.json)",
  "version": 1,
  "app": {
    "theme": "Night",
    "minimizeToTrayOnClose": true,
    "startWithWindows": false,
    "gracePeriodSeconds": 60,
    "panicHotkey": "Control+Shift+F12",
    "metricsPollingIntervalMs": 2000
  },
  "keepAlive": {
    "defaultMode": "InputSimulation",
    "jitterMinSeconds": 45,
    "jitterMaxSeconds": 105,
    "simulatedKey": "VK_F15",
    "mouseDeltaPixels": 1
  },
  "lanServer": {
    "enabled": false,
    "port": 4884,
    "bindAddress": "0.0.0.0",
    "requireAuth": true,
    "authToken": "a9f82d1c6e4b8a73",
    "allowScreenPreview": true,
    "screenPreviewQuality": 60
  }
}
```

---

## 3. Esquema de Definición de Flujos (`presets.json`)

Estructura formal para modelar tareas individuales y flujos encadenados paso a paso.

```json
{
  "$schema": "[https://raw.githubusercontent.com/nokto/schemas/v1/presets.schema.json](https://raw.githubusercontent.com/nokto/schemas/v1/presets.schema.json)",
  "version": 1,
  "presets": [
    {
      "id": "preset_workday_keepalive",
      "name": "Jornada Laboral Anti-Ausente",
      "description": "Mantiene el estado activo en Teams y bloquea la estación a las 18:00.",
      "isFavorite": true,
      "icon": "Sun",
      "trigger": {
        "type": "Schedule",
        "parameters": {
          "daysOfWeek": ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"],
          "startTime": "09:00:00",
          "endTime": "18:00:00"
        }
      },
      "pipeline": [
        {
          "stepOrder": 1,
          "actionType": "KeepAliveEngine",
          "parameters": {
            "mode": "Mixed",
            "jitter": true
          },
          "ignoreFailure": false
        }
      ],
      "terminalAction": {
        "type": "LockStation",
        "parameters": {
          "gracePeriodSeconds": 30
        }
      }
    },
    {
      "id": "preset_blender_night_render",
      "name": "Render Nocturno Blender",
      "description": "Supervisa blender.exe; al terminar toma captura de evidencia y apaga el PC.",
      "isFavorite": true,
      "icon": "Movie",
      "trigger": {
        "type": "ProcessExit",
        "parameters": {
          "processName": "blender.exe",
          "debounceSeconds": 10,
          "cpuLoadThreshold": 8.0,
          "sustainedSeconds": 60
        }
      },
      "pipeline": [
        {
          "stepOrder": 1,
          "actionType": "CaptureScreenshot",
          "parameters": {
            "outputFolder": "./data/snapshots/",
            "stampMetadata": true,
            "format": "WebP",
            "quality": 80
          },
          "ignoreFailure": true
        },
        {
          "stepOrder": 2,
          "actionType": "AudioFadeOut",
          "parameters": {
            "durationSeconds": 30,
            "targetVolumePercentage": 0
          },
          "ignoreFailure": true
        }
      ],
      "terminalAction": {
        "type": "Shutdown",
        "parameters": {
          "forced": true,
          "gracePeriodSeconds": 60
        }
      }
    }
  ]
}
```

### 3.1. Tipos de Disparadores y Acciones Válidos

#### Enumeración `TriggerType`
* `Countdown`: Parámetro `durationSeconds` (int).
* `FixedTime`: Parámetro `targetTime` (formato ISO `HH:mm:ss`).
* `Schedule`: Parámetros `daysOfWeek` (array de strings), `startTime`, `endTime`.
* `ProcessExit`: Parámetros `processName` (string), `debounceSeconds` (int), `cpuLoadThreshold` (float opcional).
* `SustainedLoad`: Parámetros `targetMetric` ("CPU" | "GPU"), `thresholdPercentage` (float), `durationSeconds` (int).
* `NetworkThroughput`: Parámetros `direction` ("Download" | "Upload" | "Both"), `thresholdKBs` (float), `durationSeconds` (int).
* `AudioSilence`: Parámetro `silenceThresholdSeconds` (int).
* `UserIdle`: Parámetro `idleMinutes` (int).

#### Enumeración `ActionType` (Intermedias)
* `CaptureScreenshot`: Parámetros `outputFolder` (string), `stampMetadata` (bool), `format` ("WebP" | "Png"), `quality` (int).
* `AudioFadeOut`: Parámetros `durationSeconds` (int), `targetVolumePercentage` (int).
* `MuteAudio`: Parámetro `muted` (bool).
* `MediaControl`: Parámetro `command` ("Pause" | "Stop").
* `TurnOffMonitors`: Sin parámetros adicionales.
* `ExecuteCommand`: Parámetros `executablePath` (string), `arguments` (string), `timeoutSeconds` (int), `expectedExitCode` (int).
* `KeepAliveEngine`: Parámetros `mode` ("InputSimulation" | "ThreadExecutionState" | "Mixed"), `jitter` (bool).

#### Enumeración `TerminalActionType`
* `Shutdown`: Parámetros `forced` (bool), `gracePeriodSeconds` (int).
* `Sleep`: Parámetro `gracePeriodSeconds` (int).
* `Hibernate`: Parámetro `gracePeriodSeconds` (int).
* `Restart`: Parámetros `forced` (bool), `gracePeriodSeconds` (int).
* `LockStation`: Parámetro `gracePeriodSeconds` (int).
* `Logoff`: Parámetros `forced` (bool), `gracePeriodSeconds` (int).
* `None`: Finaliza el flujo sin alterar el estado energético del sistema operativo.

---

## 4. Contrato de Estado en Tiempo Real (Live System State)

Representa el estado reactivo del motor de ejecución. Este payload se emite cada segundo hacia la UI de Avalonia y los clientes conectados al microservidor LAN.

### 4.1. Definición JSON de `SystemStatusState`
```json
{
  "timestamp": "2026-10-01T21:40:15.120Z",
  "engineState": "Running",
  "activePresetId": "preset_blender_night_render",
  "activePresetName": "Render Nocturno Blender",
  "currentPhase": "TriggerEvaluation",
  "progressPercentage": 74.5,
  "timeRemainingSeconds": 182,
  "gracePeriodActive": false,
  "gracePeriodRemainingSeconds": 0,
  "metrics": {
    "cpuUsagePercentage": 4.2,
    "gpuUsagePercentage": 0.0,
    "networkDownKBs": 12.4,
    "networkUpKBs": 1.1,
    "audioSilenceDurationSeconds": 240,
    "userIdleSeconds": 480
  },
  "monitoredProcess": {
    "name": "blender.exe",
    "isRunning": false,
    "lastSeenSecondsAgo": 8
  },
  "keepAliveActive": false
}
```

### 4.2. Estados del Motor (`engineState`)
* `Idle`: Sin flujo activo; la app monitorea métricas pasivas.
* `WaitingTrigger`: Flujo activado, evaluando condiciones de entrada (procesos, red, temporizador).
* `ExecutingActions`: Disparador cumplido; ejecutando las acciones intermedias en serie.
* `GracePeriod`: Acciones intermedias finalizadas; transcurriendo la cuenta atrás final cancelable.
* `Paused`: Flujo suspendido temporalmente por el usuario.
* `Completed`: Flujo finalizado con éxito.
* `Failed`: Flujo abortado debido a un error en una acción intermedia no opcional.

---

## 5. Especificación de la API HTTP Embebida (LAN Server)

El microservidor opera mediante `System.Net.HttpListener` dentro del propio ejecutable.

### 5.1. Reglas de Autenticación
* Si `lanServer.requireAuth` es `true`, todas las peticiones deben suministrar el token mediante:
  * Encabezado HTTP: `X-Nokto-Auth: <token>`
  * O parámetro Query string: `?auth=<token>`
* Las peticiones que no presenten el token devuelven inmediatamente código `401 Unauthorized` con payload JSON vacío.

### 5.2. Catálogo de Endpoints

#### `GET /`
* **Descripción:** Sirve la Progressive Web App (PWA) de una sola página en memoria (HTML/CSS/JS minificado).
* **Content-Type:** `text/html; charset=utf-8`

#### `GET /api/status`
* **Descripción:** Devuelve el snapshot actual de `SystemStatusState`.
* **Content-Type:** `application/json`
* **Código de respuesta:** `200 OK`

#### `GET /api/screen-preview`
* **Descripción:** Genera una captura de pantalla del escritorio virtual, la escala a un ancho máximo de 1280 px y la comprime en memoria.
* **Content-Type:** `image/webp` (o `image/jpeg` como fallback).
* **Parámetros query opcionales:** `quality` (int, default: 60).
* **Código de respuesta:** `200 OK`

#### `POST /api/action/postpone`
* **Descripción:** Añade tiempo a la tarea o periodo de gracia actual.
* **Payload de entrada:**
  ```json
  {
    "seconds": 600
  }
  ```
* **Código de respuesta:** `200 OK` | `400 Bad Request`

#### `POST /api/action/abort`
* **Descripción:** Interrumpe de inmediato cualquier flujo activo, cancela el temporizador de gracia y devuelve el motor a estado `Idle`.
* **Payload de entrada:** No requerido.
* **Código de respuesta:** `200 OK`

#### `POST /api/action/quick-power`
* **Descripción:** Ejecuta una acción de energía directa e inmediata sin requerir un flujo completo.
* **Payload de entrada:**
  ```json
  {
    "action": "TurnOffMonitors",
    "force": false
  }
  ```
  *Valores válidos para `action`:* `"TurnOffMonitors"`, `"Shutdown"`, `"Sleep"`, `"Restart"`, `"LockStation"`.
* **Código de respuesta:** `200 OK` | `400 Bad Request`

---

## 6. Registro Histórico de Auditoría (`audit.jsonl`)

Cada finalización, aborto o fallo de un flujo se registra como una línea JSON delimitada por salto de línea (`.jsonl`) para lectura secuencial sin cargar el archivo completo en memoria:

```json
{"timestamp":"2026-10-01T21:45:00.002Z","presetId":"preset_blender_night_render","status":"Success","executionDurationSeconds":1420,"triggerFired":"ProcessExit:blender.exe","terminalActionExecuted":"Shutdown","snapshotFile":"snapshots/20261001_214500_blender.webp","exitNotes":"All pipeline steps completed gracefully."}
{"timestamp":"2026-10-01T22:15:10.840Z","presetId":"preset_backup_nas","status":"Failed","executionDurationSeconds":35,"triggerFired":"Countdown","terminalActionExecuted":"None","snapshotFile":null,"exitNotes":"Step 1 (ExecuteCommand: robocopy.exe) returned exit code 8. Aborted terminal action."}
```

---

## 7. Contexto de Serialización AOT para el Agente (C#)

Para asegurar compatibilidad con compilación nativa AOT (`PublishAot = true`) y empaquetado autocontenido sin reflexión en tiempo de ejecución, el agente debe definir explícitamente el siguiente contexto de serialización en el proyecto:

```csharp
using System.Text.Json.Serialization;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(PresetsFile))]
[JsonSerializable(typeof(SystemStatusState))]
[JsonSerializable(typeof(AuditLogEntry))]
[JsonSerializable(typeof(QuickPowerRequest))]
[JsonSerializable(typeof(PostponeRequest))]
public partial class NoktoJsonContext : JsonSerializerContext
{
}
```

El motor de persistencia y la API HTTP embebida deben utilizar únicamente instancias de `NoktoJsonContext.Default` al invocar `JsonSerializer.Serialize` o `JsonSerializer.Deserialize`.