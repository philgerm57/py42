@echo off
rem ==========================================================================
rem  build.bat : compile Py42 (Py42.Core, Py42.Studio, py42 en ligne de commande)
rem
rem  Usage :  build.bat              compile la solution (Debug)
rem           build.bat release      idem en Release
rem           build.bat publish      publie Py42.exe (_publish\Py42) et py42.exe (_publish\cli), fichier unique
rem           build.bat nopause      sans pause finale (pour un script)
rem           (options combinables : build.bat publish nopause)
rem
rem  Prerequis : SDK .NET 10 et dm42dll compilee a cote (..\dm42dll\bin\Debug\dm42dll.dll) ;
rem              si elle manque, ..\dm42dll\build.bat est lance.
rem ==========================================================================
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"
set "CONFIG=Debug"
set "PUBLISH="
set "NOPAUSE="
for %%a in (%*) do (
    if /i "%%a"=="release" set "CONFIG=Release"
    if /i "%%a"=="publish" set "PUBLISH=1"
    if /i "%%a"=="nopause" set "NOPAUSE=1"
)

echo.
echo ===== Py42 : construction (%CONFIG%) =====
where dotnet >nul 2>nul || (echo ERREUR : dotnet introuvable. Installez le SDK .NET 10. & goto :error)

rem --- dm42dll : encodeur .raw utilise par Py42.Studio et py42
set "DLL=%~dp0..\dm42dll\bin\Debug\dm42dll.dll"
if not exist "%DLL%" (
    if exist "%~dp0..\dm42dll\build.bat" (
        echo dm42dll.dll absente : compilation de dm42dll.
        call "%~dp0..\dm42dll\build.bat" nopause || goto :error
    ) else (
        echo ERREUR : dm42dll introuvable. Clonez https://github.com/philgerm57/dm42dll a cote de Py42.
        goto :error
    )
)

if defined PUBLISH goto :publish

dotnet build "Py42.sln" -nologo -v:minimal -c %CONFIG% || goto :error
echo.
echo ===== Termine =====
echo Py42.exe : %~dp0Py42.Studio\bin\%CONFIG%\
echo py42.exe : %~dp0Py42.Cli\bin\%CONFIG%\
goto :done

:publish
set "OUT=%~dp0_publish"
set "PUBARGS=-c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:AllowedReferenceRelatedFileExtensions=none"
if exist "%OUT%" rmdir /s /q "%OUT%"
rem deux dossiers : Py42.exe et py42.exe ont le meme nom pour Windows
dotnet publish "Py42.Studio\Py42.Studio.csproj" %PUBARGS% -o "%OUT%\Py42" || goto :error
dotnet publish "Py42.Cli\Py42.Cli.csproj" %PUBARGS% -o "%OUT%\cli" || goto :error
echo.
echo ===== Termine =====
echo Interface         : %OUT%\Py42\Py42.exe
echo Ligne de commande : %OUT%\cli\py42.exe
goto :done

:error
echo.
echo ===== ECHEC de la construction =====
if not defined NOPAUSE pause
exit /b 1

:done
if not defined NOPAUSE pause
exit /b 0
