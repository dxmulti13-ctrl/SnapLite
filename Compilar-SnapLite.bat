@echo off
setlocal
where dotnet >nul 2>nul
if errorlevel 1 (
  echo No se encontro .NET SDK. Instala .NET 8 SDK desde https://dotnet.microsoft.com/download/dotnet/8.0
  pause
  exit /b 1
)
dotnet publish SnapLite.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist
if errorlevel 1 (
  echo Error al compilar.
  pause
  exit /b 1
)
echo.
echo Listo: dist\SnapLite.exe
pause
