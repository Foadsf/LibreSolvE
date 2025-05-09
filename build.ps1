# build.ps1
# Cross-platform build and test script for LibreSolvE

# Parameters
param (
    [switch]$VerboseScript = $false, # Verbosity for this script's actions
    [switch]$Continue = $false,
    [switch]$NoTests = $false,
    [switch]$Clean = $false,
    [switch]$VerboseCli = $false    # Pass-through verbosity for CLI execution
)

# Set error action preference
$ErrorActionPreference = "Stop"

# Import modules for better console output
if (-not (Get-Module -Name PSWriteColor -ListAvailable)) {
    Write-Host "Installing PSWriteColor module for improved output..."
    Install-Module -Name PSWriteColor -Force -Scope CurrentUser -ErrorAction SilentlyContinue
}
Import-Module PSWriteColor -ErrorAction SilentlyContinue

# Define paths
$RootDir = $PSScriptRoot
$ScriptLogDir = Join-Path $RootDir "logs" # For this script's summary log
$ExampleDir = Join-Path $RootDir "examples"
$CliProjectPath = "LibreSolvE.CLI\LibreSolvE.CLI.csproj"

# Ensure script log directory exists
if (-not (Test-Path -Path $ScriptLogDir)) {
    New-Item -Path $ScriptLogDir -ItemType Directory | Out-Null
}
$ScriptOverallLogFile = Join-Path $ScriptLogDir "build_test_summary_$(Get-Date -Format 'yyyyMMdd_HHmmss').log"
$ScriptLogContent = New-Object System.Text.StringBuilder

# Function to log to both console (if not quiet) and script log builder
function Log-Output {
    param(
        [string]$Message,
        [string]$Type = "INFO" # INFO, SUCCESS, ERROR, WARNING, HEADER
    )
    $Timestamp = Get-Date -Format 'HH:mm:ss'
    $LogEntry = "[$Timestamp $Type] $Message"
    [void]$ScriptLogContent.AppendLine($LogEntry)

    switch ($Type) {
        "HEADER" { Write-Color -Text "`n==== ", $Message, " ====" -Color White, Cyan, White }
        "SUCCESS" { Write-Color -Text "[", "SUCCESS", "] ", $Message -Color White, Green, White, White }
        "ERROR" { Write-Color -Text "[", "ERROR", "] ", $Message -Color White, Red, White, White }
        "WARNING" { Write-Color -Text "[", "WARNING", "] ", $Message -Color White, Yellow, White, White }
        default { Write-Color -Text "[", "INFO", "] ", $Message -Color White, Blue, White, White } # INFO
    }
}

# Configure Java environment (remains the same)
function Configure-JavaEnvironment {
    Log-Output -Message "Configuring Java Environment" -Type HEADER
    $JavaPaths = @(
        "C:\Program Files\OpenJDK\jdk-22.0.2",
        "C:\Program Files\Microsoft\jdk-11.0.27.6-hotspot",
        "C:\Program Files\Java\jdk-17",
        "C:\Program Files\Java\jdk-11",
        "C:\Program Files\Eclipse Adoptium\jdk-17"
    )
    $JavaFound = $false
    $DesiredJavaHome = $null
    foreach ($Path in $JavaPaths) {
        if (Test-Path (Join-Path $Path "bin\java.exe")) {
            $DesiredJavaHome = $Path; $JavaFound = $true
            if ($VerboseScript) { Log-Output "Found Java at: $DesiredJavaHome" }
            break
        }
    }
    if (-not $JavaFound -and $env:JAVA_HOME) {
        if (Test-Path (Join-Path $env:JAVA_HOME "bin\java.exe")) {
            $DesiredJavaHome = $env:JAVA_HOME; $JavaFound = $true
            if ($VerboseScript) { Log-Output "Found Java via existing JAVA_HOME: $DesiredJavaHome" }
        }
    }
    if (-not $JavaFound) {
        try {
            $JavaLocation = (Get-Command java -ErrorAction SilentlyContinue).Source
            if ($JavaLocation) {
                $DesiredJavaHome = Split-Path (Split-Path $JavaLocation -Parent) -Parent; $JavaFound = $true
                if ($VerboseScript) { Log-Output "Found Java in PATH: $DesiredJavaHome" }
            }
        }
        catch {}
    }
    if (-not $JavaFound) { Log-Output "No suitable Java installation found." -Type ERROR; return $false }
    $env:JAVA_HOME = $DesiredJavaHome
    $env:Path = "$($env:JAVA_HOME)\bin;$($env:Path)"
    Log-Output "Java environment configured successfully" -Type SUCCESS
    return $true
}

