@echo off
rem Builds Release and starts it. Needs the .NET 10 SDK (dotnet on PATH).
cd /d "%~dp0app"
rem The build fails harmlessly while the app is already running (exe locked); starting it then just shows the open window.
dotnet build -nologo -v q -c Release
start "" bin\Release\net10.0-windows\OpenEVision.exe
