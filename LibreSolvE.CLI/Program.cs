// LibreSolvE.CLI/Program.cs - Complete rewrite with more robust console handling
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

        // Capture all console output for the log file and redirect debug output
        // to prevent it from showing in normal mode
        TextWriter originalConsoleOut = Console.Out;
        StringWriter logWriter = new StringWriter();
        StringWriter debugWriter = new StringWriter();

        try
        {
            // Show title and version unless quiet mode
            if (!quiet)
            {
                string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.1.0";

                // Simple clean header
                AnsiConsole.MarkupLine($"[green]LibreSolvE[/] v{version}");
                AnsiConsole.WriteLine();
            }

            // Create default output file if none specified
            if (outputFile == null)
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

            // Show filename
            if (!quiet)
            {
                AnsiConsole.MarkupLine($"Processing: [cyan]{Path.GetFileName(inputFile.FullName)}[/]");
            }

            // Record full path in output log
            logWriter.WriteLine($"Processing file: {inputFile.FullName}");

            if (!quiet)
            {
                // Execute with spinner animation
                AnsiConsole.Status()
                    .Start("Solving...", ctx =>
                    {
                        ctx.Spinner(Spinner.Known.Dots);
                        ctx.SpinnerStyle(Style.Parse("green"));

                        // Redirect console output when not in verbose mode
                        if (!verbose)
                        {
                            Console.SetOut(debugWriter);
                        }

                        exitCode = ProcessFile(inputFile, solver, plotFormat, verbose, quiet, logWriter);

                        // Restore console output
                        Console.SetOut(originalConsoleOut);
                    });
            }
            else
            {
                // Process without spinner
                // Redirect console output when not in verbose mode
                if (!verbose)
                {
                    Console.SetOut(debugWriter);
                }

                exitCode = ProcessFile(inputFile, solver, plotFormat, verbose, quiet, logWriter);

                // Restore console output
                Console.SetOut(originalConsoleOut);
            }

            // Write output to file if requested
            if (outputFile != null && exitCode == 0)
            {
                File.WriteAllText(outputFile.FullName, logWriter.ToString());
                if (!quiet)
                {
                    // Just append this to the results instead of showing separately
                    Console.WriteLine($"Results saved to: {outputFile.FullName}");
                }
            }

            if (exitCode == 0 && !quiet)
            {
                Console.WriteLine("[Success] Processing completed successfully.");
            }
        }
        catch (Exception ex)
        {
            // Restore console output in case of exception
            Console.SetOut(originalConsoleOut);

            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            if (verbose)
            {
                AnsiConsole.WriteException(ex);
            }
            Environment.Exit(1);
        }
        finally
        {
            // Ensure console is restored
            Console.SetOut(originalConsoleOut);
        }

        Environment.Exit(exitCode);
    }

    static int ProcessFile(
        FileInfo inputFile,
        SolverType solverType,
        PlotFormat plotFormat,
        bool verbose,
        bool quiet,
        TextWriter logWriter)
    {
        try
        {
            // Full output for file log
            logWriter.WriteLine($"--- Reading file: {inputFile.FullName} ---");
            string inputText = File.ReadAllText(inputFile.FullName);

            // 1. Parse Units
            logWriter.WriteLine("--- Extracting units from source ---");
            var unitsDictionary = UnitParser.ExtractUnitsFromSource(inputText);

            if (unitsDictionary.Count > 0)
            {
                logWriter.WriteLine($"Found {unitsDictionary.Count} variables with units specified.");
                foreach (var kvp in unitsDictionary)
                {
                    logWriter.WriteLine($"  {kvp.Key}: [{kvp.Value}]");
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

            logWriter.WriteLine("--- Parsing file content ---");
            EesParser.EesFileContext parseTreeContext = parser.eesFile();
            logWriter.WriteLine("--- Parsing successful ---");

            // 3. Build Abstract Syntax Tree (AST)
            logWriter.WriteLine("--- Building Abstract Syntax Tree (AST) ---");
            var astBuilder = new AstBuilderVisitor();
            AstNode rootAstNode = astBuilder.VisitEesFile(parseTreeContext);

            if (rootAstNode is not EesFileNode fileNode)
            {
                logWriter.WriteLine("--- AST Building FAILED: Root node is not the expected EesFileNode type ---");
                if (!quiet)
                {
                    Console.WriteLine("AST Building FAILED: Root node is not the expected EesFileNode type");
                }
                return 1;
            }

            logWriter.WriteLine($"--- AST Built Successfully ({fileNode.Statements.Count} statements found) ---");

            // 4. Initialize Core Components
            logWriter.WriteLine("--- Initializing Execution Environment ---");
            var variableStore = new VariableStore();
            var functionRegistry = new FunctionRegistry(); // Includes built-ins
            var solverSettings = new SolverSettings { SolverType = solverType };

            // Apply parsed units to the store
            UnitParser.ApplyUnitsToVariableStore(variableStore, unitsDictionary);

            // 5. Create a statement executor and execute the statements
            var executor = new StatementExecutor(variableStore, functionRegistry, solverSettings);

            // Hook up plot creation event
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

                logWriter.WriteLine($"Plot saved to: {filename}");
            };

            // Execute statements
            executor.Execute(fileNode);

            // Capture variable state after assignments for logs
            logWriter.WriteLine("\n--- Variable Store State After Assignments ---");
            StringWriter varStateWriter = new StringWriter();
            TextWriter originalOut = Console.Out;
            Console.SetOut(varStateWriter);
            variableStore.PrintVariables();
            Console.SetOut(originalOut);
            logWriter.WriteLine(varStateWriter.ToString());

            // 6. Solve Equations
            logWriter.WriteLine("\n--- Equation Solving Phase ---");
            bool solveSuccess = executor.SolveRemainingAlgebraicEquations();

            if (solveSuccess)
            {
                logWriter.WriteLine("\n--- Solver Phase Completed Successfully ---");

                // Final variable state
                logWriter.WriteLine("\n--- Final Variable Store State ---");
                StringWriter finalStateWriter = new StringWriter();
                Console.SetOut(finalStateWriter);
                variableStore.PrintVariables();
                Console.SetOut(originalOut);
                string variableStoreOutput = finalStateWriter.ToString();
                logWriter.WriteLine(variableStoreOutput);

                // Always show results in console
                TextWriter consoleOut = originalOut;

                // For console output, make it nicer in normal mode
                if (!quiet)
                {
                    // Restore console output for displaying results
                    Console.SetOut(consoleOut);

                    // Show results header
                    Console.WriteLine("\n" + new string('=', 25) + " RESULTS " + new string('=', 25));

                    // Display the variables
                    DisplayPlainVariableTable(variableStoreOutput);

                    // Show plots information
                    if (generatedPlots.Count > 0)
                    {
                        Console.WriteLine($"\nGenerated {generatedPlots.Count} plots:");

                        for (int i = 0; i < generatedPlots.Count; i++)
                        {
                            Console.WriteLine($"  Plot {i + 1}: {generatedPlots[i].Settings.Title}");
                            logWriter.WriteLine($"Plot {i + 1}: {generatedPlots[i].Settings.Title}");
                        }
                    }
                }

                return 0; // Success
            }
            else
            {
                logWriter.WriteLine("\n--- Solver FAILED ---");
                if (!quiet)
                {
                    Console.WriteLine("\nSolver FAILED");
                }

                logWriter.WriteLine("\n--- Variable Store State After Failed Solve Attempt ---");
                StringWriter failedStateWriter = new StringWriter();
                Console.SetOut(failedStateWriter);
                variableStore.PrintVariables();
                Console.SetOut(originalOut);
                logWriter.WriteLine(failedStateWriter.ToString());

                return 1; // Failure
            }
        }
        catch (ParsingException pEx)
        {
            logWriter.WriteLine("\n--- PARSING FAILED ---");
            logWriter.WriteLine(pEx.Message);

            if (!quiet)
            {
                Console.WriteLine("\nPARSING FAILED");
                Console.WriteLine(pEx.Message);
            }

            return 1;
        }
        catch (IOException ioEx)
        {
            logWriter.WriteLine("\n--- FILE ERROR ---");
            logWriter.WriteLine($"File access error: {ioEx.Message}");

            if (!quiet)
            {
                Console.WriteLine("\nFILE ERROR");
                Console.WriteLine($"File access error: {ioEx.Message}");
            }

            return 1;
        }
        catch (Exception ex)
        {
            logWriter.WriteLine("\n--- UNEXPECTED ERROR ---");
            logWriter.WriteLine($"An unexpected error occurred: {ex.GetType().Name} - {ex.Message}");

            if (verbose)
            {
                logWriter.WriteLine(ex.StackTrace ?? "");
            }

            if (!quiet)
            {
                Console.WriteLine("\nUNEXPECTED ERROR");
                Console.WriteLine($"An unexpected error occurred: {ex.GetType().Name} - {ex.Message}");

                if (verbose)
                {
                    Console.WriteLine(ex.StackTrace ?? "");
                }
            }

            return 1;
        }
    }

    // Simplified display method that doesn't rely on Spectre.Console table formatting
    static void DisplayPlainVariableTable(string variableStoreOutput)
    {
        // Parse the variable store output
        var variables = new List<(string Name, string Value, string Units)>();

        var lines = variableStoreOutput.Split('\n');
        foreach (var line in lines)
        {
            if (line.StartsWith("  ") && !line.StartsWith("---") &&
                !line.Contains("Variable Store") && !line.Contains("----") &&
                !string.IsNullOrWhiteSpace(line) && line.Contains("="))
            {
                // Parse the line
                var parts = line.Trim().Split(new[] { '=' }, 2);
                if (parts.Length == 2)
                {
                    string varName = parts[0].Trim();
                    string valueWithUnits = parts[1].Trim();

                    // Extract the value (everything before any brackets)
                    string value = valueWithUnits;
                    string units = "";

                    // Look for units in brackets
                    int bracketStart = valueWithUnits.IndexOf('[');
                    if (bracketStart >= 0)
                    {
                        int bracketEnd = valueWithUnits.IndexOf(']', bracketStart);
                        if (bracketEnd > bracketStart)
                        {
                            units = valueWithUnits.Substring(bracketStart, bracketEnd - bracketStart + 1);
                            value = valueWithUnits.Substring(0, bracketStart).Trim();
                        }
                    }

                    // Remove the (explicit) part
                    int parenPos = value.IndexOf('(');
                    if (parenPos > 0)
                    {
                        value = value.Substring(0, parenPos).Trim();
                    }

                    variables.Add((varName, value, units));
                }
            }
        }

        // If no variables found
        if (variables.Count == 0)
        {
            Console.WriteLine("No variables found in results.");
            return;
        }

        // Find the max width for each column
        int nameWidth = variables.Max(v => v.Name.Length);
        int valueWidth = variables.Max(v => v.Value.Length);
        int unitsWidth = variables.Max(v => v.Units.Length);

        // Ensure minimum widths
        nameWidth = Math.Max(nameWidth, "Variable".Length);
        valueWidth = Math.Max(valueWidth, "Value".Length);
        unitsWidth = Math.Max(unitsWidth, "Units".Length);

        // Add some padding
        nameWidth += 2;
        valueWidth += 2;
        unitsWidth += 2;

        // Calculate total width
        int totalWidth = nameWidth + valueWidth + unitsWidth + 4; // 4 for the table borders

        // Draw the header
        Console.WriteLine("+" + new string('-', nameWidth) + "+" + new string('-', valueWidth) + "+" + new string('-', unitsWidth) + "+");
        Console.WriteLine(
            "| " + "Variable".PadRight(nameWidth - 1) +
            "| " + "Value".PadRight(valueWidth - 1) +
            "| " + "Units".PadRight(unitsWidth - 1) + "|");
        Console.WriteLine("+" + new string('=', nameWidth) + "+" + new string('=', valueWidth) + "+" + new string('=', unitsWidth) + "+");

        // Draw the rows
        foreach (var (name, value, units) in variables)
        {
            Console.WriteLine(
                "| " + name.PadRight(nameWidth - 1) +
                "| " + value.PadRight(valueWidth - 1) +
                "| " + units.PadRight(unitsWidth - 1) + "|");
        }

        // Draw the bottom
        Console.WriteLine("+" + new string('-', nameWidth) + "+" + new string('-', valueWidth) + "+" + new string('-', unitsWidth) + "+");
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