# Clean solution (remains the same)
function Clean-Solution {
    Log-Output "Cleaning Solution" -Type HEADER
    dotnet clean --nologo
    Log-Output "Solution cleaned successfully" -Type SUCCESS
}

# Build solution
function Build-Solution {
    Log-Output "Building Solution" -Type HEADER
    $BuildVerbosityArg = if ($VerboseScript) { "--verbosity detailed" } else { "" }
    try {
        $BuildOutput = $(dotnet build --nologo $BuildVerbosityArg 2>&1)
        $BuildExitCode = $LASTEXITCODE
        $ErrorCount = 0; $WarningCount = 0; $Errors = @(); $Warnings = @()

        $BuildOutput | ForEach-Object {
            $Line = $_
            if ($Line -match ": error " -or $Line -match "error AVLN[0-9]+:") {
                if ($Errors -notcontains $Line) { Log-Output $Line -Type ERROR; $Errors += $Line; $ErrorCount++ }
            }
            elseif ($Line -match ": warning ") {
                if ($Warnings -notcontains $Line) { Log-Output $Line -Type WARNING; $Warnings += $Line; $WarningCount++ }
            }
            elseif ($VerboseScript) { Log-Output $Line } # Only log non-error/warning lines if VerboseScript
        }

        if ($BuildExitCode -eq 0 -and $ErrorCount -eq 0) {
            if ($WarningCount -gt 0) {
                Log-Output "Build completed successfully with $WarningCount warning(s)." -Type WARNING # Report warnings
            }
            else {
                Log-Output "Build completed successfully" -Type SUCCESS
            }
            return $true
        }
        else {
            if ($ErrorCount -gt 0) {
                Log-Output "Build failed with $ErrorCount error(s) and $WarningCount warning(s)" -Type ERROR
            }
            else {
                Log-Output "Build failed (exit code $BuildExitCode) with $WarningCount warning(s)" -Type ERROR
            }
            return $false
        }
    }
    catch { Log-Output "Build process error: $_" -Type ERROR; return $false }
}

# Run tests
function Run-Tests {
    Log-Output "Running Tests" -Type HEADER
    $OverallResult = $true
    $TestCount = 0; $PassCount = 0; $FailCount = 0
    $TestFiles = Get-ChildItem -Path $ExampleDir -Filter "*.lse"
    $TotalFiles = $TestFiles.Count
    Log-Output "Found $TotalFiles test files to process."

    foreach ($File in $TestFiles) {
        $TestCount++
        $PaddedCount = "{0:D3}" -f $TestCount

        # Determine path for the .log file (next to .lse)
        $LseLogFilePath = Join-Path $File.DirectoryName "$($File.BaseName).log"

        Log-Output -Message "`n===== [$TestCount/$TotalFiles] Processing: $($File.Name) =====" -Type HEADER

        # CLI arguments
        $CliArgs = @(
            "--project", $CliProjectPath,
            "--", # Separator for passthrough arguments
            "--input-file", $File.FullName
        )
        if ($VerboseCli) {
            $CliArgs += "--verbose" # This tells CLI to be verbose to console AND create its default .log file
            # The CLI will create its own $LseLogFilePath if --verbose is passed and no --verbose-log-file is specified
        }
        else {
            # If CLI is not verbose, explicitly tell it to create the .log file for this script's record keeping,
            # especially for failures.
            $CliArgs += "--verbose-log-file", $LseLogFilePath
        }
        # Add other CLI options if needed, e.g. --solver based on script params

        try {
            # Run the test; CLI output will go to console if not quiet, and to its own .log if verbose
            $CliOutput = $(dotnet run $CliArgs 2>&1) # Capture CLI's stdout and stderr
            $Result = $LASTEXITCODE

            # Log CLI's own console output to this script's overall log for summary
            if ($VerboseScript -or $Result -ne 0) {
                # Log output if script is verbose or if there was an error
                [void]$ScriptLogContent.AppendLine("--- CLI Output for $($File.Name) ---")
                $CliOutput | ForEach-Object { [void]$ScriptLogContent.AppendLine("  $_") }
                [void]$ScriptLogContent.AppendLine("--- End CLI Output ---")
            }

            if ($Result -ne 0) {
                $FailCount++; $OverallResult = $false
                Log-Output -Message "Processing $($File.Name) FAILED (CLI Exit Code: $Result)" -Type ERROR
                Log-Output -Message "Detailed execution log for this failure should be at: $LseLogFilePath" -Type INFO # Inform about the .log file
                if (-not $Continue) {
                    Log-Output "Stopping on first failure. Use -Continue to run all tests." -Type WARNING
                    break
                }
            }
            else {
                $PassCount++
                Log-Output -Message "Processing $($File.Name) succeeded" -Type SUCCESS
                if ($VerboseCli) {
                    # If CLI was verbose, it created a .log file
                    Log-Output -Message "Detailed execution log for this success should be at: $LseLogFilePath" -Type INFO
                }
            }
        }
        catch {
            $FailCount++; $OverallResult = $false
            Log-Output -Message "Error running CLI for $($File.Name): $_" -Type ERROR
            if (-not $Continue) {
                Log-Output "Stopping on first failure. Use -Continue to run all tests." -Type WARNING
                break
            }
        }
    }

    Log-Output "Test Summary" -Type HEADER
    Log-Output "Total tests attempted: $TestCount/$TotalFiles"
    Log-Output "Passed: $PassCount"
    Log-Output "Failed: $FailCount"
    if ($OverallResult) { Log-Output "OVERALL TEST RESULT: PASSED" -Type SUCCESS }
    else { Log-Output "OVERALL TEST RESULT: FAILED" -Type ERROR }

    return $OverallResult
}

