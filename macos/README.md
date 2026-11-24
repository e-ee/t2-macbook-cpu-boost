# macOS `CpuBoost.app` Directory & Parity Spec

This directory holds the macOS status-bar counterpart (`CpuBoost.app`) for the 2019 16" MacBook Pro (`MacBookPro16,1`, `i9-9880H` + `Radeon Pro 5500M` + Apple T2).

## Cross-OS Design Parity (Tailored to macOS Silicon & XCPM Differences)

1. **Unified CPU + Integrated AppleSMC Fan Control (Replaces Free `Macs Fan Control`):**
   - Live **`CPU Core (average)` °C** (`TC1C`..`TC8C` `sp78` mean) in the macOS menu bar, updated on a lightweight 3s timer (`0.0%` CPU) without redrawing when integer °C is unchanged.
   - **2 Fan Modes (never clears saved sensor curve when toggling Full Blast):**
     - `Fans: Automatic (L: CPU 50→82°C | R: 5500M 50→83°C)` — kept at `50→82°C` / `50→83°C` on macOS (unlike Boot Camp's `68→88°C` / `66→86°C`) because macOS powers down the discrete `5500M` on the Intel UHD 630 iGPU and idles at `~42–48°C` (silent below `50°C`).
     - `Fans: Full Blast (L: 5616 RPM | R: 5200 RPM)` — 1-click max cooling (`F0Md=1, F1Md=1`).
   - **Quit / Shutdown Fail-Safe:** Restores `F0Md=0, F1Md=0` on app quit or macOS termination so Apple T2 resumes hardware fan control.

2. **2 Streamlined CPU Power Modes (macOS XCPM + T2 Focused):**
   - **`CPU: MAX Boost (100W / 125W, EPB=0, C1E=OFF)` [Default]:** Unlike Windows Boot Camp (which lacks XCPM and needs `75W/80W + 97°C TCC_Offset=3` to block T2's `_PPC=33%` `1.1 GHz` cliff), macOS Tahoe's XCPM + T2 smoothly clamps sustained all-core load at `~63.5W` (`0` `PROCHOT#` hits), so `100W/125W + EPB=0 + C1E=OFF` delivers full `+1.7–4.2%` 1T gains with zero sawtooth throttling.
   - **`CPU: Quiet / Eco (45W / 60W, EPB=6, C1E=ON)`:** Stock 45W TDP balanced mode.
