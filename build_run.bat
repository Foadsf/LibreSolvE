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
set "TEST_COUNT=0"
set "PASS_COUNT=0"
set "FAIL_COUNT=0"

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

REM --- Count total number of .lse files ---
set "TOTAL_FILES=0"
for %%F in ("%EXAMPLE_DIR%\*.lse") do (
    set /a TOTAL_FILES+=1
)

echo Found %TOTAL_FILES% test files to process.
echo.

REM --- Process each .lse file ---
for %%F in ("%EXAMPLE_DIR%\*.lse") do (
    set /a TEST_COUNT+=1

    REM --- Create padded test number for log file naming ---
    set "PADDED_COUNT=00!TEST_COUNT!"
    set "PADDED_COUNT=!PADDED_COUNT:~-3!"

    echo.
    echo ===== [!TEST_COUNT!/%TOTAL_FILES%] Processing: %%~nxF =====

    REM --- Create log file path ---
    set "LOG_FILE=%LOG_DIR%\run_!PADDED_COUNT!_%%~nxF.txt"

    REM --- Run the test and capture output ---
    dotnet run --project LibreSolvE.CLI\LibreSolvE.CLI.csproj -- "%%F" > "!LOG_FILE!" 2>&1

    REM --- Check result ---
    if errorlevel 1 (
        echo [FAILED] Processing %%~nxF
        set /a FAIL_COUNT+=1
        set OVERALL_RESULT=1
        if %CONTINUE_ON_ERROR%==0 (
            echo Stopping on first failure. Use -c or --continue to run all tests.
            goto :summary
        )
    ) else (
        echo [PASSED] Processing %%~nxF
        set /a PASS_COUNT+=1
    )

    echo Log file: !LOG_FILE!
)

:summary
echo.
echo ===== Build and Run Summary =====
echo Total tests: %TEST_COUNT%/%TOTAL_FILES%
echo Passed: %PASS_COUNT%
echo Failed: %FAIL_COUNT%

if %OVERALL_RESULT%==0 (
    echo ALL TESTS PASSED successfully.
) else (
    echo SOME TESTS FAILED. Check log files for details.
)

echo.
echo Log files are available in: %LOG_DIR%\

exit /b %OVERALL_RESULT%
