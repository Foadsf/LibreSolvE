@echo off
setlocal enabledelayedexpansion

REM --- Parse command line arguments ---
set "INPUT_FILE=%~1"
set "SOLVER_ARG="

if "%INPUT_FILE%"=="" (
    echo ERROR: Input file not specified.
    echo Usage: run_cli.bat input_file.lse [--solver-option]
    echo.
    echo solver options:
    echo   --nelder-mead          Use Nelder-Mead simplex algorithm (default)
    echo   --levenberg-marquardt  Use Levenberg-Marquardt algorithm
    exit /b 1
)

if not exist "%INPUT_FILE%" (
    echo ERROR: Input file not found: %INPUT_FILE%
    exit /b 1
)

REM --- Handle additional arguments ---
if "%~2"=="--levenberg-marquardt" (
    set "SOLVER_ARG=--levenberg-marquardt"
) else if "%~2"=="--nelder-mead" (
    set "SOLVER_ARG=--nelder-mead"
) else if not "%~2"=="" (
    echo WARNING: Unknown argument: %~2
    echo Valid solver options: --nelder-mead, --levenberg-marquardt
)

REM --- Run the equation solver CLI ---
dotnet run --project LibreSolvE.CLI\LibreSolvE.CLI.csproj -- "%INPUT_FILE%" %SOLVER_ARG%
exit /b %errorlevel%
