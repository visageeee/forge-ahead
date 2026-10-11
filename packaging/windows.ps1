$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$prefix = Join-Path $env:MSYS2_ROOT 'ucrt64'
$out = Join-Path $root 'dist\Forge-Ahead-win-x64'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item $out -ItemType Directory -Force | Out-Null
dotnet publish Weland.csproj -c Release -r win-x64 --self-contained true -p:PublishTrimmed=false -o $out
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
# Copy the complete installed native runtime, including plugin dependencies.
Copy-Item "$prefix\bin\*.dll" $out
Copy-Item "$prefix\bin\gspawn*.exe" $out
New-Item "$out\lib" -ItemType Directory -Force | Out-Null
foreach ($folder in @('gdk-pixbuf-2.0', 'gio')) {
    if (Test-Path "$prefix\lib\$folder") {
        Copy-Item "$prefix\lib\$folder" "$out\lib\$folder" -Recurse
    }
}
Copy-Item "$prefix\share" "$out\share" -Recurse
if (Test-Path "$prefix\etc") { Copy-Item "$prefix\etc" "$out\etc" -Recurse }
$loader = Get-ChildItem "$prefix\bin\gdk-pixbuf-query-loaders*.exe" | Select-Object -First 1
$cache = & $loader.FullName
if ($LASTEXITCODE -ne 0) { throw 'Image loader discovery failed' }
# RuntimeSetup replaces this token with the installation directory after extraction.
$cache = ($cache -join "`n").Replace($prefix.Replace('\','/'), '@GTKROOT@').Replace($prefix.Replace('\','\\'), '@GTKROOT@')
if ($cache -match '(?m)^"(?!@GTKROOT@)[A-Z]:') { throw 'Image cache still contains build-machine paths' }
[IO.File]::WriteAllText("$out\loaders.cache.in", $cache)
Copy-Item COPYING.txt "$out\COPYING.txt"
Copy-Item packaging\BUILDING.md "$out\BUILDING.md"
$zip = Join-Path $root 'dist\Forge-Ahead-win-x64.zip'
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path $out -DestinationPath $zip
