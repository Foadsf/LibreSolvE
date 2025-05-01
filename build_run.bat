@echo off
setlocal enabledelayedexpansion

REM --- Parse command line arguments ---
set "VERBOSE="
set "CONTINUE_ON_ERROR=1" REM Default to continue on error

:parse_args
if "%~1"=="" goto :end_parse_args
if /i "%~1"=="--verbose" set "VERBOSE=1" & shift & goto :parse_args
if /i "%~1"=="-v" set "VERBOSE=1" & shift & goto :parse_args
if /i "%~1"=="--continue" set "CONTINUE_ON_ERROR=1" & shift & goto :parse_args
if /i "%~1"=="-c" set "CONTINUE_ON_ERROR=1" & shift & goto :parse_args
shift
goto :parse_args
:end_parse_args

REM --- Configuration ---
set "LOG_DIR=logs"
set "LOG_FILE=%LOG_DIR%\build_run.txt"

REM --- Ensure Log Directory Exists ---
if not exist "%LOG_DIR%" mkdir "%LOG_DIR%"

REM --- Configure Java Environment ---
echo Configuring Java...
set "JAVA_FOUND="

for %%J in (
    "C:\Program Files\Microsoft\jdk-11.0.27.6-hotspot"
    "C:\Program Files\Java\jdk-17"
    "C:\Program Files\Java\jdk-11"
    "C:\Program Files\OpenJDK\jdk-17"
    "C:\Program Files\Eclipse Adoptium\jdk-17"
) do (
    if exist "%%~J\bin\java.exe" (
        set "JAVA_HOME=%%~J"
        set "JAVA_FOUND=1"
        goto :java_found
    )
)

if defined JAVA_HOME (
    if exist "!JAVA_HOME!\bin\java.exe" (
        set "JAVA_FOUND=1"
        goto :java_found
    )
)

echo ERROR: No Java installation found.
echo Please install Java 11 or newer and set JAVA_HOME
exit /b 1

:java_found
set "PATH=%JAVA_HOME%\bin;%PATH%"

REM --- Start Log File ---
echo ===================================================================>"!LOG_FILE!"
echo Build started: %date% %time%>>"!LOG_FILE!"
echo ===================================================================">>"!LOG_FILE!"
echo.>>"!LOG_FILE!"

if defined VERBOSE (
    echo Using Java from: %JAVA_HOME%
    java -version 2>>"!LOG_FILE!"
) else (
    echo Using Java from: %JAVA_HOME%>>"!LOG_FILE!"
    java -version>>"!LOG_FILE!" 2>&1
)

REM --- Clean Solution ---
echo Cleaning solution...
if defined VERBOSE (
    dotnet clean
) else (
    dotnet clean --nologo>>"!LOG_FILE!" 2>&1
)

if %errorlevel% neq 0 (
    echo Clean FAILED!
    if %CONTINUE_ON_ERROR%==0 exit /b 1
    echo Continuing despite errors...
)

REM --- Building Solution ---
echo Building solution...
if defined VERBOSE (
    dotnet build -v:detailed
) else (
    dotnet build --nologo>>"!LOG_FILE!" 2>&1
)

if %errorlevel% neq 0 (
    echo Build FAILED
    type "!LOG_FILE!" | findstr /C:"error CS" /C:"ANTLR" /C:"failed"
    if %CONTINUE_ON_ERROR%==0 exit /b 1
    echo Continuing despite errors...
)

echo Build succeeded.

REM --- Running Test Example ---
echo Running test example...
if defined VERBOSE (
    dotnet run --project LibreSolvE.CLI\LibreSolvE.CLI.csproj -- examples/test.lse
    set RUN_RESULT=%errorlevel%
) else (
    dotnet run --project LibreSolvE.CLI\LibreSolvE.CLI.csproj -- examples/test.lse>>"!LOG_FILE!" 2>&1
    set RUN_RESULT=%errorlevel%
    echo Results:
    type "!LOG_FILE!" | findstr /C:"---"
)

if %RUN_RESULT% neq 0 (
    echo Run completed with errors: Exit code %RUN_RESULT%
    if %CONTINUE_ON_ERROR%==0 exit /b 1
    echo Note: Error details may be in the log file: !LOG_FILE!
) else (
    echo Run completed successfully!
)

echo.
echo Build and run completed!

endlocal
exit /b 0
