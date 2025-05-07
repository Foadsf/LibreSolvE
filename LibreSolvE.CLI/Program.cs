// LibreSolvE.CLI/Program.cs
using System;
using System.IO; // Required for file operations
using System.Text;
using Antlr4.Runtime; // ANTLR Runtime
using LibreSolvE.Core.Parsing; // Your ANTLR generated parser/lexer namespace
using LibreSolvE.Core.Ast;     // Your Abstract Syntax Tree node classes
using LibreSolvE.Core.Evaluation; // Your Evaluation classes (VariableStore, Executor, Solver, FunctionRegistry, UnitParser)
// Note: We don't typically need UnitsNet directly here unless handling specific unit args

// --- Main Program Logic ---

int exitCode = 0; // Default to success
string? inputFilePath = null; // Use nullable string
var solverSettings = new SolverSettings(); // Initialize with defaults

// --- Argument Parsing ---
// Simple manual parsing for now
var argsList = args.ToList();
for (int i = 0; i < argsList.Count; i++)
{
    string arg = argsList[i];
    if (arg.StartsWith("-")) // It's an option flag
    {
        string lowerArg = arg.ToLowerInvariant();
        if (lowerArg == "--nelder-mead")
        {
            solverSettings.SolverType = SolverType.NelderMead;
            Console.WriteLine("Solver specified: Nelder-Mead");
        }
        else if (lowerArg == "--levenberg-marquardt" || lowerArg == "--lm")
        {
            solverSettings.SolverType = SolverType.LevenbergMarquardt;
            Console.WriteLine("Solver specified: Levenberg-Marquardt");
        }
        // Add more option parsing here later (e.g., tolerance, iterations)
        else
        {
            Console.Error.WriteLine($"Warning: Unknown option '{arg}' ignored.");
        }
    }
    else if (inputFilePath == null) // First non-flag argument is the input file
    {
        inputFilePath = arg;
    }
    else // Subsequent non-flag arguments are unexpected
    {
        Console.Error.WriteLine($"Warning: Unexpected argument '{arg}' ignored.");
    }
}


// Check if input file argument was provided
if (string.IsNullOrEmpty(inputFilePath))
{
    Console.Error.WriteLine("Error: Input file path argument is required.");
    Console.WriteLine(@"Usage: dotnet run --project LibreSolvE.CLI\LibreSolvE.CLI.csproj -- <input_file.lse> [--solver-option]");
    Console.WriteLine(@"Solver options: --nelder-mead (default), --levenberg-marquardt");
    return 1; // Indicate error
}

Console.WriteLine($"Processing file: {inputFilePath}"); // Log which file is being processed

// Check if the input file exists
if (!File.Exists(inputFilePath))
{
    Console.Error.WriteLine($"Error: Input file not found: '{inputFilePath}'");
    return 1; // Indicate error
}

