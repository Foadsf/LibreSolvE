// LibreSolvE.CLI/Program.cs
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Reflection;
using System.Text;
using Antlr4.Runtime;
using LibreSolvE.CLI.Logging;
using LibreSolvE.Core.Parsing;
using LibreSolvE.Core.Ast;
using LibreSolvE.Core.Evaluation;
using LibreSolvE.Core.Plotting;
using Spectre.Console;
using System.IO;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Serilog;
using Serilog.Events; // For LogEventLevel

namespace LibreSolvE.CLI;

#region Supporting Enums
public enum PlotFormat
{
    SVG,
    PNG,
    PDF
}
#endregion Supporting Enums

// No class definition needed if Main is top-level statement (C# 9+),
// but for clarity and older compatibility, let's keep it in a class.
class Program
{
    // Shared static field for general CLI Serilog log path
    private static string cliGeneralSerilogPath = string.Empty;

    #region Program Entry Point
    [Log]
    static async Task<int> Main(string[] args)
    {
        string cliLogDirectory;

        // Set up Serilog first
        LoggingHelper.ConfigureSerilog(args, out cliLogDirectory, out cliGeneralSerilogPath);

        // to verify logging is working
        LoggingHelper.VerifyLogging(cliLogDirectory);

        try
        {
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

                // Update logger configuration for console verbosity
                LoggingHelper.UpdateLoggerForConsoleVerbosity(cliGeneralSerilogPath, quiet, verboseConsole);

                // Display console header if not quiet
                if (!quiet)
                {
                    string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "dev";
                    AnsiConsole.MarkupLine($"[green bold]LibreSolvE CLI[/] v{version} (Serilog runtime log: [grey]{cliGeneralSerilogPath}[/])");
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"Processing: [cyan bold]{Path.GetFileName(inputFile.FullName)}[/]");
                }

                ExecuteProgram(inputFile, explicitOutputFile, solver, plotFormat, quiet, verboseConsole, explicitVerboseLogFile);
            });

            int result = await new CommandLineBuilder(rootCommand).UseDefaults().Build().InvokeAsync(args);

            Log.Information("LibreSolvE CLI Application Exiting with code {ExitCode}.", result);
            await Log.CloseAndFlushAsync(); // Ensure all logs are written before exit
            return result;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Fatal error in CLI main method");
            await Log.CloseAndFlushAsync();
            return 1;
        }
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
            lseSpecificVerboseLogBuilder.AppendLine($"- Solver Algorithm: `{solverType}`");
            lseSpecificVerboseLogBuilder.AppendLine($"- Plot Output Format: `{plotFormat}`");
            lseSpecificVerboseLogBuilder.AppendLine($"- Quiet Mode: `{quiet}`");
            lseSpecificVerboseLogBuilder.AppendLine($"- Verbose Console Output Requested: `{verboseConsoleRequested}`");
            lseSpecificVerboseLogBuilder.AppendLine($"- Explicit Concise Output File: `{(explicitConciseOutputFile?.FullName ?? "N/A (derived if not quiet)")}`");
            lseSpecificVerboseLogBuilder.AppendLine($"- Explicit LSE Verbose Log File Target: `{(explicitLseVerboseLogFile?.FullName ?? "N/A")}`");
            lseSpecificVerboseLogBuilder.AppendLine($"- Actual LSE Verbose Log File Used: `{(actualLseVerboseLogFile?.FullName ?? "None (or console only if verbose)")}`");
            lseSpecificVerboseLogBuilder.AppendLine("---");

            Console.SetOut(capturedCoreDirectConsoleOutput); // Capture direct Console.Write from Core
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
            Log.Error(ex, "Fatal error during ExecuteProgram orchestration for {InputFile}", inputFile.Name); // Log to Serilog
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
                Console.WriteLine();
                if (exitCode == 0) AnsiConsole.MarkupLine("[green bold]Processing completed successfully.[/]");
                else AnsiConsole.MarkupLine($"[red bold]Processing failed for {inputFile.Name}. See logs or output files for details.[/]");
            }
            #endregion Final Console Status
        }
        // Environment.Exit is handled by Main's return value
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
        var conciseResultsOutput = new StringBuilder();
        outExitCode = 0;

        conciseResultsOutput.AppendLine($"# Results for `{Path.GetFileName(inputFile.FullName)}`");
        conciseResultsOutput.AppendLine($"Solved with: `{solverType}`");
        conciseResultsOutput.AppendLine();

        Log.Information("ProcessFileCore started for {InputFile}", inputFile.Name);

        try
        {
            lseSpecificLogBuilder.AppendLine($"--- Reading file: {inputFile.FullName} ---");
            string inputText = File.ReadAllText(inputFile.FullName);

            // Core library internal logging now uses Serilog's static Log class
            Log.Debug("Core: Extracting units from source...");
            var unitsDictionary = UnitParser.ExtractUnitsFromSource(inputText);
            lseSpecificLogBuilder.AppendLine($"--- Extracted {unitsDictionary.Count} units ---"); // Example of adding to LSE log

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
            lseSpecificLogBuilder.AppendLine("\n--- Variable Store State After Assignments/ODE ---");
            AppendVariableStoreToLog(variableStore, lseSpecificLogBuilder);

            Log.Debug("Core: Solving algebraic equations...");
            bool solveSuccess = executor.SolveRemainingAlgebraicEquations();

            conciseResultsOutput.AppendLine("```text");
            if (solveSuccess)
            {
                lseSpecificLogBuilder.AppendLine("\n--- Algebraic Solver Phase Completed Successfully ---");
                lseSpecificLogBuilder.AppendLine("\n" + new string('=', 25) + " FINAL RESULTS (for LSE log) " + new string('=', 25));
                AppendVariableStoreToLog(variableStore, lseSpecificLogBuilder, conciseResultsOutput);
                outExitCode = 0;
            }
            else
            {
                lseSpecificLogBuilder.AppendLine("\n--- Algebraic Solver FAILED ---");
                conciseResultsOutput.AppendLine("Solver FAILED to converge or problem with equations.");
                lseSpecificLogBuilder.AppendLine("\n--- Variable Store State After Failed Solve Attempt (for LSE log) ---");
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
            Log.Error(opEx, "Core: Invalid operation during execution for {InputFile}", inputFile.Name);
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
        Log.Information("ProcessFileCore finished for {InputFile} with exit code {ExitCode}", inputFile.Name, outExitCode);
        return conciseResultsOutput.ToString();
    }
    #endregion Core File Processing Logic

    #region Helper Methods
    static void AppendVariableStoreToLog(VariableStore store, StringBuilder verboseLogBuilder, StringBuilder? conciseLogBuilder = null)
    {
        StringWriter tempSw = new StringWriter();
        TextWriter originalConsole = Console.Out;
        Console.SetOut(tempSw);
        store.PrintVariables();     // This method in VariableStore should use Console.WriteLine
        Console.SetOut(originalConsole);

        string variableOutput = tempSw.ToString();
        verboseLogBuilder.AppendLine(variableOutput);
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
} // This should be the final closing brace for the Program class
