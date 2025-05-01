using System;
using System.IO; // Required for file operations
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using LibreSolvE.Core.Ast; // Add this line to reference AST types
using LibreSolvE.Core.Parsing;

// Minimal implementation for Phase 0
if (args.Length == 0)
{
    Console.WriteLine("Error: Please provide an input file path as an argument.");
    Console.WriteLine("Usage: dotnet run --project LibreSolvE.CLI\\LibreSolvE.CLI.csproj -- <input_file>");
    return 1; // Indicate error
}

string inputFilePath = args[0];

if (!File.Exists(inputFilePath))
{
    Console.WriteLine($"Error: Input file not found: '{inputFilePath}'");
    return 1; // Indicate error
}

try
{
    Console.WriteLine($"--- Reading file: {inputFilePath} ---");
    string input = File.ReadAllText(inputFilePath);
    AntlrInputStream inputStream = new AntlrInputStream(input);
    EesLexer lexer = new EesLexer(inputStream);
    CommonTokenStream commonTokenStream = new CommonTokenStream(lexer);
    EesParser parser = new EesParser(commonTokenStream);

    // Remove default error listener for cleaner output IF NEEDED, otherwise keep for debugging
    parser.RemoveErrorListeners();
    parser.AddErrorListener(new ConsoleErrorListener<IToken>()); // Standard listener

    Console.WriteLine("--- Attempting to parse file content ---");
    EesParser.EesFileContext context = parser.eesFile(); // Parse

    if (parser.NumberOfSyntaxErrors > 0)
    {
        Console.WriteLine($"--- Parsing FAILED with {parser.NumberOfSyntaxErrors} syntax errors ---");
        return 1;
    }
    Console.WriteLine("--- Parsing SUCCESSFUL (Basic syntax check passed) ---");

    // *** NEW VISITOR STEP ***
    Console.WriteLine("--- Building Abstract Syntax Tree (AST)... ---");
    var astBuilder = new AstBuilderVisitor();
    AstNode rootNode = astBuilder.VisitEesFile(context); // Visit the parse tree root

    if (rootNode is EesFileNode fileNode)
    {
        Console.WriteLine($"--- AST Built Successfully ({fileNode.Statements.Count} statements found) ---");
        // Optional: Print the constructed AST for debugging
        // Console.WriteLine("--- Constructed AST ---");
        // Console.WriteLine(rootNode.ToString());
        // Console.WriteLine("-----------------------");
    }
    else
    {
        Console.WriteLine("--- AST Building FAILED: Root node is not an EesFileNode ---");
        return 1; // Indicate failure
    }
    // *** END NEW VISITOR STEP ***

    return 0; // Indicate success
}
catch (Exception ex)
{
    Console.WriteLine($"An error occurred during processing: {ex.Message}");
    Console.WriteLine(ex.StackTrace); // Show stack trace for debugging
    return 1; // Indicate error
}