try
{
    Console.WriteLine($"--- Reading file: {inputFilePath} ---");
    string inputText = File.ReadAllText(inputFilePath);

    // 1. Parse Units *before* main parsing/execution
    Console.WriteLine("--- Extracting units from source ---");
    // Use the static method from UnitParser
    var unitsDictionary = UnitParser.ExtractUnitsFromSource(inputText);
    Console.WriteLine($"Found {unitsDictionary.Count} variables with units specified.");
    if (unitsDictionary.Count > 0)
    {
        foreach (var kvp in unitsDictionary) Console.WriteLine($"  {kvp.Key}: [{kvp.Value}]");
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
    // var errorListener = new ThrowingErrorListener();
    parser.AddErrorListener(errorListener);
    lexer.AddErrorListener(errorListener); // Add to lexer too

    Console.WriteLine("--- Attempting to parse file content ---");
    EesParser.EesFileContext parseTreeContext = parser.eesFile();
    Console.WriteLine("--- Parsing SUCCESSFUL (Basic syntax check passed) ---");

    // 3. Build Abstract Syntax Tree (AST)
    Console.WriteLine("--- Building Abstract Syntax Tree (AST)... ---");
    var astBuilder = new AstBuilderVisitor();
    AstNode rootAstNode = astBuilder.VisitEesFile(parseTreeContext);

    if (rootAstNode is not EesFileNode fileNode)
    {
        Console.Error.WriteLine("--- AST Building FAILED: Root node is not the expected EesFileNode type ---");
        return 1;
    }
    Console.WriteLine($"--- AST Built Successfully ({fileNode.Statements.Count} statements found) ---");
    // Console.WriteLine("--- Constructed AST ---\n" + rootAstNode.ToString() + "\n-----------------------");


    // 4. Initialize Core Components
    Console.WriteLine("--- Initializing Execution Environment ---");
    var variableStore = new VariableStore();
    var functionRegistry = new FunctionRegistry(); // Includes built-ins

    // Apply parsed units to the store
    UnitParser.ApplyUnitsToVariableStore(variableStore, unitsDictionary);

    // 5. Execute Statements (Assignments) and Collect Equations
    // Pass registry and settings to executor
    var executor = new StatementExecutor(variableStore, functionRegistry, solverSettings);
    executor.Execute(fileNode);

    Console.WriteLine("\n--- Variable Store State After Assignments ---");
    variableStore.PrintVariables();


    // 6. Solve Equations
    Console.WriteLine("\n--- Equation Solving Phase ---");
    // The executor now internally creates and uses the EquationSolver
    bool solveSuccess = executor.SolveRemainingAlgebraicEquations();

    if (solveSuccess)
    {
        Console.WriteLine("\n--- Solver Phase Completed Successfully ---");
        Console.WriteLine("\n--- Final Variable Store State ---");
        variableStore.PrintVariables(); // Print final solved values
        exitCode = 0; // Success
    }
    else
    {
        Console.Error.WriteLine("\n--- Solver FAILED ---");
        Console.WriteLine("\n--- Variable Store State After Failed Solve Attempt ---");
        variableStore.PrintVariables(); // Print state after failure
        exitCode = 1; // Indicate solver failure
    }
    Console.WriteLine("------------------------------------");

}
catch (ParsingException pEx)
{
    Console.Error.WriteLine($"\n--- PARSING FAILED ---");
    Console.Error.WriteLine(pEx.Message);
    // Optionally include inner exception details if helpful
    // if(pEx.InnerException != null) Console.Error.WriteLine($"   Inner: {pEx.InnerException.Message}");
    exitCode = 1;
}
catch (IOException ioEx)
{
    Console.Error.WriteLine($"\n--- FILE ERROR ---");
    Console.Error.WriteLine($"File access error: {ioEx.Message}");
    exitCode = 1;
}
catch (NotImplementedException niEx)
{
    Console.Error.WriteLine($"\n--- EXECUTION ERROR ---");
    Console.Error.WriteLine($"Feature not implemented: {niEx.Message}");
    // Console.Error.WriteLine(niEx.StackTrace); // Often too verbose, enable if needed
    exitCode = 1;
}
catch (KeyNotFoundException knfEx) // Catch errors from FunctionRegistry or VariableStore
{
    Console.Error.WriteLine($"\n--- EXECUTION ERROR ---");
    Console.Error.WriteLine($"Identifier not found: {knfEx.Message}");
    exitCode = 1;
}
catch (ArgumentException argEx) // Catch errors from function calls or invalid operations
{
    Console.Error.WriteLine($"\n--- EXECUTION ERROR ---");
    Console.Error.WriteLine($"Invalid argument or operation: {argEx.Message}");
    exitCode = 1;
}
catch (DivideByZeroException dbzEx)
{
    Console.Error.WriteLine($"\n--- EXECUTION ERROR ---");
    Console.Error.WriteLine($"Mathematical error: {dbzEx.Message}");
    exitCode = 1;
}
catch (Exception ex) // Catch-all for other unexpected errors
{
    Console.Error.WriteLine($"\n--- UNEXPECTED ERROR ---");
    Console.Error.WriteLine($"An unexpected error occurred: {ex.GetType().Name} - {ex.Message}");
    Console.Error.WriteLine("Stack Trace:");
    Console.Error.WriteLine(ex.StackTrace);
    exitCode = 1;
}

Console.WriteLine($"\nExiting with code {exitCode}");
return exitCode;


// --- Helper Class for ANTLR Error Handling ---
// --- Helper Class for ANTLR Error Handling ---
// Implement both interfaces
public class ThrowingErrorListener : BaseErrorListener, IAntlrErrorListener<int>
{
    // --- IAntlrErrorListener<IToken> implementation (for Parser) ---
    public override void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line, int charPositionInLine, string msg, RecognitionException e)
    {
        string errorMessage = $"Syntax error at line {line}:{charPositionInLine} near '{offendingSymbol?.Text ?? "<EOF>"}': {msg}";
        // Console.Error.WriteLine(errorMessage); // Optional: Log before throwing
        throw new ParsingException(errorMessage, e);
    }

    // --- IAntlrErrorListener<int> implementation (for Lexer) ---
    // Lexer errors often don't have an offending token, just a character position
    public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol, int line, int charPositionInLine, string msg, RecognitionException e)
    {
        // We don't have an 'offendingSymbol.Text' here, so adjust the message
        string errorMessage = $"Lexer error at line {line}:{charPositionInLine}: {msg}";
        // Console.Error.WriteLine(errorMessage); // Optional: Log before throwing
        throw new ParsingException(errorMessage, e);
    }
}

