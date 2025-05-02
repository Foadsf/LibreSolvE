// LibreSolvE.CLI/Program.cs
using System;
using System.IO; // Required for file operations
using Antlr4.Runtime; // ANTLR Runtime
using LibreSolvE.Core.Parsing; // Your ANTLR generated parser/lexer namespace
using LibreSolvE.Core.Ast;     // Your Abstract Syntax Tree node classes
using LibreSolvE.Core.Evaluation; // Your Evaluation classes (VariableStore, Executor, Solver)

// --- Main Program Logic ---

int exitCode = 0; // Default to success

// Check if input file argument is provided
if (args.Length == 0)
{
    Console.Error.WriteLine("Error: Please provide an input file path as an argument.");
    Console.WriteLine("Usage: dotnet run --project LibreSolvE.CLI\\LibreSolvE.CLI.csproj -- <input_file>");
    return 1; // Indicate error
}

string inputFilePath = args[0];
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

    // 1. ANTLR Parsing
    AntlrInputStream inputStream = new AntlrInputStream(inputText);
    EesLexer lexer = new EesLexer(inputStream);
    CommonTokenStream commonTokenStream = new CommonTokenStream(lexer);
    EesParser parser = new EesParser(commonTokenStream);

    // Remove default error listeners
    parser.RemoveErrorListeners();
    lexer.RemoveErrorListeners();

    // Add custom error listener to parser only (to simplify)
    parser.AddErrorListener(new CustomErrorListener());

    Console.WriteLine("--- Attempting to parse file content ---");
    EesParser.EesFileContext parseTreeContext = parser.eesFile(); // Get the parse tree
    Console.WriteLine("--- Parsing SUCCESSFUL (Basic syntax check passed) ---");

    // 2. Build Abstract Syntax Tree (AST)
    Console.WriteLine("--- Building Abstract Syntax Tree (AST)... ---");
    var astBuilder = new AstBuilderVisitor();
    AstNode rootAstNode = astBuilder.VisitEesFile(parseTreeContext); // Visit the parse tree root

    if (rootAstNode is not EesFileNode fileNode)
    {
        Console.Error.WriteLine("--- AST Building FAILED: Root node is not the expected EesFileNode type ---");
        return 1; // Indicate AST building failure
    }
    Console.WriteLine($"--- AST Built Successfully ({fileNode.Statements.Count} statements found) ---");
    Console.WriteLine("--- Constructed AST ---\n" + rootAstNode.ToString() + "\n-----------------------"); // Optional debug

    // 2.5 Extract units from the input file
    Console.WriteLine("--- Extracting units from source ---");
    var unitsDictionary = UnitParser.ExtractUnitsFromSourceText(inputText);
    Console.WriteLine($"Found {unitsDictionary.Count} variables with units");

    // 3. Execute Statements (Assignments) and Collect Equations
    Console.WriteLine("--- Initializing Execution ---");
    var variableStore = new VariableStore();

    // Apply extracted units to the variable store
    UnitParser.ApplyUnitsToVariableStore(variableStore, unitsDictionary);

    var functionRegistry = new FunctionRegistry(); // Initialize function registry
    var solverSettings = new SolverSettings(); // Default solver settings

    // Choose solver type (can be made configurable via command line args in the future)
    if (args.Length > 1 && args[1].Equals("--levenberg-marquardt", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("Using Levenberg-Marquardt solver");
        solverSettings.SolverType = SolverType.LevenbergMarquardt;
    }
    else
    {
        Console.WriteLine("Using Nelder-Mead solver (default)");
        solverSettings.SolverType = SolverType.NelderMead;
    }

    var executor = new StatementExecutor(variableStore, functionRegistry);

    executor.Execute(fileNode); // Pass 1 (Assignments), Pass 2 (Collect Equations)

    Console.WriteLine("\n--- Variable Store State After Assignments ---");
    variableStore.PrintVariables();

    // 4. Solve Equations
    Console.WriteLine("\n--- Equation Solving Phase ---");
    bool solveSuccess = executor.SolveEquations(); // Call the solver

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
catch (ParsingException pEx) // Catch specific parsing errors from our error listener
{
    Console.Error.WriteLine($"\n--- PARSING FAILED ---");
    Console.Error.WriteLine(pEx.Message);
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
    Console.Error.WriteLine(niEx.StackTrace);
    exitCode = 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"\n--- UNEXPECTED ERROR ---");
    Console.Error.WriteLine($"An unexpected error occurred: {ex.GetType().Name} - {ex.Message}");
    Console.Error.WriteLine("Stack Trace:");
    Console.Error.WriteLine(ex.StackTrace);
    exitCode = 1;
}

Console.WriteLine($"\nExiting with code {exitCode}");
return exitCode;


// --- Custom Error Listener ---
// Much simpler approach - just one listener attached to the parser

public class CustomErrorListener : IAntlrErrorListener<IToken>
{
    public void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line, int charPositionInLine, string msg, RecognitionException e)
    {
        throw new ParsingException($"Syntax error at line {line}:{charPositionInLine} - {msg}", e);
    }
}

// Custom exception for parsing errors
public class ParsingException : Exception
{
    public ParsingException(string message, Exception innerException) : base(message, innerException) { }
    public ParsingException(string message) : base(message) { }
}
