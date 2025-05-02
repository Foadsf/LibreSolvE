@echo off
setlocal enabledelayedexpansion

REM --- Parse command line arguments ---
set "VERBOSE="
set "CONTINUE_ON_ERROR=0"

:parse_args_loop
if "%~1"=="" goto :end_parse_args
if /i "%~1"=="--verbose" set "VERBOSE=1" & shift & goto :parse_args_loop
if /i "%~1"=="-v" set "VERBOSE=1" & shift & goto :parse_args_loop
if /i "%~1"=="--continue" set "CONTINUE_ON_ERROR=1" & shift & goto :parse_args_loop
if /i "%~1"=="-c" set "CONTINUE_ON_ERROR=1" & shift & goto :parse_args_loop
shift
goto :parse_args_loop
:end_parse_args

REM --- Configuration ---
set "LOG_DIR=logs"
set "EXAMPLE_DIR=examples"
set "OVERALL_RESULT=0"

REM --- Ensure Log Directory Exists ---
if not exist "%LOG_DIR%" mkdir "%LOG_DIR%"

REM --- Configure Java Environment for ANTLR ---
set "JAVA_FOUND="
set "DESIRED_JAVA_HOME="

echo Configuring Java environment for ANTLR...
for %%J in (
    "C:\Program Files\OpenJDK\jdk-22.0.2"
    "C:\Program Files\Microsoft\jdk-11.0.27.6-hotspot"
    "C:\Program Files\Java\jdk-17"
    "C:\Program Files\Java\jdk-11"
    "C:\Program Files\OpenJDK\jdk-17"
    "C:\Program Files\Eclipse Adoptium\jdk-17"
) do (
    if not defined JAVA_FOUND (
        if exist "%%~J\bin\java.exe" (
            set "DESIRED_JAVA_HOME=%%~J"
            set "JAVA_FOUND=1"
            if defined VERBOSE echo Found Java at: !DESIRED_JAVA_HOME!
        )
    )
)

if not defined JAVA_FOUND (
    if defined JAVA_HOME (
        if exist "!JAVA_HOME!\bin\java.exe" (
            set "DESIRED_JAVA_HOME=!JAVA_HOME!"
            set "JAVA_FOUND=1"
            if defined VERBOSE echo Found Java via existing JAVA_HOME: !DESIRED_JAVA_HOME!
        )
    )
)

if not defined JAVA_FOUND (
    echo ERROR: No suitable Java installation found.
    exit /b 1
)

set "JAVA_HOME=!DESIRED_JAVA_HOME!"
set "PATH=!JAVA_HOME!\bin;%PATH%"

REM --- Clean and Build Solution ---
echo Cleaning solution...
dotnet clean --nologo > nul 2>&1

echo Building solution...
set "BUILD_VERBOSITY="
if defined VERBOSE set "BUILD_VERBOSITY=-v detailed"

dotnet build --nologo %BUILD_VERBOSITY%
if errorlevel 1 (
    echo Build FAILED! Please fix the build errors.
    exit /b 1
)

echo Build succeeded.

REM --- Running all example files in examples ---
echo.
echo Running all example files in %EXAMPLE_DIR%...

if not exist "%EXAMPLE_DIR%" (
    echo ERROR: Examples directory not found: %EXAMPLE_DIR%
    exit /b 1
)

for %%F in ("%EXAMPLE_DIR%\*.lse") do (
    echo.
    echo ===== Processing: %%~nxF =====
    call dotnet run --project LibreSolvE.CLI\LibreSolvE.CLI.csproj -- "%%F"

    if errorlevel 1 (
        echo FAILED: Processing %%~nxF
        set OVERALL_RESULT=1
        if %CONTINUE_ON_ERROR%==0 exit /b 1
    ) else (
        echo SUCCEEDED: Processed %%~nxF
    )
)

echo.
echo ===== Build and Run Summary =====
if %OVERALL_RESULT%==0 (
    echo All operations completed successfully.
) else (
    echo Script completed with errors.
)

exit /b %OVERALL_RESULT%
