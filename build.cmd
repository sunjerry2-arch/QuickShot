@echo off
rem Build QuickShot.exe with the .NET Framework compiler that ships with Windows (no SDK needed).
rem Quit the running QuickShot from the tray before rebuilding.
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set OPTS=/nologo /target:winexe /optimize+ /codepage:65001 /r:System.Windows.Forms.dll /r:System.Drawing.dll
"%CSC%" %OPTS% /out:QuickShot.exe QuickShot.cs || exit /b 1
start "" /wait QuickShot.exe /writeicon app.ico
"%CSC%" %OPTS% /win32icon:app.ico /out:QuickShot.exe QuickShot.cs || exit /b 1
echo Built: %cd%\QuickShot.exe
