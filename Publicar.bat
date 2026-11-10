@echo off
chcp 65001 >nul
title Otimizador PC PRO - Publicar
cd /d "%~dp0"

where dotnet >nul 2>&1 || (
  echo [ERRO] .NET SDK 8 nao encontrado. Baixe em https://dotnet.microsoft.com/download/dotnet/8.0
  pause & exit /b 1
)

echo Fechando o app, se estiver aberto...
taskkill /f /im OtimizadorPCPro.exe >nul 2>&1

if exist publicar rmdir /s /q publicar

if /i "%1"=="autonomo" (
  echo Publicando versao AUTONOMA ^(nao precisa do .NET instalado, ~70 MB^)...
  dotnet publish src\Otimizador\Otimizador.csproj -c Release -o publicar ^
    -p:SelfContained=true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
) else (
  echo Publicando versao LEVE ^(~0,5 MB, usa o .NET 8 Desktop Runtime^)...
  dotnet publish src\Otimizador\Otimizador.csproj -c Release -o publicar -p:PublishSingleFile=true
)
if errorlevel 1 ( echo [ERRO] Falha na publicacao. & pause & exit /b 1 )

echo.
echo Pronto: publicar\OtimizadorPCPro.exe
echo Para gerar o instalador, rode CriarInstalador.bat
pause
