@echo off
setlocal enabledelayedexpansion

REM --- Initialize variables ---
set "INPUT_FILE="
set "OUTPUT_FILE="
set "SOLVER_ARG="
set "VERBOSE="

REM --- Parse command line arguments ---
:parse_args
if "%~1"=="" goto :end_parse_args

if "%~1"=="--help" (
    goto :show_help
) else if "%~1"=="-h" (
    goto :show_help
) else if "%~1"=="--verbose" (
    set "VERBOSE=--verbose"
    shift
    goto :parse_args
) else if "%~1"=="-v" (
    set "VERBOSE=--verbose"
    shift
    goto :parse_args
) else if "%~1"=="--output" (
    set "OUTPUT_FILE=%~2"
    shift
    shift
    goto :parse_args
) else if "%~1"=="-o" (
    set "OUTPUT_FILE=%~2"
    shift
    shift
    goto :parse_args
) else if "%~1"=="--nelder-mead" (
    set "SOLVER_ARG=--nelder-mead"
    shift
    goto :parse_args
) else if "%~1"=="--levenberg-marquardt" (
    set "SOLVER_ARG=--levenberg-marquardt"
    shift
    goto :parse_args
) else if "%~1"=="--lm" (
    set "SOLVER_ARG=--levenberg-marquardt"
    shift
    goto :parse_args
) else if not defined INPUT_FILE (
    set "INPUT_FILE=%~1"
    shift
    goto :parse_args
) else (
    echo WARNING: Unknown argument: %~1
    shift
    goto :parse_args
)

:end_parse_args

REM --- Validate inputs ---
if not defined INPUT_FILE (
    echo ERROR: Input file not specified.
    call :show_help
    exit /b 1
)

REM --- Handle path normalization ---
REM Remove leading backslash if present
if "%INPUT_FILE:~0,1%"=="\" set "INPUT_FILE=%INPUT_FILE:~1%"

REM --- Check if file exists ---
if not exist "%INPUT_FILE%" (
    echo ERROR: Input file not found: %INPUT_FILE%
    exit /b 1
)

REM --- Build command line ---
set "CMD=dotnet run --project LibreSolvE.CLI\LibreSolvE.CLI.csproj -- "%INPUT_FILE%" %SOLVER_ARG% %VERBOSE%"

REM --- Execute with or without output redirection ---
if defined OUTPUT_FILE (
    echo Executing: %CMD% ^> "%OUTPUT_FILE%"
    %CMD% > "%OUTPUT_FILE%" 2>&1
) else (
    echo Executing: %CMD%
    %CMD%
)

exit /b %errorlevel%

:show_help
echo.
echo Usage: run_cli.bat [options] input_file.lse
echo.
echo Options:
echo   -h, --help                   Show this help message
echo   -v, --verbose                Enable verbose output
echo   -o, --output ^<file^>          Redirect output to specified file
echo   --nelder-mead                Use Nelder-Mead simplex algorithm (default)
echo   --levenberg-marquardt, --lm  Use Levenberg-Marquardt algorithm
echo.
exit /b 1
