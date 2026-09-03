@echo установка клиента Tnov
setlocal
:: ======== Установка клиента TNov ========
set "TNovSource=\\fs-nova\Distr\0.For Admin\_TNov\actual\client"
set "TNovDest=%USERPROFILE%\TNovClient"
set "StartupFolder=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup"

mkdir "%TNovDest%" 2>nul

copy "%TNovSource%\Hardcodet.NotifyIcon.Wpf.dll"        "%TNovDest%\" >nul
copy "%TNovSource%\Microsoft.Toolkit.Uwp.Notifications.dll" "%TNovDest%\" >nul
copy "%TNovSource%\Microsoft.Windows.SDK.NET.dll"       "%TNovDest%\" >nul
copy "%TNovSource%\TNovClient.deps.json"               "%TNovDest%\" >nul
copy "%TNovSource%\TNovClient.dll"                     "%TNovDest%\" >nul
copy "%TNovSource%\TNovClient.exe"                     "%TNovDest%\" >nul
copy "%TNovSource%\TNovClient.pdb"                     "%TNovDest%\" >nul
copy "%TNovSource%\TNovClient.runtimeconfig.json"      "%TNovDest%\" >nul
copy "%TNovSource%\TNovClient.Updater.deps.json"       "%TNovDest%\" >nul
copy "%TNovSource%\TNovClient.Updater.dll"             "%TNovDest%\" >nul
copy "%TNovSource%\TNovClient.Updater.exe"             "%TNovDest%\" >nul
copy "%TNovSource%\TNovClient.Updater.pdb"             "%TNovDest%\" >nul
copy "%TNovSource%\TNovClient.Updater.runtimeconfig.json" "%TNovDest%\" >nul
copy "%TNovSource%\WinRT.Runtime.dll"                  "%TNovDest%\" >nul
copy "%TNovSource%\Newtonsoft.Json.dll"                "%TNovDest%\" >nul
copy "%TNovSource%\TNovConfig.json"                    "%TNovDest%\" >nul
copy "%TNovSource%\CreateShortcut.vbs"                 "%TNovDest%\" >nul

cd /d "%TNovDest%"
cscript //nologo CreateShortcut.vbs /source:"%TNovDest%\TNovClient.exe" /destination:"%StartupFolder%" /name:"TNovClient"

echo Запуск TNov клиента...
start "" TNovClient.exe
