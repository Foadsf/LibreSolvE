# build.ps1
# Cross-platform build and test script for LibreSolvE

# Parameters
param (
    [switch]$Verbose = $false,
    [switch]$Continue = $false,
    [switch]$NoTests = $false,
    [switch]$Clean = $false
)

# Set error action preference
$ErrorActionPreference = "Stop"

# Import modules for better console output
if (-not (Get-Module -Name PSWriteColor -ListAvailable)) {
    Write-Host "Installing PSWriteColor module for improved output..."
    Install-Module -Name PSWriteColor -Force -Scope CurrentUser
}
Import-Module PSWriteColor -ErrorAction SilentlyContinue

# Define paths
$RootDir = $PSScriptRoot
$LogDir = Join-Path $RootDir "logs"
$ExampleDir = Join-Path $RootDir "examples"

# Ensure log directory exists
if (-not (Test-Path -Path $LogDir)) {
    New-Item -Path $LogDir -ItemType Directory | Out-Null
}

# Function to show a header
function Show-Header {
    param (
        [string]$Title
    )
    Write-Color -Text "`n==== ", $Title, " ====" -Color White, Cyan, White
}

# Function to show a success message
function Show-Success {
    param (
        [string]$Message
    )
    Write-Color -Text "[", "SUCCESS", "] ", $Message -Color White, Green, White, White
}

# Function to show an error message
function Show-Error {
    param (
        [string]$Message
    )
    Write-Color -Text "[", "ERROR", "] ", $Message -Color White, Red, White, White
}

# Function to show a warning message
function Show-Warning {
    param (
        [string]$Message
    )
    Write-Color -Text "[", "WARNING", "] ", $Message -Color White, Yellow, White, White
}

# Function to show an info message
function Show-Info {
    param (
        [string]$Message
    )
    Write-Color -Text "[", "INFO", "] ", $Message -Color White, Blue, White, White
}

# Configure Java environment for ANTLR
function Configure-JavaEnvironment {
    Show-Header "Configuring Java Environment"

    # Try to find Java installation
    $JavaPaths = @(
        "C:\Program Files\OpenJDK\jdk-22.0.2",
        "C:\Program Files\Microsoft\jdk-11.0.27.6-hotspot",
        "C:\Program Files\Java\jdk-17",
        "C:\Program Files\Java\jdk-11",
        "C:\Program Files\Eclipse Adoptium\jdk-17"
    )

    $JavaFound = $false
    $DesiredJavaHome = $null

    # Check each potential Java location
    foreach ($Path in $JavaPaths) {
        if (Test-Path (Join-Path $Path "bin\java.exe")) {
            $DesiredJavaHome = $Path
            $JavaFound = $true
            if ($Verbose) {
                Show-Info "Found Java at: $DesiredJavaHome"
            }
            break
        }
    }

    # If not found in standard locations, check JAVA_HOME
    if (-not $JavaFound -and $env:JAVA_HOME) {
        if (Test-Path (Join-Path $env:JAVA_HOME "bin\java.exe")) {
            $DesiredJavaHome = $env:JAVA_HOME
            $JavaFound = $true
            if ($Verbose) {
                Show-Info "Found Java via existing JAVA_HOME: $DesiredJavaHome"
            }
        }
    }

    # If still not found, try to find java in PATH
    if (-not $JavaFound) {
        try {
            $JavaLocation = (Get-Command java -ErrorAction SilentlyContinue).Source
            if ($JavaLocation) {
                $DesiredJavaHome = Split-Path (Split-Path $JavaLocation -Parent) -Parent
                $JavaFound = $true
                if ($Verbose) {
                    Show-Info "Found Java in PATH: $DesiredJavaHome"
                }
            }
        }
        catch {
            # Java not in PATH
        }
    }

    if (-not $JavaFound) {
        Show-Error "No suitable Java installation found."
        exit 1
    }

    # Set Java environment variables for this session
    $env:JAVA_HOME = $DesiredJavaHome
    $env:Path = "$($env:JAVA_HOME)\bin;$($env:Path)"

    Show-Success "Java environment configured successfully"
}

# Clean solution
function Clean-Solution {
    Show-Header "Cleaning Solution"

    dotnet clean --nologo

    Show-Success "Solution cleaned successfully"
}

