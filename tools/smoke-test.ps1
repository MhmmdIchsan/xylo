param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repository 'src/Xylo/Xylo.csproj'
& $Dotnet build $project -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
if (Get-Process Xylo -ErrorAction SilentlyContinue) { throw 'Quit Xylo before running the smoke test.' }
$reportPath = Join-Path $env:TEMP 'xylo-smoke-test.json'
$started = Get-Date
$executable = Join-Path $repository 'src/Xylo/bin/Release/net8.0-windows/win-x64/Xylo.exe'
$process = Start-Process -FilePath $executable -ArgumentList '--smoke-test' -PassThru -WindowStyle Hidden
if (!$process.WaitForExit(20000)) {
    $process.Kill()
    throw 'Smoke test did not exit within 20 seconds.'
}
if ($process.ExitCode -ne 0) { throw "Xylo exited with code $($process.ExitCode). Check %LOCALAPPDATA%/Xylo/last-error.txt." }
if (!(Test-Path -LiteralPath $reportPath) -or (Get-Item -LiteralPath $reportPath).LastWriteTime -lt $started) {
    throw 'No fresh smoke-test report was produced.'
}
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
if ($report.RenderCount -lt 10) { throw 'Expand/collapse animation did not render.' }
if (!$report.TrayRegistered) { throw 'System tray icon could not be registered.' }
if ($report.IdleRenderCount -ne 0) { throw 'Renderer continued drawing at idle.' }
if ($report.AfterIdle.GdiObjects -gt ($report.Initial.GdiObjects + 2)) { throw 'GDI objects increased after expand/collapse cycles.' }
$report | ConvertTo-Json -Depth 4
Write-Output ('Idle working set: {0:N1} MiB; private bytes: {1:N1} MiB' -f ($report.AfterIdle.WorkingSetBytes / 1MB), ($report.AfterIdle.PrivateBytes / 1MB))
Write-Output ('CPU used during ~2 seconds idle: {0:N1} ms' -f ($report.AfterIdle.CpuMilliseconds - $report.BeforeIdle.CpuMilliseconds))
if ($report.AfterIdle.WorkingSetBytes -gt 50MB -or $report.AfterIdle.PrivateBytes -gt 50MB) {
    Write-Warning 'The initial 50 MB idle memory target has not been met in this run.'
}
