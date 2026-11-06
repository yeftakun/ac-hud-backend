# AC HUD CSP Bridge

Python app untuk Assetto Corsa + Custom Shaders Patch. App ini membaca state
mobil pemain melalui API CSP dan mengirim patch kecil ke backend AC HUD.

Field yang dikirim:

```json
{
  "abs.active": false,
  "tc.active": false,
  "abs": 3,
  "tc": 4
}
```

`abs.active` dan `tc.active` berasal dari state intervensi CSP, bukan dari
level ABS/TC setup atau heuristik slip. `abs` dan `tc` berasal dari state
setting yang dibaca CSP. Oil/water sengaja tidak dikirim karena bukan prioritas
driver HUD saat ini.

## Instalasi manual

1. Pastikan backend AC HUD berjalan di PC yang sama:

   ```powershell
   cd D:\code\ac-hud-backend
   npm start
   ```

2. Salin file [`ac_hud_csp_bridge.py`](ac_hud_csp_bridge.py) ke folder Python
   Apps Assetto Corsa, biasanya:

   ```text
   C:\Program Files (x86)\Steam\steamapps\common\assettocorsa\apps\python\ac_hud_csp_bridge\ac_hud_csp_bridge.py
   ```

   Jika instalasi Steam berada di drive/folder lain, gunakan folder
   `assettocorsa\apps\python` milik instalasi tersebut.

3. Di Content Manager, aktifkan Python Apps untuk Assetto Corsa.

4. Jalankan AC, buka Python Apps, lalu aktifkan `AC HUD Bridge`.

5. Biarkan collector .NET tetap berjalan seperti biasa. Bridge ini hanya
   mengirim field tambahan; ia tidak menggantikan collector.

## Konfigurasi backend lain

Jika backend tidak berjalan di PC yang sama, ubah konstanta berikut di bagian
atas `ac_hud_csp_bridge.py`, misalnya:

```python
BACKEND_HOST = "10.169.143.2"
BACKEND_PORT = 3001
```

Pastikan Windows Firewall mengizinkan port `3001`.

## Cara menguji

- Saat app aktif, label berubah menjadi `CSP bridge: connected`.
- Tekan rem atau gas sampai CSP benar-benar mengintervensi ABS/TC.
- `abs.active`/`tc.active` di HUD akan mengikuti status intervensi.
- Jika bridge dihentikan, backend akan menandai patch stale setelah TTL dan
  kembali ke collector utama.

App ini memerlukan CSP yang menyediakan fungsi `ext_getStateAbsActive`,
`ext_getStateTcActive`, `ext_getStateAbs`, dan `ext_getStateTc`. Pada CSP lama,
fungsi yang tidak tersedia akan dilewati tanpa menghentikan collector utama.
