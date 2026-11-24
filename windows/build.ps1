param(
    [switch]$PackageRelease
)

$ErrorActionPreference = "Stop"
$winDir  = $PSScriptRoot
$repoDir = Split-Path -Parent $winDir
$distDir = Join-Path $repoDir "dist\WinCpuBoost-windows-x64"
$csc     = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $csc)) {
    throw "Required x64 C# compiler not found at $csc"
}

New-Item -ItemType Directory -Path $distDir -Force | Out-Null

Write-Host "Compiling WinCpuBoost.exe..."
& $csc /nologo /optimize+ /platform:x64 /target:winexe `
    /r:System.dll,System.Drawing.dll,System.Management.dll,System.Windows.Forms.dll `
    /out:"$distDir\WinCpuBoost.exe" "$winDir\WinCpuBoost.cs"

Write-Host "Compiling PawnIoCalibrate.exe..."
& $csc /nologo /optimize+ /platform:x64 /target:exe `
    /out:"$distDir\PawnIoCalibrate.exe" "$winDir\PawnIoCalibrate.cs"

Copy-Item "$winDir\modules\IntelMSR.bin"    "$distDir\IntelMSR.bin"    -Force
Copy-Item "$winDir\modules\AppleT2Smc.bin"  "$distDir\AppleT2Smc.bin"  -Force
Copy-Item "$repoDir\LICENSE"                "$distDir\LICENSE"         -Force
Copy-Item "$repoDir\THIRD_PARTY_NOTICES.md" "$distDir\THIRD_PARTY_NOTICES.md" -Force
Copy-Item "$repoDir\README.md"              "$distDir\README.md"       -Force

if ($PackageRelease) {
    $zipPath = Join-Path $repoDir "dist\WinCpuBoost-windows-x64.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path "$distDir\*" -DestinationPath $zipPath -Force
    $sizeKb = [math]::Round((Get-Item $zipPath).Length / 1KB, 1)
    Write-Host "Packaged release archive: $zipPath ($sizeKb KB)"
}