# Function to clear logs and output files
function Clear-OutputFiles {
    Log-Output "Clearing previous output files..." -Type HEADER

    # Clear specific CLI output files from examples directory
    $ExampleFiles = Get-ChildItem -Path $ExampleDir
    foreach ($File in $ExampleFiles) {
        if ($File.Extension -eq ".log" -or $File.Extension -eq ".md" -or $File.Extension -eq ".svg") {
            if ($File.Name -notlike "README.md") {
                # Avoid deleting a potential README in examples
                if ($VerboseScript) { Log-Output "Removing $($File.FullName)" }
                Remove-Item -Path $File.FullName -Force -ErrorAction SilentlyContinue
            }
        }
    }
    Log-Output "Cleared .log, .md, .svg files from $ExampleDir" -Type INFO

    # Clear PowerShell script's own summary logs from root logs directory
    # Keep Serilog/AOP runtime logs if they are named differently or in subfolders.
    # This example clears all .log files from $ScriptLogDir - be cautious if GUI Serilog logs here too.
    # It's better if CLI Serilog runtime logs and GUI Serilog runtime logs go to a specific subfolder or have distinct names.
    if (Test-Path $ScriptLogDir) {
        Get-ChildItem -Path $ScriptLogDir -Filter "*.log" | ForEach-Object {
            if ($VerboseScript) { Log-Output "Removing script log: $($_.FullName)" }
            Remove-Item -Path $_.FullName -Force -ErrorAction SilentlyContinue
        }
        Log-Output "Cleared previous script summary logs from $ScriptLogDir" -Type INFO
    }
}


# Main script execution
try {
    Clear-Host
    Log-Output -Message "LibreSolvE Build & Test Script" -Type HEADER
    Log-Output -Message "==================================" -Type HEADER

    Clear-OutputFiles

    if (-not (Configure-JavaEnvironment)) { exit 1 }
    if ($Clean) { Clean-Solution }
    if (-not (Build-Solution)) {
        Log-Output "Build failed. Script will exit." -Type ERROR
        exit 1
    }

    $TestResult = $true
    if (-not $NoTests) {
        $TestResult = Run-Tests
    }

    Log-Output "Build and test script finished." -Type HEADER
    if ($TestResult) { exit 0 } else { exit 1 }

}
catch {
    Log-Output "SCRIPT EXECUTION FAILED: $_" -Type ERROR
    exit 1
}
finally {
    # Write the script's overall log file
    Set-Content -Path $ScriptOverallLogFile -Value $ScriptLogContent.ToString() -Encoding UTF8
    Write-Host "`nOverall script log saved to: $ScriptOverallLogFile"
}
