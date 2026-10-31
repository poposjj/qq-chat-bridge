@echo off
chcp 65001 >nul
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo 找不到 csc.exe，请安装 .NET Framework 4.x
  pause
  exit /b 1
)
"%CSC%" /nologo /target:winexe /codepage:65001 /out:QQBot启动器.exe ^
  /win32manifest:app.manifest ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll Launcher.cs
if errorlevel 1 (
  echo 编译失败
  pause
  exit /b 1
)
echo 编译完成：QQBot启动器.exe
pause
