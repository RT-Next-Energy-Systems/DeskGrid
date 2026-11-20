# Builds the current source and runs it as the DeskGrid on this computer: stops the running DeskGrid (and the NotiLand it
# started), publishes artifacts\publish\DeskGrid.exe and starts it through explorer.exe, so it mounts on the desktop like a
# normal start. DeskGrid points its "start with Windows" entry at the exe it runs, so after this it also starts at sign-in.
#
#   powershell -ExecutionPolicy Bypass -File tools\run-latest.ps1            (publish, the real exe)
#   powershell -ExecutionPolicy Bypass -File tools\run-latest.ps1 -Debug     (a Debug build, faster)
param([switch]$Debug)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')

# Whatever DeskGrid is running (this build, an old download) leaves first; NotiLand follows its DeskGrid out.
Get-Process | Where-Object { $_.ProcessName -like 'DeskGrid*' } | ForEach-Object {
    Write-Host "Stopping $($_.ProcessName) ($($_.Id))"
    Stop-Process -Id $_.Id -Force
}
Start-Sleep -Seconds 2
Get-Process BrimDeck -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$env:LOCALAPPDATA\DeskGrid\NotiLand\*" -or $_.Path -like "$root\*" } |
    ForEach-Object { Stop-Process -Id $_.Id -Force }

if ($Debug) {
    dotnet build "$root\src\DeskGrid\DeskGrid.csproj" -nologo -v q
    $exe = "$root\src\DeskGrid\bin\Debug\net10.0-windows10.0.19041.0\win-x64\DeskGrid.exe"
} else {
    dotnet publish "$root\src\DeskGrid\DeskGrid.csproj" -p:PublishProfile=win-x64 -nologo -v q
    $exe = "$root\artifacts\publish\DeskGrid.exe"
}

if ($LASTEXITCODE -ne 0) { throw 'The build failed; DeskGrid was not started.' }
Start-Process explorer.exe -ArgumentList "`"$exe`""
Write-Host "Started $exe"
