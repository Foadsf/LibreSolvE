@echo off
setlocal enabledelayedexpansion

REM --- Parse command line arguments ---
set "INPUT_FILE=%~1"
set "OUTPUT_FILE=%~2"

if "%INPUT_FILE%"=="" (
    echo ERROR: Input file not specified.
    echo Usage: run_lse.bat input_file.lse [output_file.txt]
    exit /b 1
)

if not exist "%INPUT_FILE%" (
    echo ERROR: Input file not found: %INPUT_FILE%
    exit /b 1
)

REM --- Run the equation solver CLI ---
if "%OUTPUT_FILE%"=="" (
    REM Output to console
    dotnet run --project LibreSolvE.CLI\LibreSolvE.CLI.csproj -- "%INPUT_FILE%"
) else (
    REM Output to file
    dotnet run --project LibreSolvE.CLI\LibreSolvE.CLI.csproj -- "%INPUT_FILE%" > "%OUTPUT_FILE%" 2>&1
)

exit /b %errorlevel%
