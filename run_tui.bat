@echo off
setlocal enabledelayedexpansion

REM --- Run the TUI interface ---
dotnet run --project LibreSolvE.TUI\LibreSolvE.TUI.csproj %*
exit /b %errorlevel%
