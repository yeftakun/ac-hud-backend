# AC Telemetry Collector

Collector ini adalah boundary khusus untuk Assetto Corsa original. Collector berjalan di PC yang menjalankan game, membaca shared memory AC, menormalisasi data ke schema flat, lalu mengirimkannya ke backend.

Shared memory AC yang menjadi sumber utama:

- `acpmf_physics`
- `acpmf_graphics`
- `acpmf_static`

Target endpoint collector:

```text
POST http://127.0.0.1:3001/telemetry/ingest
```

Payload harus berupa object flat dengan primitive values, misalnya:

```json
{
  "speed": 180,
  "rpm": 4500,
  "gear": 4,
  "fuel": 42.5,
  "lap.current": 42.123,
  "pit.active": false
}
```

## Menjalankan collector

Dari folder `collector`:

```powershell
dotnet run --project .\AcTelemetryCollector.csproj
```

Variabel lingkungan opsional:

```powershell
$env:AC_HUD_BACKEND_URL = "http://127.0.0.1:3001/telemetry/ingest"
$env:AC_HUD_PUBLISH_INTERVAL_MS = "33"
dotnet run --project .\AcTelemetryCollector.csproj
```

Heuristic ABS/TC dapat diaktifkan untuk eksperimen, tetapi default-nya mati agar lampu tidak menyala sebelum ada indikasi intervensi yang benar-benar tersedia:

```powershell
$env:AC_HUD_ENABLE_ACTIVATION_HEURISTIC = "true"
```

Collector akan retry menunggu shared memory saat Assetto Corsa belum dibuka. Tekan `Ctrl+C` untuk berhenti.

Field yang tidak tersedia langsung dari shared memory standar harus diberi mapper/derivation terpisah, terutama `abs.active`, `tc.active`, dan data temperatur/tekanan engine. Collector membulatkan speed, memetakan `PerformanceMeter` ke `delta`, memformat gear menjadi `R`/`N`/angka, dan menghitung estimasi jumlah lap dari konsumsi fuel per lap. Heuristic ABS/TC tersedia opt-in melalui environment variable, tetapi default-nya mati.

Pastikan Shared Memory diaktifkan pada konfigurasi Assetto Corsa sebelum pengujian. Collector menggunakan library open-source yang disimpan di `vendor/AssettoCorsaSharedMemory`; lisensinya ikut tersedia di folder vendor.
