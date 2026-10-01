# 02_UI_UX_SPEC.md — Especificación de Diseño de Interfaz y Experiencia de Usuario

**Proyecto:** Nokto  
**Versión de especificación:** 1.0.0  
**Framework GUI:** Avalonia UI (v11+) con FluentTheme  
**Estilo Visual:** Neominimalista industrial, Windows 11 Fluent Design nativo (Mica/Acrylic), diseño funcional sin degradados artificiales ni ornamentos innecesarios (Anti-AI Slop)  
**Resolución objetivo de escritorio:** Ventana compacta 460x580 px (Modo Rápido) expandible a 820x620 px (Modo Studio)  
**Resolución objetivo móvil (LAN PWA):** Mobile First responsivo (360x640 px a 430x932 px)  

---

## 1. Sistema de Diseño e Identidad Visual

El diseño de Nokto prioriza la legibilidad técnica, la economía de atención y el contraste directo de instrumentación.

### 1.1. Paleta Cromática y Tokens de Color

| Token | Hex | Uso en UI |
| :--- | :--- | :--- |
| `Bg-App-Night` | `#0E0F12` | Fondo principal de ventana (Modo Noche / Reposo). |
| `Bg-App-Day` | `#F4F5F7` | Fondo principal de ventana (Modo Día / Productividad). |
| `Bg-Surface-Night` | `#16181D` | Tarjetas de tareas, paneles de pasos y contenedores. |
| `Bg-Surface-Day` | `#FFFFFF` | Tarjetas en modo día con borde sutil. |
| `Border-Subtle` | `#262930` | Separadores y bordes de tarjetas (1 px sólido). |
| `Accent-Cyan` | `#00D2FF` | Anillo de progreso en curso, botones de acción primaria y badges técnicos. |
| `Accent-Amber` | `#FFB300` | Avisos de advertencia, cuenta atrás de gracia (< 60s) y alertas leves. |
| `Accent-Green` | `#00E676` | Estado activo "Modo Trabajo" (Keep-Alive) y confirmación de éxito. |
| `Accent-Red` | `#FF4B4B` | Botón "Abortar", error en scripts o cancelación inmediata. |
| `Text-Primary` | `#F0F2F5` | Encabezados, tiempos y cifras principales. |
| `Text-Muted` | `#7D8390` | Etiquetas secundarias, rutas de archivos y métricas pasivas. |

### 1.2. Tipografía e Iconografía de Precisión
* **Familia tipográfica:** Segoe UI Variable (en Windows 11) o Inter / System Sans fallback.
* **Cifras de temporizador:** `FontFeatureSettings = "tnum"` (números tabulares monoespaciados para evitar saltos de ancho al correr los segundos).
* **Iconografía:** Geometría lineal pura con trazo constante de 1.5 px. Prohibidos iconos genéricos tipo chispas/estrellas mágicas o degradados púrpuras.
  * *Modo Día:* Sol geométrico circular (círculo limpio con 4 marcas cardinales de 1 px).
  * *Modo Noche:* Luna estilizada en corte geométrico.
  * *Confirmación / Éxito:* Rombo sólido o check lineal recto sin círculo envolvente.
  * *Estado Terminal:* Símbolo cuadrado de parada (`▪`) o anillo concéntrico.

---

## 2. Ventana Principal: Arquitectura de Doble Vista

La aplicación ofrece dos niveles de interacción según el perfil de uso:

### 2.1. Vista Simple (Modo Rápido / 1-Clic)
Diseñada para iniciar tareas cotidianas en dos segundos. Dimensiones: 460 px (ancho) x 580 px (alto).

