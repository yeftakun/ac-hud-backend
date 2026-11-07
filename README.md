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

## Production bundle Windows

Frontend production dapat disatukan dengan backend dan collector menjadi satu
launcher. Script ini membangun React, mem-publish collector .NET self-contained,
menyertakan runtime Node, lalu menghasilkan bundle portable:

```powershell
.\tools\build-production.ps1
```

Hasilnya berada di `release\`. Jalankan satu file berikut:

```powershell
.\release\AcHud.exe
```

Launcher otomatis menjalankan API backend, menyajikan frontend hasil build, dan
menjalankan `AcTelemetryCollector.exe`. Launcher sekarang berupa aplikasi Windows
dengan window konfigurasi dan system tray. Menutup window akan memindahkannya ke
tray secara default; gunakan menu tray `Exit` untuk benar-benar menghentikan
backend dan collector.

Window launcher menyediakan:

- status backend dan collector;
- Start, Stop, Restart;
- Open Display;
- pengaturan port;
- pilihan auto-start;
- pilihan close-to-tray;
- log proses backend dan collector.

Aplikasi dapat dibuka melalui:

```text
http://localhost:3001
http://<IPv4-laptop>:3001/display
```

Pastikan Assetto Corsa berjalan sebelum atau sesudah launcher. Collector akan
menunggu shared memory AC sampai tersedia. Untuk akses HP melalui Wi-Fi, izinkan
port TCP `3001` pada Windows Firewall.

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
POST /telemetry/patch
GET  /telemetry/events
```

`PUT /telemetry` dipakai oleh halaman data/debugger. `POST /telemetry/ingest` adalah endpoint yang disiapkan untuk collector Assetto Corsa. Keduanya memvalidasi payload flat dan membroadcast payload ke semua koneksi SSE.

`POST /telemetry/patch` disediakan untuk sumber tambahan seperti bridge Custom Shaders Patch (CSP). Payload-nya flat dan hanya perlu berisi field yang disediakan sumber tambahan, misalnya:

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

Backend menggabungkan `POST /telemetry/ingest` sebagai state utama dengan patch CSP. Selama patch CSP masih menerima update dalam `CSP_FRESH_MS` (default 1500 ms), field patch menimpa field dengan key yang sama. Jika bridge berhenti mengirim, backend otomatis kembali ke state collector utama dan mengirim snapshot SSE baru. Frontend tetap hanya membaca satu snapshot telemetry.

TTL dapat diubah saat menjalankan backend:

```powershell
$env:CSP_FRESH_MS = "1000"
npm start
```

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

Kirim patch sumber tambahan:

```powershell
$patch = '{"abs.active":true,"tc.active":false,"engine.oilTemp":104.2}'
Invoke-WebRequest http://localhost:3001/telemetry/patch -Method Post -ContentType 'application/json' -Body $patch
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

Bridge CSP belum dipaksa masuk ke collector shared-memory utama. Ia cukup mengirim field yang tidak tersedia atau tidak akurat dari shared memory standar ke `POST /telemetry/patch` secara berkala. Dokumentasi resmi CSP memang menyediakan input `ABS_INACTION`, `TRACTIONCONTROL_INACTION`, `OIL_TEMPERATURE`, dan `OIL_PRESSURE`, tetapi cara mengambilnya ke proses Windows eksternal tetap perlu disesuaikan dengan versi CSP, mode Content Manager, dan mobil yang dipakai. API sumbernya harus divalidasi pada instalasi pengguna sebelum dibuat adapter permanen.

Adapter Python App yang siap dicoba tersedia di [`csp-bridge/README.md`](csp-bridge/README.md). Karena Python embedded CSP pada instalasi ini tidak menyediakan `socket`, adapter menulis patch ke file lokal yang dipoll backend. Adapter ini memprioritaskan state intervensi ABS/TC dan level setting ABS/TC, tanpa mengubah collector utama.
