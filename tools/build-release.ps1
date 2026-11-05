param(
    [string]$Dotnet = 'dotnet',
    [string]$InnoCompiler = 'ISCC.exe'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src/Xylo/Xylo.csproj'
$release = Join-Path $root 'artifacts/release'
$payload = Join-Path $release 'payload'
$script = Join-Path $root 'installer/xylo.iss'
$releaseLock = Join-Path $root 'src/Xylo/packages.release.lock.json'
New-Item -ItemType Directory -Force -Path $payload | Out-Null

& $Dotnet restore $project -r win-x64 --locked-mode -p:SelfContained=true -p:PublishSingleFile=true "-p:NuGetLockFilePath=$releaseLock"
if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }
& $Dotnet publish $project -c Release -r win-x64 --no-restore --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true "-p:NuGetLockFilePath=$releaseLock" -o $payload
if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed.' }
Copy-Item -LiteralPath (Join-Path $payload 'Xylo.exe') -Destination (Join-Path $release 'Xylo.exe') -Force
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $release 'Xylo-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $root 'third-party/WinIsland.LICENSE') -Destination (Join-Path $release 'WinIsland-LICENSE.txt') -Force
& $InnoCompiler "/DPayloadDir=$payload" "/DOutputDir=$release" $script
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
Write-Output "Standalone: $(Join-Path $release 'Xylo.exe')"
Write-Output "Installer: $(Join-Path $release 'Xylo-Setup-0.1.0-win-x64.exe')"
