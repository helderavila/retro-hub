<#
  Gera dist\RetroHub-Setup-<versão>.exe:
    1. dotnet publish self-contained (o .NET vai junto — quem instala não precisa de nada);
    2. Inno Setup compila installer\RetroHub.iss.
  Uso:  powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version
$publish = Join-Path $root 'dist\publish'

$cmd = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnet = if ($cmd) { $cmd.Source } else { 'C:\Program Files\dotnet\dotnet.exe' }

$iscc = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 não encontrado. Instale com: winget install JRSoftware.InnoSetup' }

Write-Host "Retro Hub $version"
if (Test-Path $publish) { [IO.Directory]::Delete($publish, $true) }

& $dotnet publish (Join-Path $root 'src\RetroHub.App\RetroHub.App.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true -p:DebugType=none `
    -o $publish --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish falhou.' }

& $iscc /Q "/DAppVersion=$version" (Join-Path $PSScriptRoot 'RetroHub.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup falhou.' }

$setup = Join-Path $root "dist\RetroHub-Setup-$version.exe"
Write-Host ("Instalador: {0} ({1:0.0} MB)" -f $setup, ((Get-Item $setup).Length / 1MB))
