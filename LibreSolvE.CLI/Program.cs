// LibreSolvE.CLI/Program.cs
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Reflection;
using System.Text;
using Antlr4.Runtime;
// Ensure this using is present if LogAttribute is in this namespace, or adjust as needed
using LibreSolvE.CLI.Logging; // For [Log] attribute if LogAttribute.cs is in LibreSolvE.CLI/Logging
using LibreSolvE.Core.Parsing;
using LibreSolvE.Core.Ast;
using LibreSolvE.Core.Evaluation;
using LibreSolvE.Core.Plotting;
using Spectre.Console; // For AnsiConsole
using System.IO;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Serilog;
using Serilog.Events;

namespace LibreSolvE.CLI;

#region Supporting Enums
public enum PlotFormat
{
    SVG,
    PNG,
    PDF
}
#endregion Supporting Enums

class Program
{
    // Path for the general Serilog runtime log file for the CLI application itself
    // Made this public static so it can be referenced by VerifySerilogConfiguration if needed,
    // though it's better if VerifySerilogConfiguration takes it as a parameter.
    // For now, to fix the immediate error, let's make it accessible.
    public static string _cliGeneralSerilogFilePath = string.Empty;

    #region Serilog Configuration and Helpers (Moved from LoggingHelper.cs)
    private static void ConfigureGlobalSerilog(string[] args)
    {
        string cliLogDirectory;
        try
        {
            string currentDir = AppDomain.CurrentDomain.BaseDirectory;
            string projectRootPath = Path.GetFullPath(Path.Combine(currentDir, "..", "..", ".."));
            cliLogDirectory = Path.Combine(projectRootPath, "logs");
            Directory.CreateDirectory(cliLogDirectory);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[yellow]Warning: Could not determine project root for Serilog logs. Falling back. Error: {ex.Message}[/]");
            cliLogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cli_runtime_logs");
            Directory.CreateDirectory(cliLogDirectory);
        }
        _cliGeneralSerilogFilePath = Path.Combine(cliLogDirectory, "cli_serilog_runtime_.log");

        var loggerConfiguration = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .Enrich.WithProcessId()
            .Enrich.WithThreadId()
            .WriteTo.File(_cliGeneralSerilogFilePath,
                          rollingInterval: RollingInterval.Day,
                          retainedFileCountLimit: 7,
                          outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
                          restrictedToMinimumLevel: LogEventLevel.Debug);

        bool initialQuiet = args.Contains("--quiet") || args.Contains("-q");
        if (!initialQuiet)
        {
            loggerConfiguration.WriteTo.Console(
                outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                restrictedToMinimumLevel: LogEventLevel.Information); // Default console level
        }

        Log.Logger = loggerConfiguration.CreateLogger();

        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            Log.Fatal(e.ExceptionObject as Exception, "Unhandled CLI exception occurred. Application will terminate.");
            Log.CloseAndFlushAsync().GetAwaiter().GetResult();
        };

