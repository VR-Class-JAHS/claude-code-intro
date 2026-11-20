@echo off
rem Moves this laptop onto the Wi-Fi network saved in the .xml file next to this script.
rem Double-click it on each laptop (from a USB stick works). "switch-wifi.cmd /check" only shows
rem which network it would use, and changes nothing. See README.md for making the .xml file.
setlocal

set "HERE=%~dp0"
set "COUNT=0"
for %%F in ("%HERE%*.xml") do (
  set /a COUNT+=1
  set "PROFILE_FILE=%%~fF"
)
if "%COUNT%"=="0" (
  echo No .xml network file next to this script. See README.md to export one.
  goto :fail
)
if not "%COUNT%"=="1" (
  echo Found %COUNT% .xml files next to this script. Leave only the network to switch to.
  goto :fail
)

rem The network's name, read from the file.
set "PROFILE_NAME="
for /f "usebackq delims=" %%N in (`powershell -NoProfile -Command "([xml](Get-Content -LiteralPath '%PROFILE_FILE%')).WLANProfile.name"`) do set "PROFILE_NAME=%%N"
if not defined PROFILE_NAME (
  echo Couldn't read a network name from "%PROFILE_FILE%".
  goto :fail
)

echo Network: %PROFILE_NAME%
echo File:    %PROFILE_FILE%
if /i "%~1"=="/check" (
  echo Check only: nothing was changed.
  goto :done
)

rem user=current: saved for whoever is signed in, which needs no administrator rights.
netsh wlan add profile filename="%PROFILE_FILE%" user=current
if errorlevel 1 (
  echo Windows didn't accept the network. The school may block adding networks on this laptop.
  goto :fail
)

netsh wlan connect name="%PROFILE_NAME%"
if errorlevel 1 (
  echo Couldn't connect to %PROFILE_NAME%. Is it in range? It's saved, so Windows will join it when it can.
  goto :fail
)

echo Waiting for the connection...
timeout /t 8 /nobreak >nul
netsh wlan show interfaces | findstr /r /c:"^ *State" /c:"^ *SSID"
goto :done

:fail
echo.
echo Not switched.
pause
exit /b 1

:done
echo.
pause
exit /b 0