# Build solution
function Build-Solution {
    Show-Header "Building Solution"

    $BuildVerbosity = if ($Verbose) { "--verbosity detailed" } else { "" }

    # Use try-catch to capture build errors
    try {
        # Run the build and capture all output
        $BuildOutput = $(dotnet build --nologo $BuildVerbosity 2>&1)
        $BuildExitCode = $LASTEXITCODE

        # Variables to track errors and warnings
        $ErrorCount = 0
        $WarningCount = 0

        # Process and display build output
        $BuildOutput | ForEach-Object {
            $Line = $_
            if ($Line -match ": error ") {
                Show-Error $Line
                $ErrorCount++
            }
            elseif ($Line -match ": warning ") {
                Show-Warning $Line
                $WarningCount++
            }
            elseif ($Verbose) {
                Write-Host $Line
            }
        }

        # Check if the build actually succeeded (exit code 0 and no errors)
        if ($BuildExitCode -eq 0 -and $ErrorCount -eq 0) {
            Show-Success "Build completed successfully"
            return $true
        }
        else {
            if ($ErrorCount -gt 0) {
                Show-Error "Build failed with $ErrorCount error(s) and $WarningCount warning(s)"
            }
            else {
                Show-Error "Build failed (exit code $BuildExitCode)"
            }
            return $false
        }
    }
    catch {
        Show-Error "Build process error: $_"
        return $false
    }
}

# Run tests
function Run-Tests {
    Show-Header "Running Tests"

    $OverallResult = $true
    $TestCount = 0
    $PassCount = 0
    $FailCount = 0

    # Get all .lse files in the examples directory
    $TestFiles = Get-ChildItem -Path $ExampleDir -Filter "*.lse"
    $TotalFiles = $TestFiles.Count

    Show-Info "Found $TotalFiles test files to process."

    foreach ($File in $TestFiles) {
        $TestCount++
        $PaddedCount = "{0:D3}" -f $TestCount
        $LogFile = Join-Path $LogDir "run_${PaddedCount}_$($File.Name).txt"

        Write-Color -Text "`n===== [", "$TestCount", "/", "$TotalFiles", "] Processing: ", $File.Name, " =====" -Color White, Cyan, White, Cyan, White, Cyan, White

        try {
            # Run the test and capture output
            $Output = dotnet run --project LibreSolvE.CLI\LibreSolvE.CLI.csproj -- --input-file "$($File.FullName)" --verbose 2>&1
            $Result = $LASTEXITCODE

            # Save output to log file
            $Output | Out-File -FilePath $LogFile -Encoding utf8

            if ($Result -ne 0) {
                $FailCount++
                $OverallResult = $false
                Show-Error "Processing $($File.Name) failed"

                if (-not $Continue) {
                    Show-Warning "Stopping on first failure. Use -Continue to run all tests."
                    break
                }
            }
            else {
                $PassCount++
                Show-Success "Processing $($File.Name) succeeded"
            }
        }
        catch {
            $FailCount++
            $OverallResult = $false
            Show-Error "Error processing $($File.Name): $_"

            if (-not $Continue) {
                Show-Warning "Stopping on first failure. Use -Continue to run all tests."
                break
            }
        }

        Show-Info "Log file: $LogFile"
    }

    # Summary
    Show-Header "Test Summary"
    Write-Host "Total tests: $TestCount/$TotalFiles"
    Write-Host "Passed: $PassCount"
    Write-Host "Failed: $FailCount"

    if ($OverallResult) {
        Show-Success "ALL TESTS PASSED successfully."
    }
    else {
        Show-Error "SOME TESTS FAILED. Check log files for details."
    }

    Write-Host "Log files are available in: $LogDir\"

    return $OverallResult
}

# Main script execution
try {
    # Show banner
    Clear-Host
    Write-Color -Text "`n", "LibreSolvE", " Build & Test Script" -Color White, Green, White
    Write-Host "=================================="

    # Configure Java environment
    Configure-JavaEnvironment

    # Clean if requested
    if ($Clean) {
        Clean-Solution
    }

    # Build solution
    $BuildResult = Build-Solution

    # Only run tests if build succeeded and tests are not disabled
    if ($BuildResult -and -not $NoTests) {
        $TestResult = Run-Tests
        if (-not $TestResult) {
            exit 1
        }
    }
    elseif (-not $BuildResult) {
        Show-Error "Build failed. Tests will not be run."
        exit 1
    }

    # All done
    if ($BuildResult -and ($NoTests -or $TestResult)) {
        Show-Success "Build and test completed successfully"
    }
    exit 0
}
catch {
    Show-Error "Script execution failed: $_"
    exit 1
}
