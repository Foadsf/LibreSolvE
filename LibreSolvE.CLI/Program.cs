// LibreSolvE.CLI/Program.cs
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Reflection;
using System.Text;
using Antlr4.Runtime;
using LibreSolvE.Core.Parsing;
using LibreSolvE.Core.Ast;
using LibreSolvE.Core.Evaluation;
using LibreSolvE.Core.Plotting;
using Spectre.Console;

namespace LibreSolvE.CLI;

class Program
{
    static async Task<int> Main(string[] args)
    {
        // Create command structure
        var rootCommand = new RootCommand("LibreSolvE - A free equation solver and engineering calculation tool");

        // Input file option (required)
        var inputFileOption = new Option<FileInfo>(
            name: "--input-file",
            description: "The input .lse file to process")
        {
            IsRequired = true
        };
        inputFileOption.AddAlias("-i");

        // Output file option (optional)
        var outputFileOption = new Option<FileInfo?>(
            name: "--output-file",
            description: "The file to write results to (default: [input-file-name].out.txt)");
        outputFileOption.AddAlias("-o");

        // Solver algorithm option
        var solverOption = new Option<SolverType>(
            name: "--solver",
            description: "The solver algorithm to use",
            getDefaultValue: () => SolverType.NelderMead);
        solverOption.AddAlias("-s");

        // Plot format option
        var plotFormatOption = new Option<PlotFormat>(
            name: "--plot-format",
            description: "Format for plot output files",
            getDefaultValue: () => PlotFormat.SVG);
        plotFormatOption.AddAlias("-p");

        // Quiet mode option
        var quietOption = new Option<bool>(
            name: "--quiet",
            description: "Suppress console output except for errors",
            getDefaultValue: () => false);
        quietOption.AddAlias("-q");

        // Verbose mode option
        var verboseOption = new Option<bool>(
            name: "--verbose",
            description: "Print detailed execution information",
            getDefaultValue: () => false);
        verboseOption.AddAlias("-v");

        // Add options to root command
        rootCommand.AddOption(inputFileOption);
        rootCommand.AddOption(outputFileOption);
        rootCommand.AddOption(solverOption);
        rootCommand.AddOption(plotFormatOption);
        rootCommand.AddOption(quietOption);
        rootCommand.AddOption(verboseOption);

        // Set the handler
        rootCommand.SetHandler((context) =>
        {
            // Parse arguments from the context
            var inputFile = context.ParseResult.GetValueForOption(inputFileOption)!;
            var outputFile = context.ParseResult.GetValueForOption(outputFileOption);
            var solver = context.ParseResult.GetValueForOption(solverOption);
            var plotFormat = context.ParseResult.GetValueForOption(plotFormatOption);
            var quiet = context.ParseResult.GetValueForOption(quietOption);
            var verbose = context.ParseResult.GetValueForOption(verboseOption);

            // Execute the program with parsed arguments
            ExecuteProgram(inputFile, outputFile, solver, plotFormat, quiet, verbose);
        });

        // Parse command line and execute
        return await new CommandLineBuilder(rootCommand)
            .UseDefaults()
            .Build()
            .InvokeAsync(args);
    }

