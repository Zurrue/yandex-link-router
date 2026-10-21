param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$src = Join-Path $root "src"
$dist = Join-Path $root "dist"
$version = "1.0.0"

$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path $csc)) { throw "csc.exe (.NET Framework 4) not found" }

New-Item -ItemType Directory -Force -Path $dist | Out-Null

# icon (regenerated on demand, committed in assets/)
$icon = Join-Path $root "assets\icon.ico"
if (-not (Test-Path $icon)) {
    Write-Host "Generating assets..."
    & $csc /nologo /target:exe /r:System.dll /r:System.Drawing.dll `
        /out:"$root\tools\make-assets.exe" "$root\tools\make-assets.cs"
    if ($LASTEXITCODE -ne 0) { throw "asset build failed" }
    & "$root\tools\make-assets.exe" $root
    if ($LASTEXITCODE -ne 0) { throw "asset generation failed" }
}

Write-Host "Building router..."
& $csc /nologo /codepage:65001 /optimize+ /target:winexe /win32icon:"$icon" `
    /r:System.dll /r:System.Core.dll `
    /out:"$dist\ymrouter.exe" "$src\Router.cs"

if ($LASTEXITCODE -ne 0) { throw "router build failed" }

Write-Host "Building setup..."
Push-Location $dist
try {
    & $csc /nologo /codepage:65001 /optimize+ /target:winexe /win32icon:"$icon" `
        /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll `
        /resource:ymrouter.exe,payload.exe `
        /out:YandexLinkRouter-Setup.exe (Join-Path $src "Setup.cs")

    if ($LASTEXITCODE -ne 0) { throw "setup build failed" }
}
finally {
    Pop-Location
}

# sanity check: Cyrillic strings must survive the build
$bytes = [System.IO.File]::ReadAllBytes("$dist\YandexLinkRouter-Setup.exe")
# "Готово" as UTF-16LE bytes (kept as hex so the script stays ASCII-only)
$needle = [byte[]](0x13,0x04,0x3E,0x04,0x42,0x04,0x3E,0x04,0x32,0x04,0x3E,0x04)
$found = $false
for ($i = 0; $i -le $bytes.Length - $needle.Length; $i++) {
    $ok = $true
    for ($j = 0; $j -lt $needle.Length; $j++) {
        if ($bytes[$i + $j] -ne $needle[$j]) { $ok = $false; break }
    }
    if ($ok) { $found = $true; break }
}
if (-not $found) { throw "Cyrillic strings were mangled during build" }

# package: setup + instructions
Copy-Item (Join-Path $root "install-notes.txt") (Join-Path $dist "README.txt") -Force

$zip = Join-Path $dist "YandexLinkRouter-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $dist "YandexLinkRouter-Setup.exe"), (Join-Path $dist "README.txt") -DestinationPath $zip

Get-ChildItem $dist | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
Write-Host "OK"
