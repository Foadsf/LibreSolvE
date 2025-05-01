@echo off
echo --- Building Solution ---
dotnet build
if errorlevel 1 (
    echo Build FAILED!
    exit /b 1
)

echo.
echo --- Running Test Example (examples/test.lse) ---
dotnet run --project LibreSolvE.CLI\LibreSolvE.CLI.csproj -- examples/test.lse
if errorlevel 1 (
    echo Run FAILED!
    exit /b 1
)

echo.
echo --- Script Completed Successfully ---
exit /b 0
