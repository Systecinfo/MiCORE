@echo off
rem Gera o MiCORE pronto para levar a outro PC (nao precisa instalar o .NET la).
rem Saida: ..\MiCORE-dist\MiCORE.exe + LEIA-ME.txt
rem Sempre recompila do zero a partir do codigo atual desta pasta.
cd /d "%~dp0"
echo Fechando o MiCORE se estiver aberto ...
taskkill /f /im MiCORE.exe >nul 2>&1
echo Gerando MiCORE.exe a partir do codigo atual ...
rmdir /s /q obj\Release >nul 2>&1
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o "..\MiCORE-dist" -nologo -v q
if errorlevel 1 (
    echo.
    echo FALHOU. Veja os erros acima. O .exe antigo NAO foi atualizado.
    pause
    exit /b 1
)
copy /y "LEIA-ME.txt" "..\MiCORE-dist\LEIA-ME.txt" >nul
echo.
echo Pronto: %~dp0..\MiCORE-dist
for %%F in ("..\MiCORE-dist\MiCORE.exe") do echo Arquivo gerado em %%~tF (confira se e de agora^)
pause