    static void ExecuteProgram(
        FileInfo inputFile,
        FileInfo? outputFile,
        SolverType solver,
        PlotFormat plotFormat,
        bool quiet,
        bool verbose)
    {
        var exitCode = 0;
        var outputBuilder = new StringBuilder();

        // Show title and version unless quiet mode
        if (!quiet)
        {
            string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.1.0";
            AnsiConsole.Write(
                new FigletText("LibreSolvE")
                    .LeftJustified()
                    .Color(Color.Green));
            AnsiConsole.WriteLine($"Version {version}");
            AnsiConsole.WriteLine();
        }

        try
        {
            // If no output file is specified, create one based on the input file name
            if (outputFile == null && !quiet)
            {
                string outputPath = Path.Combine(
                    Path.GetDirectoryName(inputFile.FullName) ?? "",
                    Path.GetFileNameWithoutExtension(inputFile.FullName) + ".out.txt");
                outputFile = new FileInfo(outputPath);
            }

            // Validate input file exists
            if (!inputFile.Exists)
            {
                AnsiConsole.MarkupLine($"[red]Error: Input file not found: {inputFile.FullName}[/]");
                Environment.Exit(1);
            }

            WriteOutput($"Processing file: {inputFile.FullName}", quiet, outputBuilder);

            if (!quiet)
            {
                // Execute with spinner animation
                AnsiConsole.Status()
                    .Start("Processing input file...", ctx =>
                    {
                        ctx.Spinner(Spinner.Known.Dots);
                        ctx.SpinnerStyle(Style.Parse("green"));

                        exitCode = ProcessFile(inputFile, solver, plotFormat, verbose, quiet, outputBuilder);
                    });
            }
            else
            {
                // Process without spinner
                exitCode = ProcessFile(inputFile, solver, plotFormat, verbose, quiet, outputBuilder);
            }

            // Write output to file if requested
            if (outputFile != null && exitCode == 0)
            {
                File.WriteAllText(outputFile.FullName, outputBuilder.ToString());
                if (!quiet)
                {
                    AnsiConsole.MarkupLine($"[green]Results written to: {outputFile.FullName}[/]");
                }
            }

            if (exitCode == 0 && !quiet)
            {
                AnsiConsole.MarkupLine("[green]Processing completed successfully.[/]");
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            if (verbose)
            {
                AnsiConsole.WriteException(ex);
            }
            Environment.Exit(1);
        }

        Environment.Exit(exitCode);
    }

    static int ProcessFile(
        FileInfo inputFile,
        SolverType solverType,
        PlotFormat plotFormat,
        bool verbose,
        bool quiet,
        StringBuilder outputBuilder)
    {
        try
        {
            WriteOutput($"--- Reading file: {inputFile.FullName} ---", quiet, outputBuilder);
            string inputText = File.ReadAllText(inputFile.FullName);

            // 1. Parse Units *before* main parsing/execution
            WriteOutput("--- Extracting units from source ---", quiet, outputBuilder);
            // Use the static method from UnitParser
            var unitsDictionary = UnitParser.ExtractUnitsFromSource(inputText);

            if (verbose)
            {
                WriteOutput($"Found {unitsDictionary.Count} variables with units specified.", quiet, outputBuilder);
                if (unitsDictionary.Count > 0)
                {
                    foreach (var kvp in unitsDictionary)
                    {
                        WriteOutput($"  {kvp.Key}: [{kvp.Value}]", quiet, outputBuilder);
                    }
                }
            }

            // 2. ANTLR Parsing
            AntlrInputStream inputStream = new AntlrInputStream(inputText);
            EesLexer lexer = new EesLexer(inputStream);
            CommonTokenStream commonTokenStream = new CommonTokenStream(lexer);
            EesParser parser = new EesParser(commonTokenStream);

            // Setup error handling
            parser.RemoveErrorListeners();
            lexer.RemoveErrorListeners();
            var errorListener = new BetterErrorListener();
            parser.AddErrorListener(errorListener);
            lexer.AddErrorListener(errorListener);

            WriteOutput("--- Parsing file content ---", quiet, outputBuilder);
            EesParser.EesFileContext parseTreeContext = parser.eesFile();
            WriteOutput("--- Parsing successful ---", quiet, outputBuilder);

            // 3. Build Abstract Syntax Tree (AST)
            WriteOutput("--- Building Abstract Syntax Tree (AST) ---", quiet, outputBuilder);
            var astBuilder = new AstBuilderVisitor();
            AstNode rootAstNode = astBuilder.VisitEesFile(parseTreeContext);

            if (rootAstNode is not EesFileNode fileNode)
            {
                WriteOutput("--- AST Building FAILED: Root node is not the expected EesFileNode type ---", quiet, outputBuilder, forceOutput: true);
                return 1;
            }

            WriteOutput($"--- AST Built Successfully ({fileNode.Statements.Count} statements found) ---", quiet, outputBuilder);

            // 4. Initialize Core Components
            WriteOutput("--- Initializing Execution Environment ---", quiet, outputBuilder);
            var variableStore = new VariableStore();
            var functionRegistry = new FunctionRegistry(); // Includes built-ins
            var solverSettings = new SolverSettings { SolverType = solverType };

            // Apply parsed units to the store
            UnitParser.ApplyUnitsToVariableStore(variableStore, unitsDictionary);

            // 5. Create a statement executor and execute the statements
            var executor = new StatementExecutor(variableStore, functionRegistry, solverSettings);

            // Hook up plot creation event if we need to handle plots
            List<PlotData> generatedPlots = new List<PlotData>();
            executor.PlotCreated += (sender, plotData) =>
            {
                generatedPlots.Add(plotData);
                // Save the plot based on the chosen format
                string filename = Path.Combine(
                    Path.GetDirectoryName(inputFile.FullName) ?? "",
                    $"plot_{DateTime.Now:yyyyMMdd_HHmmssfff}{GetPlotExtension(plotFormat)}");

                var renderer = new SvgPlotRenderer();
                renderer.SaveToFile(plotData, filename);

                WriteOutput($"Plot saved to: {filename}", quiet, outputBuilder);
            };

            // Execute statements
            executor.Execute(fileNode);

            // Print variable state after assignments
            WriteOutput("\n--- Variable Store State After Assignments ---", quiet, outputBuilder);
            var variableStoreOutput = CaptureConsoleOutput(() => variableStore.PrintVariables());
            WriteOutput(variableStoreOutput, quiet, outputBuilder);

            // 6. Solve Equations
            WriteOutput("\n--- Equation Solving Phase ---", quiet, outputBuilder);
            bool solveSuccess = executor.SolveRemainingAlgebraicEquations();

            if (solveSuccess)
            {
                WriteOutput("\n--- Solver Phase Completed Successfully ---", quiet, outputBuilder);
                WriteOutput("\n--- Final Variable Store State ---", quiet, outputBuilder);
                variableStoreOutput = CaptureConsoleOutput(() => variableStore.PrintVariables());
                WriteOutput(variableStoreOutput, quiet, outputBuilder);

                // Handle plots
                if (generatedPlots.Count > 0)
                {
                    WriteOutput($"\n--- Generated {generatedPlots.Count} plots ---", quiet, outputBuilder);
                    for (int i = 0; i < generatedPlots.Count; i++)
                    {
                        WriteOutput($"Plot {i + 1}: {generatedPlots[i].Settings.Title}", quiet, outputBuilder);
                    }
                }

                return 0; // Success
            }
            else
            {
                WriteOutput("\n--- Solver FAILED ---", quiet, outputBuilder, forceOutput: true);
                WriteOutput("\n--- Variable Store State After Failed Solve Attempt ---", quiet, outputBuilder);
                variableStoreOutput = CaptureConsoleOutput(() => variableStore.PrintVariables());
                WriteOutput(variableStoreOutput, quiet, outputBuilder);
                return 1; // Failure
            }
        }
        catch (ParsingException pEx)
        {
            WriteOutput($"\n--- PARSING FAILED ---", quiet, outputBuilder, forceOutput: true);
            WriteOutput(pEx.Message, quiet, outputBuilder, forceOutput: true);
            return 1;
        }
        catch (IOException ioEx)
        {
            WriteOutput($"\n--- FILE ERROR ---", quiet, outputBuilder, forceOutput: true);
            WriteOutput($"File access error: {ioEx.Message}", quiet, outputBuilder, forceOutput: true);
            return 1;
        }
        catch (Exception ex)
        {
            WriteOutput($"\n--- UNEXPECTED ERROR ---", quiet, outputBuilder, forceOutput: true);
            WriteOutput($"An unexpected error occurred: {ex.GetType().Name} - {ex.Message}", quiet, outputBuilder, forceOutput: true);
            if (verbose)
            {
                WriteOutput(ex.StackTrace ?? "", quiet, outputBuilder);
            }
            return 1;
        }
    }

    static string CaptureConsoleOutput(Action action)
    {
        // Capture console output from an action
        var originalOut = Console.Out;
        using var stringWriter = new StringWriter();
        Console.SetOut(stringWriter);

        action();

        Console.SetOut(originalOut);
        return stringWriter.ToString();
    }

    static void WriteOutput(string message, bool quiet, StringBuilder builder, bool forceOutput = false)
    {
        builder.AppendLine(message);
        if (!quiet || forceOutput)
        {
            Console.WriteLine(message);
        }
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

    private static void SavePlot(PlotData plotData, string outputDirectory, PlotFormat format)
    {
        string filename = Path.Combine(
            outputDirectory,
            $"plot_{DateTime.Now:yyyyMMdd_HHmmssfff}{GetPlotExtension(format)}");

        // Use PlotExporter to save the plot in the desired format
        PlotExporter.ExportToFormat(plotData, filename, format);
    }
}

/// <summary>
/// Enum for plot output formats
/// </summary>
public enum PlotFormat
{
    SVG,
    PNG,
    PDF
}
