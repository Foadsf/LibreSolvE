// In LibreSolvE.CLI/Logging/LoggingHelper.cs
using Serilog;
using Serilog.Events;
using Spectre.Console;  // Add this import for AnsiConsole
using System;
using System.IO;
using System.Reflection; // Required for Assembly to get version

namespace LibreSolvE.CLI.Logging
{
    public static class LoggingHelper
    {
        public static void ConfigureSerilog(string[] args, out string determinedCliLogDirectory, out string determinedCliGeneralSerilogPath)
        {
            // Determine base path for logs (e.g., next to executable or in a project 'logs' folder)
            try
            {
                // Try to find project root to place logs folder at the solution level
                string currentDir = AppDomain.CurrentDomain.BaseDirectory; // Typically ...\bin\Debug\netX.Y
                // Navigate up to find solution root (heuristic, adjust if your folder structure is different)
                // Assuming CLI project is like SolvE/LibreSolvE.CLI, so up 3 levels to SolvE/, then logs/
                string projectRootPath = Path.GetFullPath(Path.Combine(currentDir, "..", "..", ".."));
                determinedCliLogDirectory = Path.Combine(projectRootPath, "logs");
                Directory.CreateDirectory(determinedCliLogDirectory); // Ensure solution-level logs dir exists
            }
            catch (Exception ex) // Fallback if navigating up fails (e.g. deployed scenario)
            {
                Console.WriteLine($"Warning: Could not determine project root for logs. Falling back. Error: {ex.Message}");
                determinedCliLogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cli_runtime_logs");
                Directory.CreateDirectory(determinedCliLogDirectory); // Ensure local logs dir exists
            }
            determinedCliGeneralSerilogPath = Path.Combine(determinedCliLogDirectory, "cli_serilog_runtime_.log");

            // Base logger configuration
            var loggerConfiguration = new LoggerConfiguration()
                .MinimumLevel.Debug() // Log everything from Debug level up
                .Enrich.FromLogContext()
                .Enrich.WithProcessId()
                .Enrich.WithThreadId();
            // Add other enrichers if needed, e.g., EnvironmentUserName
            // .Enrich.WithEnvironmentUserName();


            // Add File Sink - always logs Debug level and above
            loggerConfiguration.WriteTo.File(determinedCliGeneralSerilogPath,
                          rollingInterval: RollingInterval.Day, // New log file each day
                          retainedFileCountLimit: 7,            // Keep logs for 7 days
                          outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                          restrictedToMinimumLevel: LogEventLevel.Debug);

            // Console Sink - its minimum level will be set later based on --quiet and --verbose flags
            // For now, we create the logger with just the file sink.
            // The console sink will be added in UpdateLoggerForConsoleVerbosity.
            Log.Logger = loggerConfiguration.CreateLogger();

            // Set up global unhandled exception handler to log fatal errors
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                Log.Fatal(e.ExceptionObject as Exception, "Unhandled CLI exception occurred. Application will terminate.");
                Log.CloseAndFlush(); // Attempt to flush logs before crashing
            };

            string version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "dev";
            Log.Information("LibreSolvE CLI v{Version} Serilog Initialized. Runtime Log: {LogPath}. Args: {Args}",
                version,
                determinedCliGeneralSerilogPath,
                string.Join(" ", args));
        }

        // UpdateLoggerForConsoleVerbosity and VerifyLogging methods remain the same as before
        public static void UpdateLoggerForConsoleVerbosity(
            string cliGeneralSerilogPath, // Path to the main file log
            bool quiet,
            bool verboseConsole)
        {
            // Start with the base configuration (enrichers, etc.)
            var finalLoggerConfig = new LoggerConfiguration()
                .MinimumLevel.Debug() // Set base minimum, sinks will filter
                .Enrich.FromLogContext()
                .Enrich.WithProcessId()
                .Enrich.WithThreadId();

            // Always add the file sink for Debug level and above
            finalLoggerConfig.WriteTo.File(
                cliGeneralSerilogPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
                restrictedToMinimumLevel: LogEventLevel.Debug);

            // Add console sink based on quiet/verbose settings
            if (!quiet)
            {
                finalLoggerConfig.WriteTo.Console(
                    outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}", // Simpler template for console
                    restrictedToMinimumLevel: verboseConsole ? LogEventLevel.Debug : LogEventLevel.Information);
            }

            // Apply the new configuration
            Log.Logger = finalLoggerConfig.CreateLogger();

            // Log level info
            if (verboseConsole && !quiet) Log.Debug("Console logging reconfigured to Debug level.");
            else if (!quiet) Log.Debug("Console logging reconfigured to Information level.");
            else Log.Debug("Console logging is off (quiet mode).");
        }

        public static void VerifyLogging(string logDirectory) // logDirectory here is the one determined for Serilog files
        {
            Log.Information("--- Starting Log Verification ---");
            try
            {
                if (!Directory.Exists(logDirectory))
                {
                    Log.Warning("Log directory for Serilog does not exist: {LogDirectoryPath}", logDirectory);
                    try
                    {
                        Directory.CreateDirectory(logDirectory);
                        Log.Information("Created Serilog log directory: {LogDirectoryPath}", logDirectory);
                    }
                    catch (Exception dirEx)
                    {
                        Log.Error(dirEx, "FAILED to create Serilog log directory: {LogDirectoryPath}", logDirectory);
                        AnsiConsole.MarkupLine($"[red]CLI: FAILED to create Serilog log directory: {logDirectory}[/]");
                        return; // Cannot proceed with file test if dir creation failed
                    }
                }

                string testLogPath = Path.Combine(logDirectory, "serilog_startup_test.txt");
                try
                {
                    File.WriteAllText(testLogPath, $"Serilog startup test log entry created at {DateTime.Now}");
                    Log.Information("Successfully wrote a startup test log file to: {TestLogFilePath}", testLogPath);
                }
                catch (Exception fileEx)
                {
                    Log.Error(fileEx, "FAILED to write startup test log file to: {TestLogFilePath}", testLogPath);
                    AnsiConsole.MarkupLine($"[red]CLI: FAILED to write startup test log to: {testLogPath}[/]");
                }

                Log.Debug("Serilog: This is a Debug test message for verification.");
                Log.Information("Serilog: This is an Information test message for verification.");
                Log.Warning("Serilog: This is a Warning test message for verification.");
                // Log.Error("Serilog: This is an Error test message for verification."); // Uncomment to test error logging

                // Check the configured cliGeneralSerilogPath (static field in Program.cs)
                if (!string.IsNullOrEmpty(Program._cliGeneralSerilogFilePath) && File.Exists(Program._cliGeneralSerilogFilePath))
                {
                    Log.Information("Serilog messages should be appearing in the main CLI runtime log: {MainCliLogPath}", Program._cliGeneralSerilogFilePath);
                }
                else
                {
                    Log.Warning("Main CLI runtime log path not found or not yet created: {MainCliLogPath}", Program._cliGeneralSerilogFilePath);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during Serilog verification process.");
                AnsiConsole.MarkupLine($"[red]CLI: Error during Serilog verification: {ex.Message}[/]");
            }
            Log.Information("--- Finished Log Verification ---");
        }
    }
}
