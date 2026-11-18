@echo off
rem Abre o MiCORE direto do codigo (para desenvolvimento): acha o .NET 8 SDK, pede administrador e roda "dotnet run".
rem Dois cliques bastam. Nao depende do PATH do terminal nem de qual conta do Windows e a administradora.
setlocal EnableDelayedExpansion
cd /d "%~dp0"

rem 1) Procura o dotnet: argumento recebido, conta atual, Program Files, PATH e, por fim, a pasta de cada usuario
set "DN="
if not "%~1"=="" if exist "%~1" set "DN=%~1"
if not defined DN if exist "%USERPROFILE%\.dotnet\dotnet.exe" set "DN=%USERPROFILE%\.dotnet\dotnet.exe"
if not defined DN if exist "%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe" set "DN=%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe"
if not defined DN if exist "%ProgramFiles%\dotnet\dotnet.exe" set "DN=%ProgramFiles%\dotnet\dotnet.exe"
if not defined DN for /f "delims=" %%D in ('where dotnet 2^>nul') do if not defined DN set "DN=%%D"
if not defined DN for /d %%U in ("%SystemDrive%\Users\*") do if not defined DN if exist "%%U\.dotnet\dotnet.exe" set "DN=%%U\.dotnet\dotnet.exe"
if not defined DN for /d %%U in ("%SystemDrive%\Users\*") do if not defined DN if exist "%%U\AppData\Local\Microsoft\dotnet\dotnet.exe" set "DN=%%U\AppData\Local\Microsoft\dotnet\dotnet.exe"
if not defined DN (
    echo Nao encontrei o .NET 8 SDK.
    echo Conta atual: %USERNAME%   ^|   Procurei em: %USERPROFILE%\.dotnet, %LOCALAPPDATA%\Microsoft\dotnet, %ProgramFiles%\dotnet, PATH e %SystemDrive%\Users\*\AppData\Local\Microsoft\dotnet
    echo Instale em https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

rem 2) Se nao for administrador, reabre este arquivo elevado passando o caminho do dotnet
net session >nul 2>&1
if errorlevel 1 (
    set "MICORE_BAT=%~f0"
    set "MICORE_DN=!DN!"
    powershell -NoProfile -Command "Start-Process -Verb RunAs -FilePath $env:ComSpec -ArgumentList ('/c \"\"' + $env:MICORE_BAT + '\" \"' + $env:MICORE_DN + '\"\"')"
    exit /b
)

rem 3) Roda o app
for %%I in ("!DN!") do set "DOTNET_ROOT=%%~dpI"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_NOLOGO=1"
echo Usando: !DN!
echo Compilando e abrindo o MiCORE ...
"!DN!" run -c Debug
if errorlevel 1 pause
