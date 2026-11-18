@echo off
rem Build WeiboReader.exe with the in-box .NET Framework C# compiler (no SDK needed).
setlocal
cd /d "%~dp0"

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo [build] in-box csc.exe not found; install .NET Framework 4.x or Visual Studio.
    exit /b 1
)

set GAC=%WINDIR%\Microsoft.NET\assembly\GAC_MSIL
set GAC64=%WINDIR%\Microsoft.NET\assembly\GAC_64

set REFS=
call :ref "%GAC64%\PresentationCore\v4.0_4.0.0.0__31bf3856ad364e35\PresentationCore.dll"
call :ref "%GAC%\PresentationFramework\v4.0_4.0.0.0__31bf3856ad364e35\PresentationFramework.dll"
call :ref "%GAC%\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll"
call :ref "%GAC%\System.Xaml\v4.0_4.0.0.0__b77a5c561934e089\System.Xaml.dll"
rem System.Web.Extensions ships in the framework dir and is referenced by the
rem compiler's default response file, so it must not be added again here.
if errorlevel 1 exit /b 1

if not exist out mkdir out
"%CSC%" -nologo -target:winexe -optimize+ -out:"out\WeiboReader.exe" %REFS% src\*.cs src\views\*.cs
if errorlevel 1 (
    echo [build] compile failed.
    exit /b 1
)
echo [build] out\WeiboReader.exe built.
exit /b 0

:ref
if exist %~1 (
    set REFS=%REFS% -r:%~1
) else (
    echo [build] missing assembly: %~1
    exit /b 1
)
exit /b 0
