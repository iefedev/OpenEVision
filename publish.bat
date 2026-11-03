@echo off
rem Builds one self-contained OpenEVision.exe (win-x64, no .NET install needed) into dist\. Needs the .NET 10 SDK.
cd /d "%~dp0app"
dotnet publish -nologo -v q -c Release -o "%~dp0dist" || (pause & exit /b 1)
echo Built %~dp0dist\OpenEVision.exe
