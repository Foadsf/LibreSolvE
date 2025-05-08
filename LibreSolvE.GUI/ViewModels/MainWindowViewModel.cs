// File: LibreSolvE.GUI/ViewModels/MainWindowViewModel.cs
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibreSolvE.Core.Ast;
using LibreSolvE.Core.Evaluation;
using LibreSolvE.Core.Parsing;
using LibreSolvE.Core.Plotting;
using LibreSolvE.GUI.Views; // May need removal if PlotView is gone
// REMOVE using OxyPlot;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Antlr4.Runtime;
using LibreSolvE.GUI.AOP;
using LibreSolvE.GUI.Logging;
using Serilog;
using ScottPlot; // <--- ADD THIS
using ScottPlot.Avalonia; // <-- ADD THIS

namespace LibreSolvE.GUI.ViewModels
{
    // New class to hold ScottPlot data for the UI
    public class ScottPlotViewModel : ViewModelBase
    {
        private Plot _plot = new(); // ScottPlot.Plot model
        public Plot Plot
        {
            get => _plot;
            set => SetProperty(ref _plot, value);
        }

        private string _title = "Plot";
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }
    }

    public partial class MainWindowViewModel : ViewModelBase
    {
        private string _fileContent = "";
        private string _outputText = "";
        private string _filePath = "";
        private string _statusText = "Ready";
        private Avalonia.Controls.Window? _window; // Fix: Use fully qualified Window type

        private EquationsViewModel _equationsVM = new EquationsViewModel();
        private SolutionViewModel _solutionVM = new SolutionViewModel();
        private ObservableCollection<ScottPlotViewModel> _plotViewModels = new ObservableCollection<ScottPlotViewModel>();

        // private StatementExecutor? _executor; // Core executor instance

        private IntegralTableViewModel _integralTableVM = new IntegralTableViewModel();
        public IntegralTableViewModel IntegralTableVM
        {
            get => _integralTableVM;
            set => SetProperty(ref _integralTableVM, value);
        }


        public string FileContent
        {
            get => _fileContent;
            set
            {
                if (SetProperty(ref _fileContent, value))
                {
                    // Sync with EquationsVM if it exists
                    if (EquationsVM != null)
                    {
                        EquationsVM.EquationText = value;
                    }
                }
            }
        }

        public string OutputText
        {
            get => _outputText;
            set => SetProperty(ref _outputText, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public EquationsViewModel EquationsVM
        {
            get => _equationsVM;
            set => SetProperty(ref _equationsVM, value);
        }

        public SolutionViewModel SolutionVM
        {
            get => _solutionVM;
            set => SetProperty(ref _solutionVM, value);
        }

        public ObservableCollection<ScottPlotViewModel> PlotViewModels
        {
            get => _plotViewModels;
            set => SetProperty(ref _plotViewModels, value);
        }


        public ICommand OpenCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand SaveAsCommand { get; }
        public ICommand RunCommand { get; }
        public ICommand ExitCommand { get; }

        private DebugLogViewModel _debugLogVM = new DebugLogViewModel(); // Initialize in declaration
        public DebugLogViewModel DebugLogVM   // Add this
        {
            get => _debugLogVM;
            set => SetProperty(ref _debugLogVM, value);
        }

        [Log]
        public MainWindowViewModel()
        {
            OpenCommand = new AsyncRelayCommand(OpenFileAsync);
            SaveCommand = new AsyncRelayCommand(SaveFileAsync);
            SaveAsCommand = new AsyncRelayCommand(SaveFileAsAsync);
            RunCommand = new RelayCommand(RunFile);
            ExitCommand = new RelayCommand(() =>
            {
                if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktopApp)
                {
                    desktopApp.Shutdown();
                }
                else
                {
                    Environment.Exit(0);
                }
            });

            EquationsVM = new EquationsViewModel();
            SolutionVM = new SolutionViewModel();
            // CHANGE Instantiation type
            PlotViewModels = new ObservableCollection<ScottPlotViewModel>();
            DebugLogVM = new DebugLogViewModel();
            IntegralTableVM = new IntegralTableViewModel();

            this.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(FileContent) && EquationsVM != null)
                {
                    if (EquationsVM.EquationText != FileContent)
                        EquationsVM.EquationText = FileContent;
                }
            };

            EquationsVM.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(EquationsViewModel.EquationText))
                {
                    if (FileContent != EquationsVM.EquationText)
                        FileContent = EquationsVM.EquationText;
                }
            };
        }


        public void SetWindow(Avalonia.Controls.Window window)
        {
            if (window != null)
            {
                _window = window;
                StatusText = "Ready - Window reference set";
            }
            else
            {
                StatusText = "Error: Window reference is null";
            }
        }

        private async Task OpenFileAsync()
        {
            if (_window == null)
            {
                StatusText = "Error: Window reference not set for file operations.";
                return;
            }

            var storageProvider = _window.StorageProvider;
            if (!storageProvider.CanOpen)
            {
                StatusText = "Error: Cannot open files on this platform.";
                return;
            }

            var lseFileType = new FilePickerFileType("LibreSolvE Files (*.lse)")
            { Patterns = new[] { "*.lse" } };
            var allFileType = FilePickerFileTypes.All;

            var result = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open LibreSolvE File",
                AllowMultiple = false,
                FileTypeFilter = new[] { lseFileType, allFileType }
            });

            if (result?.Count > 0)
            {
                var file = result[0];
                _filePath = file.Path.LocalPath; // Note: LocalPath might not always be available. Consider using TryGetLocalPath.
                if (string.IsNullOrEmpty(_filePath)) // Fallback if LocalPath is null
                {
                    _filePath = file.Name; // Use name as a temporary placeholder if path isn't accessible
                }

                try
                {
                    using var stream = await file.OpenReadAsync();
                    using var reader = new StreamReader(stream);
                    FileContent = await reader.ReadToEndAsync(); // This will also update EquationsVM.EquationText
                    StatusText = $"Opened: {Path.GetFileName(_filePath)}";
                }
                catch (Exception ex)
                {
                    StatusText = $"Error opening file '{file.Name}': {ex.Message}";
                    OutputText = $"Error opening file:\n{ex}";
                }
            }
        }

        private async Task SaveFileAsync()
        {
            if (string.IsNullOrEmpty(_filePath))
            {
                await SaveFileAsAsync();
            }
            else
            {
                try
                {
                    await File.WriteAllTextAsync(_filePath, FileContent); // Use FileContent from VM
                    StatusText = $"Saved: {Path.GetFileName(_filePath)}";
                }
                catch (Exception ex)
                {
                    StatusText = $"Error saving file '{_filePath}': {ex.Message}";
                    OutputText = $"Error saving file:\n{ex}";
                }
            }
        }

        private async Task SaveFileAsAsync()
        {
            if (_window == null)
            {
                StatusText = "Error: Window reference not set for file operations.";
                return;
            }

            var storageProvider = _window.StorageProvider;
            if (!storageProvider.CanSave)
            {
                StatusText = "Error: Cannot save files on this platform.";
                return;
            }

            var lseFileType = new FilePickerFileType("LibreSolvE Files (*.lse)")
            { Patterns = new[] { "*.lse" } };

            var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save LibreSolvE File As",
                SuggestedFileName = Path.GetFileName(_filePath) ?? "untitled.lse",
                DefaultExtension = ".lse",
                FileTypeChoices = new[] { lseFileType }
            });

            if (file != null)
            {
                _filePath = file.Path.LocalPath; // Update current file path
                if (string.IsNullOrEmpty(_filePath)) // Fallback if path not accessible
                {
                    _filePath = file.Name; // Use name as temp if path not accessible
                }

                try
                {
                    using var stream = await file.OpenWriteAsync();
                    using var writer = new StreamWriter(stream);
                    await writer.WriteAsync(FileContent); // Use FileContent from VM
                    StatusText = $"Saved as: {Path.GetFileName(_filePath)}";
                }
                catch (Exception ex)
                {
                    StatusText = $"Error saving file as '{file.Name}': {ex.Message}";
                    OutputText = $"Error saving file as:\n{ex}";
                }
            }
        }

        [Log]
        private void RunFile()
        {
            // --- Setup ---
            var outputBuilder = new StringBuilder(); // To collect all output for the UI TextBox
            TextWriter originalConsoleOut = Console.Out; // Save original console output stream
            StringWriter capturedConsoleWriter = new StringWriter(); // To capture Console.Write from core library
            string currentInputText = EquationsVM.EquationText; // Get the code to run from the editor ViewModel

            // --- Reset UI State ---
            StatusText = "Executing..."; // Update status bar
            OutputText = ""; // Clear the raw output text box
            SolutionVM.Variables.Clear(); // Clear the previous solution grid visually
            PlotViewModels.Clear(); // Clear any plots from the previous run
            IntegralTableVM.TableData.Clear(); // Clear the integral table from the previous run

            // --- Declare Core Variables ---
            // These need to be declared outside the try block to be accessible in finally
            VariableStore? variableStore = null;
            StatementExecutor? executor = null; // Use the class field _executor if appropriate, or local if instance-per-run
            bool solveSuccess = false;
            EesParser? parser = null; // Declare parser here
            EesLexer? lexer = null;   // Declare lexer here

            try
            {
                Console.SetOut(capturedConsoleWriter); // Redirect Console.Write to capture core library output

                // 1. Initialize Core Components
                variableStore = new VariableStore(); // Initialize the variable store for this run
                var functionRegistry = new FunctionRegistry(); // Initialize function registry (with built-ins)
                var solverSettings = new SolverSettings(); // Initialize solver settings (TODO: Link to UI later)

                // 2. Parse Units from Source
                var unitsDictionary = UnitParser.ExtractUnitsFromSource(currentInputText);
                UnitParser.ApplyUnitsToVariableStore(variableStore, unitsDictionary);
                LogCaptured(capturedConsoleWriter, $"Found {unitsDictionary.Count} units.\n");

                // 3. ANTLR Parsing Setup
                AntlrInputStream inputStream = new AntlrInputStream(currentInputText);
                lexer = new EesLexer(inputStream); // Assign to the declared variable
                CommonTokenStream commonTokenStream = new CommonTokenStream(lexer);
                parser = new EesParser(commonTokenStream); // Assign to the declared variable

                // Setup error handling (now parser and lexer are in scope)
                var errorListener = new BetterErrorListener(); // Custom listener for better errors
                parser.RemoveErrorListeners(); // Remove default console error listener
                lexer.RemoveErrorListeners();
                parser.AddErrorListener(errorListener); // Add our custom listener
                lexer.AddErrorListener(errorListener);

                // --- Parse ---
                LogCaptured(capturedConsoleWriter, "Parsing input...\n");
                EesParser.EesFileContext parseTreeContext = parser.eesFile(); // Execute the parse rule
                LogCaptured(capturedConsoleWriter, "Parsing successful.\n");

                // 4. Build Abstract Syntax Tree (AST)
                LogCaptured(capturedConsoleWriter, "Building AST...\n");
                var astBuilder = new AstBuilderVisitor();
                AstNode rootAstNode = astBuilder.VisitEesFile(parseTreeContext); // Visit the parse tree to build AST
                LogCaptured(capturedConsoleWriter, "AST built.\n");

                if (rootAstNode is EesFileNode fileNode)
                {
                    // 5. Initialize StatementExecutor & Execute Assignments/ODEs
                    executor = new StatementExecutor(variableStore, functionRegistry, solverSettings); // Use local instance
                    executor.PlotCreated += OnPlotCreated; // Subscribe to plot event

                    LogAttribute.LogMessage("Executing initial statements and ODEs...");
                    executor.Execute(fileNode); // Process assignments, ODEs, directives
                    LogAttribute.LogMessage("Initial execution phase complete.");

                    // --- Log Variable State Before Algebraic Solve ---
                    LogAttribute.LogMessage("Variable Store State BEFORE Algebraic Solve:");
                    VariableStoreLogger.LogVariableStoreContents("Before Solve", variableStore);

                    // 6. Solve Remaining Algebraic Equations
                    LogAttribute.LogMessage("Solving algebraic equations...");
                    solveSuccess = executor.SolveRemainingAlgebraicEquations(); // Attempt to solve the system
                    LogAttribute.LogMessage("Algebraic solve attempt {Result}", solveSuccess ? "succeeded" : "failed");

                    // --- Log Variable State After Algebraic Solve ---
                    LogAttribute.LogMessage("Variable Store State AFTER Algebraic Solve:");
                    VariableStoreLogger.LogVariableStoreContents("After Solve", variableStore);

                    // 7. Update Status Text
                    StatusText = solveSuccess ? "Execution complete. Solver converged." : "Execution complete. Solver did NOT converge or no algebraic equations to solve.";
                }
                else
                {
                    // Handle case where AST root is not the expected type
                    outputBuilder.AppendLine("AST Build Error: Root node is not EesFileNode.");
                    StatusText = "AST Build Error";
                    solveSuccess = false; // Mark run as failed
                }
            }
            catch (ParsingException pEx)
            {
                // Handle parsing errors specifically
                outputBuilder.AppendLine($"\n--- PARSING FAILED ---\n{pEx.Message}");
                StatusText = "Parsing Failed";
                Debug.WriteLine($"Parsing Exception: {pEx}");
                solveSuccess = false; // Mark run as failed
            }
            catch (Exception coreEx)
            {
                // Handle general errors during core execution or solving
                outputBuilder.AppendLine($"\n--- CORE EXECUTION ERROR ---\n{coreEx.GetType().Name}: {coreEx.Message}");
                if (coreEx.StackTrace != null)
                {
                    outputBuilder.AppendLine(coreEx.StackTrace);
                }
                StatusText = "Core Execution Error";
                Debug.WriteLine($"Core Exception: {coreEx}");
                solveSuccess = false; // Mark run as failed
            }
            finally
            {
                // --- Restore Console and Update UI ---
                Console.SetOut(originalConsoleOut); // IMPORTANT: Restore original console output
                outputBuilder.Insert(0, capturedConsoleWriter.ToString()); // Prepend captured core output
                OutputText = outputBuilder.ToString(); // Update the raw output UI element

                // Update the structured solution view, even if solve failed (might show intermediate values)
                if (variableStore != null)
                {
                    var variableCount = variableStore.GetAllVariableNames().Count();
                    LogAttribute.LogMessage("About to update SolutionVM with {VariableCount} variables", variableCount);

                    // Log variable names and values for debugging
                    foreach (var varName in variableStore.GetAllVariableNames())
                    {
                        LogAttribute.LogMessage("Variable {VarName} = {Value} {Units} (IsExplicit: {IsExplicit})",
                            varName,
                            variableStore.GetVariable(varName),
                            variableStore.GetUnit(varName),
                            variableStore.IsExplicitlySet(varName));
                    }

                    SolutionVM.UpdateResults(variableStore);

                    Dispatcher.UIThread.Post(() =>
                    {
                        LogAttribute.LogMessage("Running delayed diagnostics after UI update");
                        LogAttribute.LogMessage("SolutionVM now has {Count} variables", SolutionVM.Variables.Count);
                        SolutionVM.LogItemProperties();
                    }, DispatcherPriority.Background);

                    // Update the integral table view model if executor has data
                    if (executor != null)
                    {
                        var integralTable = executor.GetIntegralTable();
                        if (integralTable != null && integralTable.Count > 0 && integralTable.Values.Any(v => v.Count > 0))
                        {
                            LogAttribute.LogMessage("Updating IntegralTableVM with data from executor");

                            // Instead of replacing the entire view model, update the data in the existing one
                            if (IntegralTableVM != null)
                            {
                                IntegralTableVM.UpdateFromIntegralTable(integralTable);
                            }
                            else
                            {
                                IntegralTableVM = new IntegralTableViewModel();
                                IntegralTableVM.UpdateFromIntegralTable(integralTable);
                            }
                        }
                        else
                        {
                            LogAttribute.LogMessage("No integral table data to display");
                        }
                    }
                }
                else
                {
                    Debug.WriteLine("--- VariableStore was null in finally, cannot update SolutionVM ---");
                    // UI Thread Post might be needed if clearing from non-UI thread causes issues
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => SolutionVM.Variables.Clear());
                }

                // Unsubscribe from the event to prevent memory leaks
                if (executor != null)
                {
                    executor.PlotCreated -= OnPlotCreated;
                }
            }
        } // End of RunFile method

        private void LogCaptured(TextWriter capturedWriter, string message)
        {
            // Helper to write to the captured stream, useful if Console.Out is redirected
            capturedWriter.Write(message);
        }

        [Log]
        private void OnPlotCreated(object? sender, PlotData plotData)
        {
            Log.Debug("OnPlotCreated event handler started (ScottPlot). Received PlotData: {PlotDataTitle}", plotData?.Settings?.Title ?? "null");
            try
            {
                if (plotData == null)
                {
                    Log.Warning("OnPlotCreated received null plotData.");
                    StatusText = "Warning: Plot data generation failed.";
                    return;
                }
                string title = plotData.Settings?.Title ?? "Plot";
                string xLabel = plotData.Settings?.XLabel ?? "X-Axis";
                string yLabel = plotData.Settings?.YLabel ?? "Y-Axis";
                // ScottPlot grid can be enabled/disabled per axis later if needed

                Log.Debug("Plot settings retrieved: Title='{Title}', XLabel='{XLabel}', YLabel='{YLabel}'", title, xLabel, yLabel);

                // Ensure this runs on the UI thread
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Log.Debug("Executing ScottPlot creation on UI thread for plot '{PlotTitle}'. Current PlotViewModels count: {Count}", title, PlotViewModels.Count);
                    try
                    {
                        // Create a ScottPlot Plot model
                        var scottPlotModel = new ScottPlot.Plot();
                        scottPlotModel.Title(title);
                        scottPlotModel.XLabel(xLabel);
                        scottPlotModel.YLabel(yLabel);
                        scottPlotModel.ShowLegend(Alignment.UpperRight); // Enable legend, specify position

                        // Add series data
                        if (plotData.Series == null || !plotData.Series.Any())
                        {
                            Log.Warning("PlotData for '{PlotTitle}' contains no Series.", title);
                            // Use Add.Annotation for ScottPlot 5
                            scottPlotModel.Add.Annotation("No series data available", Alignment.MiddleCenter);
                        }
                        else
                        {
                            Log.Debug("Processing {SeriesCount} series for ScottPlot '{PlotTitle}'", plotData.Series.Count, title);
                            foreach (var seriesData in plotData.Series)
                            {
                                if (seriesData == null || seriesData.XValues == null || seriesData.YValues == null || !seriesData.XValues.Any())
                                {
                                    Log.Warning("Skipping invalid series data: {SeriesName} in plot '{PlotTitle}'", seriesData?.Name ?? "Unnamed", title);
                                    continue;
                                }

                                // Filter out NaN/Infinity before adding to ScottPlot
                                var validPoints = seriesData.XValues
                                    .Zip(seriesData.YValues, (x, y) => new { X = x, Y = y })
                                    .Where(pt => double.IsFinite(pt.X) && double.IsFinite(pt.Y))
                                    .ToList();

                                if (validPoints.Any())
                                {
                                    var xs = validPoints.Select(pt => pt.X).ToArray();
                                    var ys = validPoints.Select(pt => pt.Y).ToArray();

                                    var scatter = scottPlotModel.Add.Scatter(xs, ys);
                                    // Use LegendText (preferred in ScottPlot 5) instead of Label
                                    // CHANGE THIS LINE: Use LINQ Count() method
                                    scatter.LegendText = seriesData.Name ?? $"Series {scottPlotModel.GetPlottables().Count()}";
                                    // Corrected Log.Debug call parameters
                                    // CHANGE THIS LINE: Use LINQ Count() method
                                    Log.Debug("Added Scatter plot for series '{SeriesName}' with {PointCount} valid points to ScottPlot '{PlotTitle}'", scatter.LegendText, validPoints.Count, title);
                                }
                                else
                                {
                                    // Use LegendText here too for consistency in logging
                                    Log.Warning("Series '{SeriesName}' for plot '{PlotTitle}' had no valid (finite) points and was not added.", seriesData.Name ?? "Unnamed", title);
                                }
                            }
                        }

                        scottPlotModel.Axes.AutoScale(); // Adjust axes limits based on data

                        // Create the ViewModel for the plot
                        var plotVM = new ScottPlotViewModel
                        {
                            Plot = scottPlotModel,
                            Title = title // Store title separately if needed for UI elements outside plot
                        };

                        PlotViewModels.Add(plotVM); // Add to the UI collection
                        StatusText = $"Plot '{title}' displayed.";
                        Log.Debug("ScottPlotViewModel for '{PlotTitle}' added to PlotViewModels collection. New count: {Count}", title, PlotViewModels.Count);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error creating ScottPlot components on UI thread for plot '{PlotTitle}'", title);
                        StatusText = "Error creating plot. See debug log.";
                    }
                }); // End InvokeAsync
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in OnPlotCreated handler (ScottPlot - before UI dispatch)");
                StatusText = "Error processing plot data. See debug log.";
            }
            Log.Debug("OnPlotCreated event handler finished (ScottPlot).");
        }

        public void DebugSolutionTab()
        {
            // This would be called from MainWindow.axaml.cs after loading
            LogAttribute.LogMessage("DebugSolutionTab: SolutionVM has {Count} variables",
                SolutionVM?.Variables?.Count ?? 0);

            // If we have a reference to the DataGrid, we could pass it here:
            // SolutionVM.DebugDataGridBinding(dataGrid);
        }

    }
}
