# T2 MacBook Pro 16" CPU & Fan Boost (`MacBookPro16,1`)

Lightweight native CPU power-envelope + Apple T2 dual-fan controllers for the **2019 16" MacBook Pro (`MacBookPro16,1`, Intel Core i9-9880H + AMD Radeon Pro 5500M + Apple T2)** across **Windows 11 Boot Camp** (`windows/`) and **macOS** (`macos/`).

Eliminates Apple T2 firmware sawtooth throttling on Boot Camp, unlocks single-thread responsiveness on macOS, and replaces `Macs Fan Control` on both OSes with `0.00%` CPU live **`CPU Core (average)`** tray/menu-bar monitoring and persistent dual-sensor fan curves that **never get erased** when toggling **Full Blast**.

---

## Why OS-Specific Silicon Tuning Is Required on `MacBookPro16,1`

Although both OSes run on the same `i9-9880H` + Apple T2 hardware, their power/thermal firmware paths behave completely differently:

| Hardware Path | Windows 11 Boot Camp (`WinCpuBoost`) | macOS Tahoe (`CpuBoost.app`) |
|---|---|---|
| **Kernel Driver** | WHQL-signed **PawnIO** (`IntelMSR.bin` + `AppleT2Smc.bin`) — zero `WinRing0`, 100% Defender/HVCI-compatible | `VOLTA_DRIVER` (MSR `0x610`/`0x1B0`/`0x1FC`) + native `IOKit` `AppleSMC` |
| **Stock Power Failure Mode** | Boot Camp EFI sets `0x610` to `100W/125W` (`Tau=28s`, `TCC=100°C`). Sustained load hits `82.4–86W` / `100°C`, tripping VRM `PROCHOT#` + **Apple T2 ACPI `_PPC=33%` (`Event ID 37`)**, which locks all 8 cores to **`1.1–1.3 GHz` (`11W`) for 10 seconds**. | XCPM + T2 smoothly clamps sustained power to `~63.5W` after ~8s with `0` `PROCHOT#` hits, **but** defaults `EPB=6` + `C1E=ON` (hurting 1T latency/clocks) and Volta's GUI fails to program silicon `0x610` on newer macOS. |
| **`MAX Boost` CPU Strategy** | **`75W PL1 / 80W PL2` (`Tau=2s`, `Clamp=True`) + `TCC_Offset=3` (`97°C` Intel PID ceiling):** Stays `2.4W` under VRM `PROCHOT#` and `3°C` under T2's `100°C` cliff -> **`+20.5%` higher sustained throughput**, **`3.74 GHz` vs `1.11 GHz` (`+238%`) min clock**, `0` T2 drops. | **`100W PL1 / 125W PL2` + `EPB=0` + `C1E=OFF`:** Lets XCPM + T2 handle the smooth `63.5W` sustained ceiling while unlocking **`+1.7–4.2%` 1-thread throughput** and instant wake. |
| **Display & Idle Thermal Floor** | Boot Camp GMUX disables UHD 630 iGPU; discrete `5500M` stays awake (`~66–70°C` idle). Uses progressive **$r^{1.5}$ curve (`L: CPU 68→88°C | R: 5500M 66→86°C`)** for silent desktop idle. | UHD 630 iGPU drives display; `5500M` powers off (`~42–48°C` idle). Uses **`L: CPU 50→82°C | R: 5500M 50→83°C`** curve. |

---

## Windows 11 Boot Camp (`windows/`)

### Verified 16-Thread Sustained Benchmark (`i9-9880H`, Boot Camp 24H2)

| Configuration | `0x610` (`PL1 / PL2 / Tau`) | `0x1A2` Guard | 30s Throughput | Min Clock (`t>=3s`) | T2 `_PPC` Floor |
|---|---|---|---|---|---|
| Stock Boot Camp EFI | `100W / 125W / 28s` | `100°C` (`0`) | `3,147.1 M-iter/s` | **`1,107 MHz`** (`-68%`) | **`33%`** (Event 37) |
| **`WinCpuBoost` (`MAX Boost`)** | **`75W / 80W / 2s`** | **`97°C` (`3`)** | **`3,698.6 – 3,720.4 M-iter/s` (`+20.5%`)** | **`3,691 – 3,739 MHz` (`+238%`)** | **`100%`** (`0` drops) |

### Features
- **`#000080` Live `CPU Core (average)` Square Tray Icon:** Polls T2 SMC `TC1C`..`TC8C` every 3s (`0.00%` measured CPU overhead; redraws GDI+ icon only when integer °C changes).
- **Windows 11 Dark Context Menu:** DWM rounded corners, live CPU Watts / Effective MHz / Package & 5500M °C / Left & Right RPM / eGPU offload status.
- **2 Fan Modes (Persistent Curve):**
  - `Automatic (L: CPU 68→88°C | R: 5500M 66→86°C)` — progressive $r^{1.5}$ ramp with `40 RPM` deadband.
  - `Full Blast (L: 5616 RPM | R: 5200 RPM)` — 1-click max cooling; toggling never wipes your saved curve.
  - Automatic hardware fail-safe restores T2 firmware control (`F0Md=0, F1Md=0`) on Quit or Windows shutdown.
- **2 CPU Modes:** `CPU: MAX Boost (75W / 80W, 97°C Guard)` and `CPU: Quiet / Eco (45W / 60W, 95°C Guard)`, enforced across sleep/wake and 30s drift checks.

### Quick Start (Windows)
1. Install the WHQL-signed **PawnIO** driver (`~3.3 MB`):
   ```powershell
   winget install --id namazso.PawnIO -e
   ```
2. Extract `WinCpuBoost-windows-x64.zip` from Releases (or run `.\windows\build.ps1 -PackageRelease` with zero external SDKs) and launch `WinCpuBoost.exe`.
3. Enable **Start Silently at Windows Login** in the tray menu (registers an `Interactive` + `Highest` Scheduled Task so it starts at boot without a UAC prompt).

---

## License

Project source code is licensed under the [MIT License](LICENSE). Precompiled PawnIO modules (`IntelMSR.bin`, `AppleT2Smc.bin`) are distributed unmodified under `LGPL-2.1-or-later`; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
