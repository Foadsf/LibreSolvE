// LibreSolvE.CLI/Program.cs
using System;
using System.IO; // Required for file operations
using Antlr4.Runtime; // ANTLR Runtime
using LibreSolvE.Core.Parsing; // Your ANTLR generated parser/lexer namespace
using LibreSolvE.Core.Ast;     // Your Abstract Syntax Tree node classes
using LibreSolvE.Core.Evaluation; // Your Evaluation classes (VariableStore, Executor)

// Check if input file argument is provided
if (args.Length == 0)
{
    Console.Error.WriteLine("Error: Please provide an input file path as an argument.");
    Console.WriteLine("Usage: dotnet run --project LibreSolvE.CLI\\LibreSolvE.CLI.csproj -- <input_file>");
    return 1; // Indicate error
}

string inputFilePath = args[0];

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

    // Remove default console error listener and add a standard one
    // This prevents ANTLR errors from polluting stdout but still shows them on stderr
    parser.RemoveErrorListeners();
    lexer.RemoveErrorListeners(); // Also remove from lexer
    parser.AddErrorListener(ConsoleErrorListener<IToken>.Instance);
    lexer.AddErrorListener(ConsoleErrorListener<int>.Instance); // Lexer uses <int>

    Console.WriteLine("--- Attempting to parse file content ---");
    EesParser.EesFileContext parseTreeContext = parser.eesFile(); // Get the parse tree

    // Check for syntax errors reported by ANTLR
    if (parser.NumberOfSyntaxErrors > 0)
    {
        Console.Error.WriteLine($"--- Parsing FAILED with {parser.NumberOfSyntaxErrors} syntax errors ---");
        // Error details should have been printed to stderr by ConsoleErrorListener
        return 1; // Indicate parsing failure
    }
    Console.WriteLine("--- Parsing SUCCESSFUL (Basic syntax check passed) ---");

    // 2. Build Abstract Syntax Tree (AST)
    Console.WriteLine("--- Building Abstract Syntax Tree (AST)... ---");
    var astBuilder = new AstBuilderVisitor();
    AstNode rootAstNode = astBuilder.VisitEesFile(parseTreeContext); // Visit the parse tree root

    if (rootAstNode is not EesFileNode fileNode) // Use pattern matching
    {
        Console.Error.WriteLine("--- AST Building FAILED: Root node is not the expected EesFileNode type ---");
        return 1; // Indicate AST building failure
    }
    Console.WriteLine($"--- AST Built Successfully ({fileNode.Statements.Count} statements found) ---");

    // Optional: Print the constructed AST structure for debugging
    // Console.WriteLine("--- Constructed AST ---");
    // Console.WriteLine(rootAstNode.ToString());
    // Console.WriteLine("-----------------------");


    // 3. Execute Statements (Assignments) and Collect Equations
    Console.WriteLine("--- Initializing Execution ---");
    var variableStore = new VariableStore();
    var executor = new StatementExecutor(variableStore);

    executor.Execute(fileNode); // Execute assignments, collect equations

    // Print the state of variables after initial assignments
    variableStore.PrintVariables();


    // 4. TODO: Solve Equations (Placeholder for next step)
    Console.WriteLine("--- Equation Solving (Placeholder) ---");
    if (executor.Equations.Count > 0)
    {
        Console.WriteLine($"Found {executor.Equations.Count} equations to solve:");
        foreach (var eq in executor.Equations)
        {
            Console.WriteLine($"- {eq}");
        }
        // var solver = new EquationSolver(variableStore, executor.Equations);
        // bool success = solver.Solve();
        // if (success) {
        //    Console.WriteLine("--- Solver Converged ---");
        //    variableStore.PrintVariables(); // Print final solved values
        // } else {
        //    Console.Error.WriteLine("--- Solver FAILED to converge ---");
        //    return 1; // Indicate solver failure
        // }
    }
    else
    {
        Console.WriteLine("No equations found to solve.");
    }
    Console.WriteLine("------------------------------------");


    return 0; // Indicate overall success for this phase
}
catch (IOException ioEx)
{
    Console.Error.WriteLine($"File access error: {ioEx.Message}");
    return 1; // Indicate file error
}
catch (Exception ex)
{
    // Catch any other exceptions during parsing or execution
    Console.Error.WriteLine($"An unexpected error occurred: {ex.GetType().Name} - {ex.Message}");
    Console.Error.WriteLine("Stack Trace:");
    Console.Error.WriteLine(ex.StackTrace);
    return 1; // Indicate general error
}