```text
┌────────────────────────────────────────────────────────┐
│  [◐] Nokto                   [● Listo]   [─] [□] [✕]   │
├────────────────────────────────────────────────────────┤
│                                                        │
│   ┌────────────────────────┐  ┌────────────────────┐   │
│   │  ☀️ MODO TRABAJO       │  │  🌙 MODO DORMIR    │   │
│   │  Mantener activo en    │  │  Apagar en 45 min  │   │
│   │  Teams / Slack         │  │  con fade audio    │   │
│   │  [ Iniciar Modo ]      │  │  [ Iniciar Modo ]  │   │
│   └────────────────────────┘  └────────────────────┘   │
│                                                        │
│   ┌────────────────────────┐  ┌────────────────────┐   │
│   │  🎬 FIN DE TAREA       │  │  ⚡ APAGADO RÁPIDO │   │
│   │  Apagar al terminar    │  │  Temporizador:     │   │
│   │  proceso o descarga    │  │  [30m] [1h] [2h]   │   │
│   │  [ Configurar ]        │  │  [ Iniciar ]       │   │
│   └────────────────────────┘  └────────────────────┘   │
│                                                        │
│  ────────────────────────────────────────────────────  │
│  ESTADO ACTUAL: En reposo                              │
│                                                        │
├────────────────────────────────────────────────────────┤
│  [⚙ Modo Studio / Flujos]        [ 📱 Control LAN QR ] │
└────────────────────────────────────────────────────────┘
```

#### Especificación de Controles en Vista Simple
1. **Tarjeta "Modo Trabajo":**
   * Al pulsar, el botón conmuta a color verde (`#00E676`) mostrando `"Activo (Jitter ON)"`.
   * Muestra un botón secundario discreto: `[ Detener ]`.
2. **Tarjeta "Modo Dormir":**
   * Dispara una cuenta atrás predeterminada de 45 minutos. Activa el desvanecimiento de audio en los últimos 10 minutos y ejecuta apagado de monitores 5 minutos antes de la suspensión final.
3. **Tarjeta "Fin de Tarea":**
   * Abre un pequeño desplegable con los procesos en ejecución detectados en el sistema con mayor consumo de CPU o memoria (ej. `blender.exe`, `handbrake.exe`, `qbittorrent.exe`).
4. **Tarjeta "Apagado Rápido":**
   * Tres botones chips de acceso rápido (`30m`, `1h`, `2h`) y un input numérico manual.
5. **Botón de conmutación:**
   * Ubicado en la barra inferior izquierda para expandir la ventana a **Modo Studio**.

---

### 2.2. Vista Avanzada (Modo Studio / Constructor de Flujos)
Se activa al expandir la ventana. Dimensiones: 820 px (ancho) x 620 px (alto).

```text
┌──────────────────────────────────────────────────────────────────────────┐
│  [◐] Nokto Studio                          [▶ Render 3D]     [─] [□] [✕] │
├────────────────┬─────────────────────────────────────────────────────────┤
│  PRESETS       │  Flujo Activo: "Compilación & Apagado Nocturno"         │
│                │                                                         │
│  ⭐ Favoritos   │  [BLOQUE 1: DISPARADOR PRINCIPAL]                       │
│  • Render 3D   │  ┌───────────────────────────────────────────────────┐  │
│  • Descarga 4K │  │ Tipo: Fin de Proceso                              │  │
│  • Workday     │  │ Proceso: [ blender.exe                      ▼ ]   │  │
│                │  │ Condición: CPU < [ 8% ] durante [ 90 seg ]        │  │
│  📂 Personal   │  └───────────────────────────────────────────────────┘  │
│  • Backup NAS  │                                                         │
│  • Sleep Audio │  [BLOQUE 2: ACCIÓN INTERMEDIA]                          │
│                │  ┌───────────────────────────────────────────────────┐  │
│  [ + Nuevo ]   │  │ Tipo: Captura de Pantalla                         │  │
│                │  │ Ruta: ~/Pictures/NoktoLogs/ [ Estampar Métricas ] │  │
│                │  └───────────────────────────────────────────────────┘  │
│                │                                                         │
│                │  [BLOQUE 3: ACCIÓN TERMINAL]                            │
│                │  ┌───────────────────────────────────────────────────┐  │
│                │  │ Tipo: Apagado del Sistema                         │  │
│                │  │ Modo: [ Forzado (Ignorar apps colgadas)     ▼ ]   │  │
│                │  │ Gracia previa: [ 60 segundos ]                    │  │
│                │  └───────────────────────────────────────────────────┘  │
│                │                                                         │
│                │  [ + Añadir Paso ]        [ 💾 Guardar ] [ ▶ Ejecutar ] │
├────────────────┴─────────────────────────────────────────────────────────┤
│  CPU: 4.1% │ GPU: 0% │ Red: 24 KB/s │ Audio: Silencio (03:12) │ Tray: Activo │
└──────────────────────────────────────────────────────────────────────────┘
```

