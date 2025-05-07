// LibreSolvE.TUI/Program.cs
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Terminal.Gui;
using LibreSolvE.Core.Ast;
using LibreSolvE.Core.Evaluation;
using LibreSolvE.Core.Parsing;
using Antlr4.Runtime;

namespace LibreSolvE.TUI;

class Program
{
    static void Main(string[] args)
    {
        // Initialize Terminal.Gui
        Application.Init();

        // Create the main window
        var mainWindow = new MainWindow();

        // Run the application
        Application.Run(mainWindow);

        // Clean up when done
        Application.Shutdown();
    }
}

/// <summary>
/// Main window for the LibreSolvE TUI
/// </summary>
class MainWindow : Window
{
    private ListView _fileListView;
    private List<string> _files = new List<string>();
    private TextView _outputTextView;
    private TextView _editorTextView;
    private MenuBar _menuBar;
    private StatusBar _statusBar;
    private FrameView _fileExplorerFrame;
    private FrameView _editorFrame;
    private FrameView _outputFrame;
    private string _currentDirectory = Directory.GetCurrentDirectory();
    private string _currentFile = string.Empty;

    public MainWindow() : base("LibreSolvE TUI")
    {
        // Fill the screen
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        // Create menu bar
        _menuBar = new MenuBar(new MenuBarItem[] {
            new MenuBarItem("_File", new MenuItem[] {
                new MenuItem("_Open", "Open a file", OpenFile),
                new MenuItem("_Save", "Save current file", SaveFile),
                new MenuItem("_Run", "Run current file", RunFile),
                new MenuItem("_Quit", "Exit the application", () => Application.RequestStop())
            }),
            new MenuBarItem("_Help", new MenuItem[] {
                new MenuItem("_About", "About LibreSolvE", () => MessageBox.Query("About LibreSolvE",
                    "LibreSolvE TUI v0.1.0\nTerminal User Interface for LibreSolvE\nA libre equation solving environment", "OK"))
            })
        });
        Add(_menuBar);

        // Create file explorer frame
        _fileExplorerFrame = new FrameView("Files")
        {
            X = 0,
            Y = 1, // Below menu bar
            Width = 25,
            Height = Dim.Fill(2) // Leave space for status bar
        };
        Add(_fileExplorerFrame);

        // Create file list view
        _fileListView = new ListView()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            AllowsMarking = false
        };
        _fileListView.OpenSelectedItem += OnFileOpen;
        _fileExplorerFrame.Add(_fileListView);

        // Create editor frame
        _editorFrame = new FrameView("Editor")
        {
            X = 25,
            Y = 1, // Below menu bar
            Width = Dim.Fill(),
            Height = Dim.Percent(60)
        };
        Add(_editorFrame);

