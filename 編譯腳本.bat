@echo off
chcp 65001 >nul
echo ========================================
echo   rg-gui 編譯腳本
echo ========================================
echo.

dotnet publish rg-gui\rg-gui.csproj --configuration Release --framework net8.0-windows --output publish --runtime win-x64 --no-self-contained -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false

if %errorlevel% equ 0 (
    echo.
    echo 編譯成功！輸出目錄：publish\
) else (
    echo.
    echo 編譯失敗！
)

pause
