# Compila o MiCORE-Setup.exe (instalador pequeno, atualizador e desinstalador) com o csc.exe do .NET Framework 4,
# que já vem no Windows 10/11. Não precisa do .NET SDK.
# Uso: .\installer\build.ps1 -Repo "Systecinfo/MiCORE" [-Icon Assets\app.ico]
param(
    [string]$Repo = "Systecinfo/MiCORE",
    [string]$AppName = "MiCORE",
    [string]$ZipName = "MiCORE.zip",
    [string]$Icon = ""
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $here 'dist'
New-Item -ItemType Directory -Force $out | Out-Null

$cfg = @"
namespace MiCoreSetup {
    static class BuildConfig {
        public const string Repo = "$Repo";
        public const string AppName = "$AppName";
        public const string ZipName = "$ZipName";
    }
}
"@
$cfgFile = Join-Path $out 'BuildConfig.g.cs'
Set-Content -Path $cfgFile -Value $cfg -Encoding UTF8

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" }

$cscArgs = @('/nologo', '/target:winexe', '/optimize+', '/platform:anycpu', '/codepage:65001', "/out:$out\$AppName-Setup.exe",
    '/r:System.dll', '/r:System.Core.dll', '/r:Microsoft.CSharp.dll', '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll',
    '/r:System.Web.Extensions.dll', '/r:System.IO.Compression.dll', '/r:System.IO.Compression.FileSystem.dll')
if ($Icon -and (Test-Path $Icon)) { $cscArgs += "/win32icon:$Icon" }
$cscArgs += @((Join-Path $here 'MiCORESetup.cs'), $cfgFile)

& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar (csc exit $LASTEXITCODE)" }
Get-Item "$out\$AppName-Setup.exe" | Select-Object Name, @{n = 'KB'; e = { [math]::Round($_.Length / 1KB, 1) } }
