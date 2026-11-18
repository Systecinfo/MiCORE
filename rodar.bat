@echo off
rem Abre o MiCORE direto do codigo (para desenvolvimento): acha o .NET 8 SDK, pede administrador e roda "dotnet run".
rem Dois cliques bastam. Nao depende do PATH do terminal.
setlocal
cd /d "%~dp0"

rem 1) Caminho do dotnet: recebido do passo nao elevado, ou procurado agora
set "DN=%~1"
if not defined DN set "DN=%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe"
if not exist "%DN%" if exist "%ProgramFiles%\dotnet\dotnet.exe" set "DN=%ProgramFiles%\dotnet\dotnet.exe"
if not exist "%DN%" for /f "delims=" %%D in ('where dotnet 2^>nul') do set "DN=%%D"
if not exist "%DN%" (
    echo Nao encontrei o .NET 8 SDK. Instale em https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

rem 2) Se nao for administrador, reabre este arquivo elevado passando o caminho do dotnet
net session >nul 2>&1
if errorlevel 1 (
    powershell -NoProfile -Command "Start-Process -Verb RunAs -FilePath '%~f0' -ArgumentList '\"%DN%\"'"
    exit /b
)

rem 3) Roda o app
for %%I in ("%DN%") do set "DOTNET_ROOT=%%~dpI"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_NOLOGO=1"
echo Usando: %DN%
echo Compilando e abrindo o MiCORE ...
"%DN%" run -c Debug
if errorlevel 1 pause
