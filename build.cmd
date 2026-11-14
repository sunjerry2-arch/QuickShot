@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
title 编译 QuickShot

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" goto :nocsc
set "OPTS=/nologo /target:winexe /optimize+ /codepage:65001 /r:System.Windows.Forms.dll /r:System.Drawing.dll"

rem 正在运行的快截会占用 exe，先关掉，编译完再打开
set "WASRUNNING="
tasklist /fi "imagename eq QuickShot.exe" 2>nul | find /i "QuickShot.exe" >nul
if errorlevel 1 goto :build
set "WASRUNNING=1"
echo 正在关闭运行中的快截...
taskkill /im QuickShot.exe /f >nul 2>&1
ping -n 2 127.0.0.1 >nul

:build
echo 正在编译...
"%CSC%" %OPTS% /out:QuickShot.exe QuickShot.cs
if errorlevel 1 goto :fail
start "" /wait QuickShot.exe /writeicon app.ico
"%CSC%" %OPTS% /win32icon:app.ico /out:QuickShot.exe QuickShot.cs
if errorlevel 1 goto :fail
echo.
echo 编译成功：%cd%\QuickShot.exe
if defined WASRUNNING (
  start "" QuickShot.exe
  echo 已重新启动快截。
)
goto :end

:nocsc
echo 找不到 C# 编译器（.NET Framework 4.x），这台电脑可能没装 .NET Framework 4。
goto :failend

:fail
echo.
echo 编译失败，错误信息见上面。
if defined WASRUNNING if exist QuickShot.exe (
  start "" QuickShot.exe
  echo 已重新启动之前的版本。
)

:failend
if not defined NOPAUSE pause
exit /b 1

:end
if not defined NOPAUSE pause
