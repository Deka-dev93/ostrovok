@echo off
chcp 65001 >nul
where dotnet >nul 2>nul || (echo Установите .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0 & pause & exit /b 1)
dotnet publish -c Release -r win-x64 --self-contained true -o publish
if errorlevel 1 (echo Ошибка сборки - скопируйте текст выше & pause & exit /b 1)
echo.
echo Готово: publish\Ostrovok.exe
start "" publish\Ostrovok.exe