#### Especificación de Controles en Modo Studio
* **Panel Izquierdo (Presets):** Lista de flujos guardados localmente (`presets.json`). Permite arrastrar, duplicar, renombrar o exportar perfiles mediante menú contextual (clic derecho).
* **Editor Central Secuencial:** Cada tarjeta representa un nodo ejecutable con botones `[▲]` `[▼]` para reordenar pasos y `[✕]` para eliminar.
* **Barra de Métricas Inferior (Hardware Monitor):**
  * Se actualiza pasivamente cada 2 segundos.
  * Muestra el uso de CPU/GPU, tasa de transferencia de red combinada y estado del mixer de audio (detectando minutos continuos de silencio).

---

## 3. Ventana de Gracia Flotante (Grace Period Overlay)

Componente crítico de seguridad que previene apagados accidentales.

### 3.1. Propiedades de la Ventana en Avalonia
* `WindowStyle = WindowStyle.None`
* `ShowInTaskbar = false`
* `Topmost = true`
* `CanResize = false`
* `Background = Transparent`
* `Size = 380x110 px`
* **Posicionamiento:** Anclada a la esquina superior derecha (X: `Screen.Bounds.Width - 400`, Y: `30`).

```text
┌────────────────────────────────────────────────────────┐
│  🌙 Nokto: Apagando en 00:48 s...            [✕ Cerrar]│
│  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━────────────── 68%        │
│                                                        │
│  [ +10 Min (Espacio) ]              [ Cancelar (Esc) ] │
└────────────────────────────────────────────────────────┘
```

### 3.2. Comportamiento Interactivo
* **Barra de Progreso:** Trazo lineal con decremento suave en color ámbar (`#FFB300`).
* **Atajos de teclado vinculados:**
  * Tecla `Escape`: Cancela el apagado de inmediato, oculta el overlay y devuelve el flujo a reposo.
  * Tecla `Espacio`: Suma 600 segundos (10 minutos) a la cuenta atrás y oculta el overlay hasta el siguiente umbral.
* **Elevación sobre pantalla completa:** Si hay una película, presentación o juego en ejecución, la ventana permanece visible en la capa superior (`HWND_TOPMOST`).

---

## 4. Renderizado Dinámico en la Bandeja del Sistema (System Tray)

El icono no es un archivo `.ico` estático; se dibuja en memoria mediante un canvas de 32x32 píxeles a través de `System.Drawing.Graphics` o SkiaSharp (`SKCanvas`) y se asigna al manejador del TrayIcon de Avalonia.

### 4.1. Estados Visuales del Icono

```text
     REPOSO                EN PROGRESO (65%)             COMPLETADO
   ┌─────────┐               ┌─────────┐                ┌─────────┐
   │         │               │   ░░░   │                │         │
   │  ┌───┐  │               │ ░     █ │                │   ◆◆    │
   │  └───┘  │               │ ░  ●  █ │                │   ◆◆    │
   │         │               │   ███   │                │         │
   └─────────┘               └─────────┘                └─────────┘
  Glifo Blanco               Anillo progreso             Rombo sólido
   Monocromo                 + Punto pulsante             Confirmación
```

* **Estado 1: Reposo**
  * Fondo transparente.
  * Glifo geométrico de Nokto trazado en blanco puro (`#FFFFFF`).
* **Estado 2: En Ejecución / Progreso**
  * **Anillo exterior:** Diámetro de 28 px centrado en coordenadas (16,16). Ancho de trazo: 2.5 px.
  * **Pista de fondo:** Círculo completo en `#262930` (gris oscuro).
  * **Arco de progreso:** Dibujado en sentido horario desde los -90° (posición de las 12 en punto) con longitud proporcional al porcentaje:
    $$\text{Barrido en grados} = \left(\frac{\text{TiempoTranscurrido}}{\text{TiempoTotal}}\right) \cdot 360^\circ$$
  * **Color del arco:** Cian técnico (`#00D2FF`). Cambia automáticamente a Ámbar (`#FFB300`) cuando restan menos de 120 segundos.
  * **Centro:** Punto central de 3x3 px que parpadea sutilmente con una interpolación alfa senoidal de 1 Hz.