        // Create editor text view
        _editorTextView = new TextView()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = false
        };
        _editorFrame.Add(_editorTextView);

        // Create output frame
        _outputFrame = new FrameView("Output")
        {
            X = 25,
            Y = Pos.Bottom(_editorFrame),
            Width = Dim.Fill(),
            Height = Dim.Fill(2) // Leave space for status bar
        };
        Add(_outputFrame);

        // Create output text view
        _outputTextView = new TextView()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true
        };
        _outputFrame.Add(_outputTextView);

        // Create status bar
        _statusBar = new StatusBar(new StatusItem[] {
            new StatusItem(Key.F1, "~F1~ Help", () => MessageBox.Query("Help", "F1: Help\nF2: Open\nF3: Save\nF9: Run\nCtrl-Q: Quit", "OK")),
            new StatusItem(Key.F2, "~F2~ Open", OpenFile),
            new StatusItem(Key.F3, "~F3~ Save", SaveFile),
            new StatusItem(Key.F9, "~F9~ Run", RunFile),
            new StatusItem(Key.CtrlMask | Key.Q, "~Ctrl-Q~ Quit", () => Application.RequestStop())
        });
        Add(_statusBar);

        // Load files in current directory
        RefreshFileList();
    }

    private void RefreshFileList()
    {
        try
        {
            _files = Directory.GetFiles(_currentDirectory, "*.lse").Select(Path.GetFileName).Where(file => file != null).Cast<string>().ToList();
            _fileListView.SetSource(_files);
        }
        catch (Exception ex)
        {
            MessageBox.ErrorQuery("Error", $"Failed to list files: {ex.Message}", "OK");
        }
    }

    private void OnFileOpen(ListViewItemEventArgs args)
    {
        if (args.Item >= 0 && args.Item < _files.Count)
        {
            string filePath = Path.Combine(_currentDirectory, _files[args.Item]);
            LoadFile(filePath);
        }
    }

    private void OpenFile()
    {
        var dialog = new OpenDialog("Open", "Select a .lse file to open")
        {
            AllowsMultipleSelection = false,
            CanChooseDirectories = false,
            CanChooseFiles = true,
            DirectoryPath = _currentDirectory
        };

        Application.Run(dialog);

        if (!dialog.Canceled && dialog.FilePaths.Count > 0)
        {
            string filePath = dialog.FilePaths[0].ToString();
            if (File.Exists(filePath))
            {
                LoadFile(filePath);
            }
        }
    }

    private void LoadFile(string filePath)
    {
        try
        {
            _currentFile = filePath;
            string content = File.ReadAllText(filePath);
            _editorTextView.Text = content;
            _editorFrame.Title = $"Editor - {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            MessageBox.ErrorQuery("Error", $"Failed to load file: {ex.Message}", "OK");
        }
    }

    private void SaveFile()
    {
        if (string.IsNullOrEmpty(_currentFile))
        {
            SaveFileAs();
            return;
        }

        try
        {
            File.WriteAllText(_currentFile, _editorTextView.Text.ToString());
            MessageBox.Query("Save", "File saved successfully", "OK");
        }
        catch (Exception ex)
        {
            MessageBox.ErrorQuery("Error", $"Failed to save file: {ex.Message}", "OK");
        }
    }

    private void SaveFileAs()
    {
        var dialog = new SaveDialog("Save As", "Save file as:")
        {
            DirectoryPath = _currentDirectory,
            FilePath = _currentFile
        };

        Application.Run(dialog);

        if (!dialog.Canceled)
        {
            _currentFile = dialog.FilePath?.ToString() ?? string.Empty;
            SaveFile();
            RefreshFileList();
        }
    }

    private void RunFile()
    {
        if (string.IsNullOrEmpty(_currentFile))
        {
            MessageBox.ErrorQuery("Error", "No file is currently open", "OK");
            return;
        }

        try
        {
            // Save current content before running
            File.WriteAllText(_currentFile, _editorTextView.Text.ToString());

            // Run the current file
            var result = RunLseFile(_currentFile);

            // Display results
            _outputTextView.Text = result;
        }
        catch (Exception ex)
        {
            MessageBox.ErrorQuery("Error", $"Error running file: {ex.Message}", "OK");
        }
    }

    private string RunLseFile(string filePath)
    {
        // Create a string writer to capture console output
        var outputWriter = new StringWriter();

        // Save original console out
        var originalOut = Console.Out;

        try
        {
            // Redirect console output to our string writer
            Console.SetOut(outputWriter);

            // Run the LSE file similar to the CLI
            string inputText = File.ReadAllText(filePath);

            // 1. Parse Units
            outputWriter.WriteLine($"--- Reading file: {filePath} ---");
            outputWriter.WriteLine("--- Extracting units from source ---");
            var unitsDictionary = UnitParser.ExtractUnitsFromSource(inputText);
            outputWriter.WriteLine($"Found {unitsDictionary.Count} variables with units specified.");
            if (unitsDictionary.Count > 0)
            {
                foreach (var kvp in unitsDictionary)
                    outputWriter.WriteLine($"  {kvp.Key}: [{kvp.Value}]");
            }

            // 2. ANTLR Parsing
            AntlrInputStream inputStream = new AntlrInputStream(inputText);
            EesLexer lexer = new EesLexer(inputStream);
            CommonTokenStream commonTokenStream = new CommonTokenStream(lexer);
            EesParser parser = new EesParser(commonTokenStream);

            // Setup error handling
            parser.RemoveErrorListeners();
            lexer.RemoveErrorListeners();
            // var errorListener = new ThrowingErrorListener();
            var errorListener = new BetterErrorListener();
            parser.AddErrorListener(errorListener);
            lexer.AddErrorListener(errorListener);

            outputWriter.WriteLine("--- Attempting to parse file content ---");
            EesParser.EesFileContext parseTreeContext = parser.eesFile();
            outputWriter.WriteLine("--- Parsing SUCCESSFUL (Basic syntax check passed) ---");

            // 3. Build Abstract Syntax Tree (AST)
            outputWriter.WriteLine("--- Building Abstract Syntax Tree (AST)... ---");
            var astBuilder = new AstBuilderVisitor();
            AstNode rootAstNode = astBuilder.VisitEesFile(parseTreeContext);

            if (rootAstNode is not EesFileNode fileNode)
            {
                outputWriter.WriteLine("--- AST Building FAILED: Root node is not the expected EesFileNode type ---");
                return outputWriter.ToString();
            }
            outputWriter.WriteLine($"--- AST Built Successfully ({fileNode.Statements.Count} statements found) ---");

            // 4. Initialize Core Components
            outputWriter.WriteLine("--- Initializing Execution Environment ---");
            var variableStore = new VariableStore();
            var functionRegistry = new FunctionRegistry();
            var solverSettings = new SolverSettings();

            // Apply parsed units to the store
            UnitParser.ApplyUnitsToVariableStore(variableStore, unitsDictionary);

            // 5. Execute Statements and Collect Equations
            var executor = new StatementExecutor(variableStore, functionRegistry, solverSettings);
            executor.Execute(fileNode);

            outputWriter.WriteLine("\n--- Variable Store State After Assignments ---");
            variableStore.PrintVariables();

            // 6. Solve Equations
            outputWriter.WriteLine("\n--- Equation Solving Phase ---");
            bool solveSuccess = executor.SolveRemainingAlgebraicEquations();

            if (solveSuccess)
            {
                outputWriter.WriteLine("\n--- Solver Phase Completed Successfully ---");
                outputWriter.WriteLine("\n--- Final Variable Store State ---");
                variableStore.PrintVariables();
            }
            else
            {
                outputWriter.WriteLine("\n--- Solver FAILED ---");
                outputWriter.WriteLine("\n--- Variable Store State After Failed Solve Attempt ---");
                variableStore.PrintVariables();
            }
            outputWriter.WriteLine("------------------------------------");
        }
        catch (Exception ex)
        {
            outputWriter.WriteLine($"\n--- ERROR ---");
            outputWriter.WriteLine($"{ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
            {
                outputWriter.WriteLine($"Inner Exception: {ex.InnerException.Message}");
            }
        }
        finally
        {
            // Restore original console out
            Console.SetOut(originalOut);
        }

        return outputWriter.ToString();
    }
}

// Custom error listener for ANTLR (same as in CLI)
public class ThrowingErrorListener : Antlr4.Runtime.BaseErrorListener, Antlr4.Runtime.IAntlrErrorListener<int>
{
    public override void SyntaxError(TextWriter output, Antlr4.Runtime.IRecognizer recognizer, Antlr4.Runtime.IToken offendingSymbol, int line, int charPositionInLine, string msg, Antlr4.Runtime.RecognitionException e)
    {
        string errorMessage = $"Syntax error at line {line}:{charPositionInLine} near '{offendingSymbol?.Text ?? "<EOF>"}': {msg}";
        throw new ParsingException(errorMessage, e);
    }

    public void SyntaxError(TextWriter output, Antlr4.Runtime.IRecognizer recognizer, int offendingSymbol, int line, int charPositionInLine, string msg, Antlr4.Runtime.RecognitionException e)
    {
        string errorMessage = $"Lexer error at line {line}:{charPositionInLine}: {msg}";
        throw new ParsingException(errorMessage, e);
    }
}
