using Serilog;
using Serilog.Events;
using System;
using System.IO;
using System.Reflection;

namespace LibreSolvE.CLI.Logging
{
    /// <summary>
    /// Helper class for configuring and managing Serilog logging for the CLI application
    /// </summary>
    public static class LoggingHelper
    {
        /// <summary>
        /// Configures the Serilog logger for the CLI application
        /// </summary>
        public static void ConfigureSerilog(string[] args, out string cliLogDirectory, out string cliGeneralSerilogPath)
        {
            // Set up log directory
            try
            {
                string currentDir = AppDomain.CurrentDomain.BaseDirectory;
                string projectRootPath = Path.GetFullPath(Path.Combine(currentDir, "..", "..", ".."));
                cliLogDirectory = Path.Combine(projectRootPath, "logs");
                Directory.CreateDirectory(cliLogDirectory);
            }
            catch
            {
                cliLogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cli_runtime_logs");
                Directory.CreateDirectory(cliLogDirectory);
            }
            cliGeneralSerilogPath = Path.Combine(cliLogDirectory, "cli_serilog_runtime_.log");

            // Basic logger configuration
            var loggerConfigBase = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.FromLogContext();

            // Add thread ID enricher if the package is available
            try
            {
                // Try to load the enricher but don't fail if not available
                var threadType = Type.GetType("Serilog.Enrichers.ThreadIdEnricher, Serilog.Enrichers.Thread");
                if (threadType != null)
                {
                    // The package is available, use it
                    loggerConfigBase = loggerConfigBase.Enrich.With(Activator.CreateInstance(threadType) as Serilog.Core.ILogEventEnricher);
                }
            }
            catch
            {
                // Just continue without this enricher
            }

            // Add file sink
            loggerConfigBase = loggerConfigBase.WriteTo.File(
                cliGeneralSerilogPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                restrictedToMinimumLevel: LogEventLevel.Debug);

            // Add console sink
            loggerConfigBase = loggerConfigBase.WriteTo.Console(
                outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                restrictedToMinimumLevel: LogEventLevel.Information);

            // Create the logger
            Log.Logger = loggerConfigBase.CreateLogger();

            // Set up global unhandled exception handler
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                Log.Fatal(e.ExceptionObject as Exception, "Unhandled CLI exception occurred.");
            };

            // Log startup information
            Log.Information("LibreSolvE CLI Application Started. Args: {Args}", string.Join(" ", args));
        }

        /// <summary>
        /// Updates the logger configuration with console verbosity settings
        /// </summary>
        public static void UpdateLoggerForConsoleVerbosity(
            string cliGeneralSerilogPath,
            bool quiet,
            bool verboseConsole)
        {
            // Create a new logger configuration
            var finalLoggerConfig = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.FromLogContext();

            // Add file sink
            finalLoggerConfig = finalLoggerConfig.WriteTo.File(
                cliGeneralSerilogPath,
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                restrictedToMinimumLevel: LogEventLevel.Debug);

            // Add console sink based on quiet/verbose settings
            if (!quiet)
            {
                finalLoggerConfig = finalLoggerConfig.WriteTo.Console(
                    outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                    restrictedToMinimumLevel: verboseConsole ? LogEventLevel.Debug : LogEventLevel.Information);
            }

            // Apply the new configuration
            Log.Logger = finalLoggerConfig.CreateLogger();

            // Log level info
            if (verboseConsole && !quiet) Log.Debug("Console logging set to Debug level");
            else if (!quiet) Log.Debug("Console logging set to Information level");
        }

        /// <summary>
        /// Checks if logs are being properly generated and writes test log entries
        /// </summary>
        public static void VerifyLogging(string logDirectory)
        {
            try
            {
                // Check if the log directory exists
                if (!Directory.Exists(logDirectory))
                {
                    Console.WriteLine($"WARNING: Log directory does not exist: {logDirectory}");
                    try
                    {
                        Directory.CreateDirectory(logDirectory);
                        Console.WriteLine($"Created log directory: {logDirectory}");
                    }
                    catch (Exception dirEx)
                    {
                        Console.WriteLine($"FAILED to create log directory: {dirEx.Message}");
                    }
                }

                // Check if we can write a test log file
                string testLogPath = Path.Combine(logDirectory, "log_test.txt");
                try
                {
                    File.WriteAllText(testLogPath, $"Test log created at {DateTime.Now}");
                    Console.WriteLine($"Successfully wrote test log to: {testLogPath}");
                }
                catch (Exception fileEx)
                {
                    Console.WriteLine($"FAILED to write test log file: {fileEx.Message}");
                }

                // Try to write to Serilog
                Log.Information("Serilog verification test message");
                Log.Debug("Serilog debug test message");

                Console.WriteLine($"Attempted to write Serilog messages. Check {logDirectory} for log files.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during log verification: {ex.Message}");
            }
        }
    }
}