* **Estado 3: Completado / Éxito**
  * El anillo exterior desaparece.
  * Glifo central: Rombo sólido de 10x10 px en color Verde Neón (`#00E676`) durante 5 segundos antes de retornar a reposo.

### 4.2. Menú Contextual del Tray (Clic Derecho)
```text
┌──────────────────────────────────────────┐
│  Nokto: Render 3D (00:24:12 restante)    │  <-- Encabezado inactivo
├──────────────────────────────────────────┤
│  ▶ Reanudar / ⏸ Pausar                   │
│  ⏱ +15 Minutos                           │
│  🌙 Apagar Monitores Ahora               │
├──────────────────────────────────────────┤
│  ⛔ Abortar Flujo Activo                 │
├──────────────────────────────────────────┤
│  Abrir Ventana Principal                 │
│  Salir de Nokto                          │
└──────────────────────────────────────────┘
```

---

## 5. Dashboard Móvil Local (LAN Webapp / PWA)

Servido directamente desde la memoria RAM del ejecutable por el microservidor HTTP embebido en el puerto local asignado (por defecto `4884`).

### 5.1. Flujo de Emparejamiento por QR
1. El usuario hace clic en `[ 📱 Control LAN QR ]` en la app de escritorio.
2. Se abre un diálogo modal con:
   * Código QR con corrección de error nivel M generado mediante librería local (ej. `QRCoder`).
   * Contenido del QR: `http://[IP_LOCAL_PC]:4884/?auth=[TOKEN_HEX]`
   * Instrucción: *"Escanea con la cámara de tu móvil conectado al mismo Wi-Fi"*.
   * Botón para copiar la URL manualmente.

### 5.2. Wireframe de la Interfaz Web Móvil
Optimizada para uso con una sola mano en pantallas OLED (fondo negro absoluto `#000000`).

```text
┌──────────────────────────────────────┐
│  ◐ Nokto Remote         [🟢 En línea]│
│  Estación: PC-TRABAJO                │
├──────────────────────────────────────┤
│                                      │
│               00:32:10               │
│           TIEMPO RESTANTE            │
│                                      │
│  Tarea activa:                       │
│  "Render Nocturno Blender"           │
│  Estado: CPU < 8% (Confirmando 42s)  │
│                                      │
│  Métricas en vivo:                   │
│  CPU: 84% ▓▓▓▓▓▓▓▓▓░░░               │
│  GPU: 91% ▓▓▓▓▓▓▓▓▓▓░░               │
│                                      │
│  ┌────────────────────────────────┐  │
│  │    📸 VER CAPTURA DE PANTALLA  │  │
│  └────────────────────────────────┘  │
│                                      │
│  ┌─────────────────┐┌────────────────┐│
│  │     +10 MIN     ││    +30 MIN     ││
│  └─────────────────┘└────────────────┘│
│  ┌────────────────────────────────┐  │
│  │       ⛔ ABORTAR TAREA         │  │
│  └────────────────────────────────┘  │
│                                      │
│  Controles Rápidos del PC:           │
│  • [ 🌙 Apagar Monitores ]           │
│  • [ ⚡ Forzar Apagado Ahora ]       │
│                                      │
└──────────────────────────────────────┘
```

### 5.3. Interacciones Específicas del Móvil
* **Haptic Feedback:** Cada pulsación de botón invoca `navigator.vibrate(25)` en navegadores compatibles.
* **Visor de Pantalla:** Al presionar *"Ver Captura"*, solicita el endpoint `/api/screen-preview`. Despliega una capa modal a pantalla completa con la imagen comprimida en WebP a calidad 60% (~120 KB) con soporte de zoom táctil (pinch-to-zoom).
* **Fase Crítica de Gracia:** Si el PC entra en los últimos 60 segundos antes de apagar, el fondo de la pantalla del móvil parpadea con un borde ámbar pulsante y emite una vibración continuada, destacando el botón gigante rojo: `CANCELAR APAGADO`.