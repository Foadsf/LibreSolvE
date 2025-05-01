@echo off
setlocal

REM --- Configuration ---
set "LOG_DIR=logs"
set "LOG_FILE=%LOG_DIR%\build_run.txt"

REM Set this to the root directory of the desired JDK installation
set "DESIRED_JAVA_HOME=C:\Program Files\Java\jdk-11.0.27.6-hotspot"
if not exist "%DESIRED_JAVA_HOME%" (
    set "DESIRED_JAVA_HOME=C:\Program Files\OpenJDK\jdk-17"
)
if not exist "%DESIRED_JAVA_HOME%" (
    set "DESIRED_JAVA_HOME=C:\Program Files\Eclipse Adoptium\jdk-17"
)

REM --- Ensure Log Directory Exists ---
if not exist "%LOG_DIR%" mkdir "%LOG_DIR%"

REM --- Redirect all subsequent output to the log file ---
(
    echo ===================================================================
    echo Starting LibreSolvE Build ^& Run Script at %DATE% %TIME%
    echo ===================================================================
    echo.

    REM --- Configure Java Environment ---
    echo --- Configuring Java ---
    if not exist "%DESIRED_JAVA_HOME%\bin\java.exe" (
        echo WARNING: Specified Java installation not found.
        echo Checking if JAVA_HOME is already set...

        if defined JAVA_HOME (
            echo Using existing JAVA_HOME: %JAVA_HOME%
            if not exist "%JAVA_HOME%\bin\java.exe" (
                echo ERROR: JAVA_HOME does not point to a valid Java installation
                echo Please ensure Java 11 or newer is installed
                exit /b 1
            )
        ) else (
            echo ERROR: No Java installation found.
            echo Please install Java 11 or newer and set JAVA_HOME
            exit /b 1
        )
    ) else (
        echo Using Java from: %DESIRED_JAVA_HOME%
        set "JAVA_HOME=%DESIRED_JAVA_HOME%"
        set "PATH=%JAVA_HOME%\bin;%PATH%"
    )

    REM Verify Java version being used by the script
    echo --- Verifying Java version for build ---
    java -version
    if errorlevel 1 (
        echo ERROR: Java not found in PATH or could not be executed
        exit /b 1
    )
    echo.

    REM --- Clean Solution ---
    echo --- Cleaning Solution ---
    dotnet clean
    if errorlevel 1 (
        echo Clean FAILED!
        exit /b 1
    )
    echo.

    REM --- Building Solution ---
    echo --- Building Solution ---
    REM Set environment variables for ANTLR4 before building
    set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
    echo Building with verbose output for better diagnostics...
    dotnet build
    if errorlevel 1 (
        echo Build FAILED! Check Java setup and ANTLR packages/configuration.
        exit /b 1
    )
    echo.

    REM --- Running Test Example (examples/test.lse) ---
    echo --- Running Test Example examples/test.lse ---
    dotnet run --project LibreSolvE.CLI\LibreSolvE.CLI.csproj -- examples/test.lse
    if errorlevel 1 (
        echo Run FAILED!
        exit /b 1
    )
    echo.

    echo ===================================================================
    echo Script Completed Successfully at %DATE% %TIME%
    echo Log file: %LOG_FILE%
    echo ===================================================================

) > "%LOG_FILE%" 2>&1

REM --- Display final status to console ---
if errorlevel 1 (
    echo.
    echo !!!!! SCRIPT FAILED !!!!!
    echo Check log file for details: %LOG_FILE%
    type "%LOG_FILE%" | findstr /C:"error" /C:"exception" /C:"failed"
) else (
    echo.
    echo +++++ SCRIPT SUCCEEDED +++++
    echo Log file: %LOG_FILE%
)

endlocal
exit /b %errorlevel%
