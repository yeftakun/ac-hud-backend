# AC HUD Backend

Backend terpisah untuk AC HUD. Repo ini memiliki tiga tanggung jawab:

1. Config API untuk profile layout.
2. Telemetry ingest dan broadcast realtime melalui SSE.
3. Boundary collector untuk membaca telemetry Assetto Corsa original.

## Menjalankan

```powershell
npm install
npm start
```

Default server:

```text
http://localhost:3001
```

Mode development dengan Node watch:

```powershell
npm run dev
```

## Endpoint

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

Profile disimpan di `data/profiles.json`.

### Telemetry

```text
GET  /telemetry
PUT  /telemetry
POST /telemetry/ingest
GET  /telemetry/events
```

`PUT /telemetry` dipakai oleh halaman data/debugger. `POST /telemetry/ingest` adalah endpoint yang disiapkan untuk collector Assetto Corsa. Keduanya memvalidasi payload flat dan membroadcast payload ke semua koneksi SSE.

Telemetry disimpan di memori. Restart backend mengembalikan baseline dari `data/capture.json`.

## Uji cepat

```powershell
Invoke-WebRequest http://localhost:3001/health -UseBasicParsing
Invoke-WebRequest http://localhost:3001/configs -UseBasicParsing
```

Kirim telemetry dummy:

```powershell
$body = '{"speed":210,"rpm":6200,"gear":5,"pit.active":false}'
Invoke-WebRequest http://localhost:3001/telemetry/ingest -Method Post -ContentType 'application/json' -Body $body
```

Tes SSE dengan curl:

```powershell
curl.exe --no-buffer http://localhost:3001/telemetry/events
```

Request SSE yang tetap `pending` adalah perilaku normal.

## Integrasi frontend

Frontend `ac-hud` nantinya diarahkan ke host backend ini. Untuk akses dari HP, backend harus listen pada `0.0.0.0` dan firewall Windows harus mengizinkan port `3001`.

Frontend tidak perlu mengetahui apakah telemetry berasal dari halaman `/data` atau collector AC. Keduanya harus menghasilkan schema telemetry flat yang sama.

## Collector Assetto Corsa

Rencana collector berada di [`collector/README.md`](collector/README.md). AC original menyediakan shared memory physics, graphics, dan static. Collector akan membaca sumber tersebut, melakukan normalisasi, kemudian mengirim `POST /telemetry/ingest` ke server lokal.
