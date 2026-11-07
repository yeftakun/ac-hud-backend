# AC HUD Backend

Separate backend and Windows runtime for the AC HUD. The frontend source is in
the companion repository [`ac-hud`](../ac-hud). This repository has three main
responsibilities:

1. Config API for layout profiles.
2. Telemetry ingest and realtime SSE broadcast.
3. The collector boundary for Assetto Corsa original shared memory.

## AC Display Unit v1.0.0

AC Display Unit is developed and published by **Yefta Asyel** under the [MIT
License](LICENSE).

- Repository: <https://github.com/yeftakun/ac-hud>
- Support: <yeftakun34@gmail.com>
- Target: Windows x64
- Runtime requirement: Assetto Corsa original with Content Manager and Custom
  Shaders Patch; tested with CSP `0.2.11`.

The CSP Python bridge is included in the release bundle, but installation into
Assetto Corsa remains a manual action through the launcher so the user can
select the correct AC installation folder.

## Development server

```powershell
npm install
npm start
```

The default server runs at:

```text
http://localhost:3001
```

Node watch mode:

```powershell
npm run dev
```

## Windows production bundle

The frontend from the companion `ac-hud` repository can be bundled with the
backend and collector into one portable Windows application. The script builds
the frontend, publishes the self-contained .NET collector, includes Node.js,
copies branding assets, and produces the launcher:

```powershell
.\tools\build-production.ps1
```

The result is written to `release\`. Run this single file:

```powershell
.\release\AcHud.exe
```

To create both release formats (`.zip` portable package and Windows installer):

```powershell
.\tools\package-release.ps1
```

The artifacts are written to `artifacts\`. The installer creates shortcuts and
adds an inbound Windows Firewall rule for TCP port `3001` on private/domain
networks. Uninstall removes that rule.

The launcher starts the Config/telemetry API, serves the frontend build, and
starts `AcTelemetryCollector.exe`. It is a Windows configuration application
with a system tray icon. Only one launcher instance is allowed; a second launch
shows an alert and exits. The `Exit` action stops the collector and backend
process tree and waits for them to terminate before closing the window.

By default, closing the window moves it to the tray. Use tray `Exit` for a full
shutdown, or disable `Close window to system tray` in the settings. The launcher
provides `Open Home` and `Open Display` shortcuts.

The launcher provides:

- backend and collector status;
- Start, Stop, Restart;
- Open Display;
- port settings;
- Assetto Corsa installation path picker;
- CSP bridge installation/status check;
- auto-start option;
- close-to-tray option;
- application logging to `%LOCALAPPDATA%\\AcHud\\logs\\application.log`;
- `Open Home` and `Open Display` shortcuts.

The launcher window does not show a raw console anymore. Backend, collector, and
launcher messages are written to the application log. Use `Open log` in the
window or the system-tray menu to open it.

The production bundle also includes `csp-bridge\ac_hud_csp_bridge.py`. Select the
Assetto Corsa installation folder in the launcher, then choose `Install / Update
Bridge`. The launcher installs the bridge at the required location:

```text
<Assetto Corsa>\apps\python\ac_hud_csp_bridge\ac_hud_csp_bridge.py
```

After installation, enable `AC HUD Bridge` in Content Manager's Python Apps.
The launcher only installs and checks the file; the Python app still runs inside
Assetto Corsa. The bridge determines the current Windows user's Documents path
automatically and writes the CSP patch to the same default location used by the
backend.

Branding assets are stored in [`assets`](assets). `assets/logo.ico` is embedded
as the executable icon and used by the launcher window/tray. The frontend uses
the PNG/favicon copies maintained in the frontend repository.

The web application is available at:

```text
http://localhost:3001
http://<IPv4-laptop>:3001/display
```

Assetto Corsa may start before or after the launcher. The collector waits for AC
shared memory to become available. For phone access over Wi-Fi, allow TCP port
`3001` through Windows Firewall.

## API endpoints

### Health

```text
GET /health
```

### Config API

```text
GET  /configs
GET  /configs/:id
POST /configs
PUT  /configs/:id
```

Profiles are stored in `data/profiles.json`.

### Telemetry

```text
GET  /telemetry
PUT  /telemetry
POST /telemetry/ingest
POST /telemetry/patch
GET  /telemetry/events
```

`PUT /telemetry` is used by the frontend data/debug page. `POST
/telemetry/ingest` is used by the Assetto Corsa collector. Both validate flat
payloads and broadcast snapshots to all SSE connections.

`POST /telemetry/patch` is provided for additional sources such as the Custom
Shaders Patch (CSP) bridge. Its payload is flat and only needs to contain fields
provided by the additional source, for example:

```json
{
  "abs.active": true,
  "tc.active": false,
  "engine.oilTemp": 104.2,
  "engine.oilPressure": 4.9,
  "engine.waterTemp": 88.1,
  "engine.waterPressure": 1.7
}
```

The backend combines collector state with CSP patches. While a CSP patch has
received an update within `CSP_FRESH_MS` (default 1500 ms), matching patch keys
override collector keys. If the bridge stops sending, the backend returns to
collector state and broadcasts a fresh snapshot. The frontend still reads one
flat telemetry snapshot.

The patch TTL can be changed when starting the backend:

```powershell
$env:CSP_FRESH_MS = "1000"
npm start
```

Telemetry is stored in memory. Restarting the backend restores the baseline from
`data/capture.json`.

## Quick test

```powershell
Invoke-WebRequest http://localhost:3001/health -UseBasicParsing
Invoke-WebRequest http://localhost:3001/configs -UseBasicParsing
```

Send dummy telemetry:

```powershell
$body = '{"speed":210,"rpm":6200,"gear":5,"pit.active":false}'
Invoke-WebRequest http://localhost:3001/telemetry/ingest -Method Post -ContentType 'application/json' -Body $body
```

Send an additional-source patch:

```powershell
$patch = '{"abs.active":true,"tc.active":false,"engine.oilTemp":104.2}'
Invoke-WebRequest http://localhost:3001/telemetry/patch -Method Post -ContentType 'application/json' -Body $patch
```

Test SSE with curl:

```powershell
curl.exe --no-buffer http://localhost:3001/telemetry/events
```

An SSE request remaining `pending` is normal.

## Frontend integration

The companion frontend repository [`ac-hud`](../ac-hud) points to this backend
on port `3001`. For phone access, the backend listens on `0.0.0.0` and Windows
Firewall must allow TCP port `3001`.

The frontend does not need to know whether telemetry comes from `/data` or the
AC collector. Both sources must produce the same flat telemetry schema.

## Assetto Corsa collector

The collector plan and setup are documented in [`collector/README.md`](collector/README.md).
AC original provides physics, graphics, and static shared memory. The collector
reads and normalizes those sources, then sends `POST /telemetry/ingest` locally.

The CSP bridge is kept separate from the main shared-memory collector. It sends
fields that are unavailable or inaccurate in standard shared memory to
`POST /telemetry/patch`. Official CSP inputs include `ABS_INACTION`,
`TRACTIONCONTROL_INACTION`, `OIL_TEMPERATURE`, and `OIL_PRESSURE`, but the
external Windows adapter depends on the installed CSP version, Content Manager
mode, and car. The source API should be validated on the target installation
before making the adapter permanent.

The ready-to-test Python app adapter is documented in
[`csp-bridge/README.md`](csp-bridge/README.md). Because embedded CSP Python on
this installation does not provide `socket`, the adapter writes patches to a
local file polled by the backend. It prioritizes ABS/TC intervention state and
ABS/TC level settings without changing the main collector.
