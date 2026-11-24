# Third-Party Notices

## PawnIO Modules (`IntelMSR.bin`, `AppleT2Smc.bin`)

The precompiled, cryptographically signed PawnIO module binaries in `windows/modules/` (`IntelMSR.bin` and `AppleT2Smc.bin`, from `PawnIO.Modules` v0.2.12) are distributed unmodified under the **GNU Lesser General Public License v2.1 or later (LGPL-2.1-or-later)**:

- **Upstream Source Code:** https://github.com/namazso/PawnIO.Modules
- **Release Binary Archive:** https://github.com/namazso/PawnIO.Modules/releases/tag/0.2.12
- **Copyright:**
  - `IntelMSR.p`: Copyright (C) 2025 namazso (`admin@namazso.eu`)
  - `AppleT2Smc.p`: Copyright (C) 2026 golirt1
- **License Text:** https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html

## PawnIO Kernel Driver (Windows Prerequisite — Not Bundled)

On Windows 11 / 10 x64, `WinCpuBoost` communicates with the separately installed WHQL-signed **PawnIO** driver (`namazso.PawnIO`, https://pawnio.eu/) via `C:\Program Files\PawnIO\PawnIOLib.dll`. Install via:

```powershell
winget install --id namazso.PawnIO -e
```
