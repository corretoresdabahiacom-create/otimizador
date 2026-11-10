@echo off
chcp 65001 >nul
title Otimizador PC PRO - Instalador
cd /d "%~dp0"

if not exist publicar\OtimizadorPCPro.exe (
  echo Rode Publicar.bat primeiro.
  pause & exit /b 1
)

set ISCC=
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set ISCC="%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set ISCC="%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not defined ISCC (
  echo [ERRO] Inno Setup 6 nao encontrado. Baixe em https://jrsoftware.org/isdl.php
  pause & exit /b 1
)

%ISCC% instalador\setup.iss
if errorlevel 1 ( echo [ERRO] Falha ao compilar o instalador. & pause & exit /b 1 )
echo.
echo Instalador criado em: saida\
start "" saida
pause
