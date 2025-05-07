@echo off
setlocal enabledelayedexpansion

REM --- Parse command line arguments ---
set "INPUT_FILE=%~1"
set "SOLVER_ARG=%~2"

if "%INPUT_FILE%"=="" goto :usage

if not exist "%INPUT_FILE%" (
    echo ERROR: Input file not found: %INPUT_FILE%
    exit /b 1
)

echo Running LibreSolvE on %INPUT_FILE%
dotnet run --project LibreSolvE.CLI\LibreSolvE.CLI.csproj -- "%INPUT_FILE%" %SOLVER_ARG%
exit /b %errorlevel%

:usage
echo ERROR: Input file not specified.
echo Usage: run_cli.bat input_file.lse [--solver-option]
echo.
echo solver options:
echo   --nelder-mead          Use Nelder-Mead simplex algorithm (default)
echo   --levenberg-marquardt  Use Levenberg-Marquardt algorithm
exit /b 1