        string version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "dev";
        // Initial log now happens after potentially reconfiguring console level in Main's SetHandler
        // Log.Information("LibreSolvE CLI v{Version} Serilog Initialized. Runtime Log: {LogPath}. Initial Args: {Args}",
        //     version, _cliGeneralSerilogFilePath, string.Join(" ", args));
    }

    private static void UpdateSerilogConsoleLevel(bool quiet, bool verboseConsole)
    {
        var finalLoggerConfig = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .Enrich.WithProcessId()
            .Enrich.WithThreadId()
            .WriteTo.File(_cliGeneralSerilogFilePath,
                          rollingInterval: RollingInterval.Day,
                          outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
                          restrictedToMinimumLevel: LogEventLevel.Debug);

        if (!quiet)
        {
            finalLoggerConfig.WriteTo.Console(
                outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                restrictedToMinimumLevel: verboseConsole ? LogEventLevel.Debug : LogEventLevel.Information);
        }
        Log.Logger = finalLoggerConfig.CreateLogger();

        if (verboseConsole && !quiet) Log.Debug("Console logging reconfigured by CLI args to Debug level.");
        else if (!quiet) Log.Debug("Console logging reconfigured by CLI args to Information level.");
        else Log.Debug("Console logging is off (quiet mode from CLI args).");
    }

    // Optional: Call this from Main after ConfigureGlobalSerilog if you want a startup log verification
    private static void VerifySerilogConfiguration()
    {
        Log.Information("--- Verifying Serilog Configuration ---");
        // Accessing the static field directly since we are in the same class
        if (!string.IsNullOrEmpty(_cliGeneralSerilogFilePath) && Directory.Exists(Path.GetDirectoryName(_cliGeneralSerilogFilePath)))
        {
            string testLogPath = Path.Combine(Path.GetDirectoryName(_cliGeneralSerilogFilePath)!, "serilog_startup_verify.txt");
            try
            {
                File.WriteAllText(testLogPath, $"Serilog startup verification log entry created at {DateTime.Now}. Main log: {_cliGeneralSerilogFilePath}");
                Log.Information("Successfully wrote a startup verification log file to: {TestLogFilePath}", testLogPath);
            }
            catch (Exception fileEx)
            {
                Log.Error(fileEx, "FAILED to write startup verification log file to: {TestLogFilePath}", testLogPath);
                AnsiConsole.MarkupLine($"[red]CLI: FAILED to write startup verification log to: {testLogPath}[/]");
            }
        }
        else
        {
            Log.Warning("Main CLI Serilog runtime log path not set or directory does not exist: {MainCliLogPath}", _cliGeneralSerilogFilePath);
        }
        Log.Debug("Serilog Verification: Debug Message.");
        Log.Information("Serilog Verification: Information Message.");
        Log.Warning("Serilog Verification: Warning Message.");
        Log.Information("--- Finished Serilog Verification ---");
    }
    #endregion Serilog Configuration and Helpers

    #region Program Entry Point
    [Log]
    static async Task<int> Main(string[] args)
    {
        ConfigureGlobalSerilog(args);
        // VerifySerilogConfiguration(); // Optional: Call for testing log setup

        var rootCommand = new RootCommand("LibreSolvE - A free equation solver and engineering calculation tool.");

        #region Command Line Options Definition
        var inputFileOption = new Option<FileInfo>(name: "--input-file", description: "The input .lse file to process.") { IsRequired = true };
        inputFileOption.AddAlias("-i");
        var outputFileOption = new Option<FileInfo?>(name: "--output-file", description: "Explicit file for concise results (e.g., results.md). Overrides default naming.");
        outputFileOption.AddAlias("-o");
        var solverOption = new Option<SolverType>(name: "--solver", description: "Solver: NelderMead, LevenbergMarquardt.", getDefaultValue: () => SolverType.NelderMead);
        solverOption.AddAlias("-s");
        var plotFormatOption = new Option<PlotFormat>(name: "--plot-format", description: "Plot output: SVG, PNG, PDF. Default: SVG.", getDefaultValue: () => PlotFormat.SVG);
        plotFormatOption.AddAlias("-p");
        var quietOption = new Option<bool>(name: "--quiet", description: "Suppress console status output and default .md/.log files.", getDefaultValue: () => false);
        quietOption.AddAlias("-q");
        var verboseConsoleOption = new Option<bool>(name: "--verbose", description: "Verbose console output. Also creates default .log for the .lse file if --verbose-log-file not set.", getDefaultValue: () => false);
        verboseConsoleOption.AddAlias("-v");
        var verboseLogFileOption = new Option<FileInfo?>(name: "--verbose-log-file", description: "Explicit path for per-LSE-file verbose log. Overrides default from --verbose.");
        verboseLogFileOption.AddAlias("-vl");

        rootCommand.AddOption(inputFileOption); rootCommand.AddOption(outputFileOption); rootCommand.AddOption(solverOption);
        rootCommand.AddOption(plotFormatOption); rootCommand.AddOption(quietOption); rootCommand.AddOption(verboseConsoleOption);
        rootCommand.AddOption(verboseLogFileOption);
        #endregion Command Line Options Definition

        rootCommand.SetHandler((context) =>
        {
            var inputFile = context.ParseResult.GetValueForOption(inputFileOption)!;
            var explicitOutputFile = context.ParseResult.GetValueForOption(outputFileOption);
            var solver = context.ParseResult.GetValueForOption(solverOption);
            var plotFormat = context.ParseResult.GetValueForOption(plotFormatOption);
            var quiet = context.ParseResult.GetValueForOption(quietOption);
            var verboseConsole = context.ParseResult.GetValueForOption(verboseConsoleOption);
            var explicitVerboseLogFile = context.ParseResult.GetValueForOption(verboseLogFileOption);

            UpdateSerilogConsoleLevel(quiet, verboseConsole);

            if (!quiet)
            {
                string version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "dev";
                AnsiConsole.MarkupLine($"[green bold]LibreSolvE CLI[/] v{version} (Serilog runtime log: [grey]{_cliGeneralSerilogFilePath}[/])");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"Processing: [cyan bold]{Path.GetFileName(inputFile.FullName)}[/]");
            }

            ExecuteProgram(inputFile, explicitOutputFile, solver, plotFormat, quiet, verboseConsole, explicitVerboseLogFile);
        });

        int result = await new CommandLineBuilder(rootCommand).UseDefaults().Build().InvokeAsync(args);

        Log.Information("LibreSolvE CLI Application Exiting with code {ExitCode}.", result);
        await Log.CloseAndFlushAsync();
        return result;
    }
    #endregion Program Entry Point

    #region Main Execution Logic
    [Log]
    static void ExecuteProgram(
        FileInfo inputFile,
        FileInfo? explicitConciseOutputFile,
        SolverType solverType,
        PlotFormat plotFormat,
        bool quiet,
        bool verboseConsoleRequested,
        FileInfo? explicitLseVerboseLogFile)
    {
        var exitCode = 0;
        var lseSpecificVerboseLogBuilder = new StringBuilder();
        string conciseResultsContent = $"# Processing Error for `{Path.GetFileName(inputFile.FullName)}`\n\nCore processing did not complete or failed.";

        TextWriter originalConsoleOut = Console.Out;
        StringWriter capturedCoreDirectConsoleOutput = new StringWriter();

        #region Determine Output File Paths
        FileInfo? conciseResultsMdFile = explicitConciseOutputFile;
        if (conciseResultsMdFile == null && !quiet)
        {
            conciseResultsMdFile = new FileInfo(Path.ChangeExtension(inputFile.FullName, ".md"));
        }

        FileInfo? actualLseVerboseLogFile = explicitLseVerboseLogFile;
        if (actualLseVerboseLogFile == null && verboseConsoleRequested && !quiet)
        {
            actualLseVerboseLogFile = new FileInfo(Path.ChangeExtension(inputFile.FullName, ".log"));
        }
        #endregion Determine Output File Paths

        try
        {
            lseSpecificVerboseLogBuilder.AppendLine($"# LibreSolvE Detailed Processing Log for: {inputFile.Name}");
            lseSpecificVerboseLogBuilder.AppendLine($"## Run Details");
            lseSpecificVerboseLogBuilder.AppendLine($"- Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            lseSpecificVerboseLogBuilder.AppendLine($"- Input File: `{inputFile.FullName}`");
            // ... (other header details for lseSpecificVerboseLogBuilder)
            lseSpecificVerboseLogBuilder.AppendLine($"- Actual LSE Verbose Log File Used: `{(actualLseVerboseLogFile?.FullName ?? "None (or console only if verbose)")}`");
            lseSpecificVerboseLogBuilder.AppendLine("---");

            Console.SetOut(capturedCoreDirectConsoleOutput);
            conciseResultsContent = ProcessFileCore(inputFile, solverType, plotFormat, lseSpecificVerboseLogBuilder, out exitCode);
            lseSpecificVerboseLogBuilder.Append(capturedCoreDirectConsoleOutput.ToString());
        }
        catch (Exception ex)
        {
            exitCode = 1;
            string errorHeader = $"\n--- FATAL CLI ORCHESTRATION ERROR for {inputFile.Name} ---";
            lseSpecificVerboseLogBuilder.AppendLine(errorHeader);
            lseSpecificVerboseLogBuilder.AppendLine($"{ex.GetType().Name}: {ex.Message}");
            lseSpecificVerboseLogBuilder.AppendLine(ex.StackTrace ?? "No stack trace available.");
            conciseResultsContent = $"# FATAL CLI ERROR for `{Path.GetFileName(inputFile.FullName)}`\n\n{errorHeader}\n```\n{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace ?? ""}\n```";
            Log.Error(ex, "Fatal error during ExecuteProgram orchestration for {InputFile}", inputFile.Name);
        }
        finally
        {
            if (Console.Out != originalConsoleOut) { Console.SetOut(originalConsoleOut); }

            #region Write LSE-Specific Verbose Log File
            if (actualLseVerboseLogFile != null)
            {
                try
                {
                    File.WriteAllText(actualLseVerboseLogFile.FullName, lseSpecificVerboseLogBuilder.ToString());
                    if (!quiet && verboseConsoleRequested) AnsiConsole.MarkupLine($"Detailed LSE log saved to: [yellow]{actualLseVerboseLogFile.FullName}[/]");
                    Log.Information("LSE-specific verbose log saved to {Path}", actualLseVerboseLogFile.FullName);
                }
                catch (Exception ex)
                {
                    if (!quiet) AnsiConsole.MarkupLine($"[red]Error saving detailed LSE log to {actualLseVerboseLogFile.FullName}: {ex.Message}[/]");
                    Log.Error(ex, "Error saving LSE-specific verbose log to {Path}", actualLseVerboseLogFile.FullName);
                }
            }
            #endregion Write LSE-Specific Verbose Log File

            #region Write Concise Results MD File
            if (conciseResultsMdFile != null)
            {
                try
                {
                    File.WriteAllText(conciseResultsMdFile.FullName, conciseResultsContent);
                    if (!quiet)
                    {
                        if (exitCode == 0) AnsiConsole.MarkupLine($"Results markdown saved to: [yellow]{conciseResultsMdFile.FullName}[/]");
                        else AnsiConsole.MarkupLine($"Error details/partial results saved to: [yellow]{conciseResultsMdFile.FullName}[/]");
                    }
                    Log.Information("Concise results MD file saved to {Path}", conciseResultsMdFile.FullName);
                }
                catch (Exception ex)
                {
                    if (!quiet) AnsiConsole.MarkupLine($"[red]Error saving concise results/errors to {conciseResultsMdFile.FullName}: {ex.Message}[/]");
                    Log.Error(ex, "Error saving concise results MD file to {Path}", conciseResultsMdFile.FullName);
                }
            }
            #endregion Write Concise Results MD File

            #region Final Console Status
            if (!quiet)
            {
                // AnsiConsole adds its own newlines often, so an explicit Console.WriteLine() might not be needed here.
                if (exitCode == 0) AnsiConsole.MarkupLine("[green bold]Processing completed successfully.[/]");
                else AnsiConsole.MarkupLine($"[red bold]Processing failed for {Path.GetFileName(inputFile.FullName)}. See logs or output files for details.[/]");
            }
            #endregion Final Console Status
        }
    }
    #endregion Main Execution Logic

    #region Core File Processing Logic
    [Log]
    static string ProcessFileCore(
        FileInfo inputFile,
        SolverType solverType,
        PlotFormat plotFormat,
        StringBuilder lseSpecificLogBuilder,
        out int outExitCode)
    {
        // ... (Implementation of ProcessFileCore remains the same as your last working version)
        // ... (Ensure it uses Serilog.Log for its own internal logging steps)
        // ... (And appends its structured output to lseSpecificLogBuilder)
        // ... (And returns the string for the concise .md output)
        var conciseResultsOutput = new StringBuilder();
        outExitCode = 0;

        conciseResultsOutput.AppendLine($"# Results for `{Path.GetFileName(inputFile.FullName)}`");
        conciseResultsOutput.AppendLine($"Solved with: `{solverType}`");
        conciseResultsOutput.AppendLine();

        Log.Information("Core: ProcessFileCore started for {InputFile}", inputFile.Name);

        try
        {
            lseSpecificLogBuilder.AppendLine($"--- Reading file: {inputFile.FullName} ---");
            string inputText = File.ReadAllText(inputFile.FullName);
            Log.Debug("Core: Extracting units from source...");
            var unitsDictionary = UnitParser.ExtractUnitsFromSource(inputText);
            lseSpecificLogBuilder.AppendLine($"--- Extracted {unitsDictionary.Count} units ---");
            Log.Debug("Core: Parsing file content...");
            AntlrInputStream inputStream = new AntlrInputStream(inputText);
            EesLexer lexer = new EesLexer(inputStream);
            CommonTokenStream commonTokenStream = new CommonTokenStream(lexer);
            EesParser parser = new EesParser(commonTokenStream);
            var errorListener = new BetterErrorListener();
            parser.RemoveErrorListeners(); lexer.RemoveErrorListeners();
            parser.AddErrorListener(errorListener); lexer.AddErrorListener(errorListener);
            EesParser.EesFileContext parseTreeContext = parser.eesFile();
            lseSpecificLogBuilder.AppendLine("--- Parsing successful ---");
            Log.Debug("Core: Building Abstract Syntax Tree (AST)...");
            var astBuilder = new AstBuilderVisitor();
            AstNode rootAstNode = astBuilder.VisitEesFile(parseTreeContext);
            if (rootAstNode is not EesFileNode fileNode)
            {
                lseSpecificLogBuilder.AppendLine("--- AST Building FAILED: Root node is not EesFileNode ---");
                conciseResultsOutput.AppendLine("**Error: AST Build Failed.**");
                outExitCode = 1; return conciseResultsOutput.ToString();
            }
            lseSpecificLogBuilder.AppendLine($"--- AST Built Successfully ({fileNode.Statements.Count} statements found) ---");
            Log.Debug("Core: Initializing Execution Environment...");
            var variableStore = new VariableStore();
            var functionRegistry = new FunctionRegistry();
            var solverSettings = new SolverSettings { SolverType = solverType };
            UnitParser.ApplyUnitsToVariableStore(variableStore, unitsDictionary);
            var executor = new StatementExecutor(variableStore, functionRegistry, solverSettings);
            var generatedPlotsInfo = new List<string>();
            executor.PlotCreated += (sender, plotData) =>
            {
                string plotBaseName = $"plot_{Path.GetFileNameWithoutExtension(inputFile.Name)}_{generatedPlotsInfo.Count + 1}";
                string plotFilename = Path.Combine(Path.GetDirectoryName(inputFile.FullName) ?? "", $"{plotBaseName}{GetPlotExtension(plotFormat)}");
                PlotExporter.ExportToFormat(plotData, plotFilename, plotFormat);
                lseSpecificLogBuilder.AppendLine($"Plot '{plotData.Settings.Title}' saved to: {plotFilename}");
                generatedPlotsInfo.Add($"Plot '{plotData.Settings.Title}' saved as `{Path.GetFileName(plotFilename)}`");
            };
            Log.Debug("Core: Executing statements (assignments, ODEs)...");
            executor.Execute(fileNode);
            lseSpecificLogBuilder.AppendLine("\n--- Variable Store State After Assignments/ODE (for .log file) ---");
            AppendVariableStoreToLog(variableStore, lseSpecificLogBuilder);
            Log.Debug("Core: Solving algebraic equations...");
            bool solveSuccess = executor.SolveRemainingAlgebraicEquations();
            conciseResultsOutput.AppendLine("```text");
            if (solveSuccess)
            {
                lseSpecificLogBuilder.AppendLine("\n--- Algebraic Solver Phase Completed Successfully (for .log file) ---");
                lseSpecificLogBuilder.AppendLine("\n" + new string('=', 25) + " FINAL RESULTS (for LSE verbose log) " + new string('=', 25));
                AppendVariableStoreToLog(variableStore, lseSpecificLogBuilder, conciseResultsOutput);
                outExitCode = 0;
            }
            else
            {
                lseSpecificLogBuilder.AppendLine("\n--- Algebraic Solver FAILED (for .log file) ---");
                conciseResultsOutput.AppendLine("Solver FAILED to converge or problem with equations.");
                lseSpecificLogBuilder.AppendLine("\n--- Variable Store State After Failed Solve Attempt (for .log file) ---");
                AppendVariableStoreToLog(variableStore, lseSpecificLogBuilder, conciseResultsOutput);
                outExitCode = 1;
            }
            conciseResultsOutput.AppendLine("```");
            if (generatedPlotsInfo.Any())
            {
                string plotSectionTitle = "\n## Plots Generated:";
                lseSpecificLogBuilder.AppendLine(plotSectionTitle); conciseResultsOutput.AppendLine(plotSectionTitle);
                foreach (var plotInfo in generatedPlotsInfo)
                {
                    lseSpecificLogBuilder.AppendLine($"- {plotInfo}"); conciseResultsOutput.AppendLine($"- {plotInfo}");
                }
            }
        }
        catch (ParsingException pEx)
        {
            Log.Error(pEx, "Core: Parsing failed for {InputFile}", inputFile.Name);
            lseSpecificLogBuilder.AppendLine($"\n--- PARSING FAILED ---\n{pEx.Message}");
            conciseResultsOutput.AppendLine($"\n**PARSING FAILED**\n```\n{pEx.Message}\n```");
            outExitCode = 1;
        }
        catch (IOException ioEx)
        {
            Log.Error(ioEx, "Core: File IO error for {InputFile}", inputFile.Name);
            lseSpecificLogBuilder.AppendLine($"\n--- FILE ERROR ---\n{ioEx.Message}");
            conciseResultsOutput.AppendLine($"\n**FILE ERROR**\n```\n{ioEx.Message}\n```");
            outExitCode = 1;
        }
        catch (InvalidOperationException opEx)
        {
            Log.Error(opEx, "Core: Invalid operation during execution for {InputFile}: {ErrorMessage}", inputFile.Name, opEx.Message);
            lseSpecificLogBuilder.AppendLine($"\n--- EXECUTION ERROR ---\n{opEx.Message}");
            conciseResultsOutput.AppendLine($"\n**EXECUTION ERROR**\n```\n{opEx.Message}\n```");
            outExitCode = 1;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Core: Unexpected error during processing of {InputFile}", inputFile.Name);
            lseSpecificLogBuilder.AppendLine($"\n--- UNEXPECTED CORE ERROR ---\n{ex.GetType().Name}: {ex.Message}");
            lseSpecificLogBuilder.AppendLine(ex.StackTrace ?? "No stack trace.");
            conciseResultsOutput.AppendLine($"\n**UNEXPECTED CORE ERROR**\n```\n{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace ?? ""}\n```");
            outExitCode = 1;
        }
        Log.Information("Core: ProcessFileCore finished for {InputFile} with exit code {ExitCode}", inputFile.Name, outExitCode);
        return conciseResultsOutput.ToString();
    }
    #endregion Core File Processing Logic

    #region Helper Methods
    static void AppendVariableStoreToLog(VariableStore store, StringBuilder verboseLogBuilder, StringBuilder? conciseLogBuilder = null)
    {
        StringWriter tempSw = new StringWriter();
        TextWriter originalConsole = Console.Out;
        Console.SetOut(tempSw);
        store.PrintVariables();
        Console.SetOut(originalConsole);

        string variableOutput = tempSw.ToString();
        // Avoid duplicating if already captured by capturedCoreDirectConsoleOutput
        // This check is heuristic. Better if PrintVariables uses Serilog.
        if (!verboseLogBuilder.ToString().EndsWith(variableOutput.TrimEnd()))
        {
            verboseLogBuilder.AppendLine(variableOutput);
        }

        conciseLogBuilder?.AppendLine(variableOutput);
    }

    static string GetPlotExtension(PlotFormat format)
    {
        return format switch
        {
            PlotFormat.SVG => ".svg",
            PlotFormat.PNG => ".png",
            PlotFormat.PDF => ".pdf",
            _ => ".svg"
        };
    }
    #endregion Helper Methods
} // This is the closing brace for the Program class
