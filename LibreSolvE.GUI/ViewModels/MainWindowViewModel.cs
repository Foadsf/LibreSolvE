// Fixed version of MainWindowViewModel.cs
using Avalonia.Controls; // Fix for Window reference
using Avalonia.Platform.Storage; // For IStorageProvider
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibreSolvE.Core.Ast;
using LibreSolvE.Core.Evaluation;
using LibreSolvE.Core.Parsing;
using LibreSolvE.Core.Plotting; // For PlotData
using LibreSolvE.GUI.Views; // For PlotView
using OxyPlot; // For PlotModel (OxyPlot's model)
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input; // For ICommand
using Antlr4.Runtime; // For ANTLR parsing components

namespace LibreSolvE.GUI.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private string _fileContent = "";
        private string _outputText = "";
        private string _filePath = "";
        private string _statusText = "Ready";
        private Avalonia.Controls.Window? _window; // Fix: Use fully qualified Window type

        private EquationsViewModel _equationsVM = new EquationsViewModel();
        private SolutionViewModel _solutionVM = new SolutionViewModel();
        private ObservableCollection<PlotViewModel> _plotViewModels = new ObservableCollection<PlotViewModel>();

        private StatementExecutor? _executor; // Core executor instance

        // Add this property that was missing
        public PlotView? PlotView { get; set; }

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

        public ObservableCollection<PlotViewModel> PlotViewModels
        {
            get => _plotViewModels;
            set => SetProperty(ref _plotViewModels, value);
        }


        public ICommand OpenCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand SaveAsCommand { get; }
        public ICommand RunCommand { get; }
        public ICommand ExitCommand { get; }

        public MainWindowViewModel()
        {
            OpenCommand = new AsyncRelayCommand(OpenFileAsync);
            SaveCommand = new AsyncRelayCommand(SaveFileAsync);
            SaveAsCommand = new AsyncRelayCommand(SaveFileAsAsync);
            RunCommand = new RelayCommand(RunFile); // This is the method we are providing
            ExitCommand = new RelayCommand(() =>
            {
                // Fix: Use Avalonia.Application instead of Application
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
            PlotViewModels = new ObservableCollection<PlotViewModel>();


            // Two-way synchronization between FileContent and EquationsVM.EquationText
            this.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(FileContent) && EquationsVM != null)
                {
                    if (EquationsVM.EquationText != FileContent) // Avoid feedback loop
                        EquationsVM.EquationText = FileContent;
                }
            };

            EquationsVM.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(EquationsViewModel.EquationText))
                {
                    if (FileContent != EquationsVM.EquationText) // Avoid feedback loop
                        FileContent = EquationsVM.EquationText;
                }
            };
        }


        public void SetWindow(Avalonia.Controls.Window window) // Fix: Use fully qualified Window type
        {
            _window = window;
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

        private void RunFile()
        {
            var outputBuilder = new StringBuilder();
            TextWriter originalConsoleOut = Console.Out;
            StringWriter capturedConsoleWriter = new StringWriter();
            string currentInputText = EquationsVM.EquationText; // Get current text from editor VM

            StatusText = "Executing...";
            OutputText = ""; // Clear previous output
            SolutionVM.Variables.Clear(); // Clear previous solution
            PlotViewModels.Clear(); // Clear previous plots

            try
            {
                Console.SetOut(capturedConsoleWriter); // Capture all Console.Write from core

                // 1. Initialize Core Components
                var variableStore = new VariableStore();
                var functionRegistry = new FunctionRegistry();
                var solverSettings = new SolverSettings(); // TODO: Expose these settings in UI later

                // 2. Parse Units
                var unitsDictionary = UnitParser.ExtractUnitsFromSource(currentInputText);
                UnitParser.ApplyUnitsToVariableStore(variableStore, unitsDictionary);
                LogCaptured(capturedConsoleWriter, $"Found {unitsDictionary.Count} units.\n");

                // 3. ANTLR Parsing
                AntlrInputStream inputStream = new AntlrInputStream(currentInputText);
                EesLexer lexer = new EesLexer(inputStream);
                CommonTokenStream commonTokenStream = new CommonTokenStream(lexer);
                EesParser parser = new EesParser(commonTokenStream);
                var errorListener = new BetterErrorListener();
                parser.RemoveErrorListeners();
                lexer.RemoveErrorListeners();
                parser.AddErrorListener(errorListener);
                lexer.AddErrorListener(errorListener);

                EesParser.EesFileContext parseTreeContext = parser.eesFile();
                LogCaptured(capturedConsoleWriter, "Parsing successful.\n");

                // 4. Build AST
                var astBuilder = new AstBuilderVisitor();
                AstNode rootAstNode = astBuilder.VisitEesFile(parseTreeContext);
                LogCaptured(capturedConsoleWriter, "AST built.\n");

                if (rootAstNode is EesFileNode fileNode)
                {
                    // 5. Initialize StatementExecutor & Execute
                    // Create a new executor instance for each run
                    _executor = new StatementExecutor(variableStore, functionRegistry, solverSettings);
                    _executor.PlotCreated += OnPlotCreated; // Subscribe to plot event

                    _executor.Execute(fileNode); // This processes assignments, ODEs (which might raise PlotCreated)
                    LogCaptured(capturedConsoleWriter, "Initial statements and ODEs (if any) executed.\n");

                    // 6. Solve Remaining Algebraic Equations
                    bool solveSuccess = _executor.SolveRemainingAlgebraicEquations();
                    LogCaptured(capturedConsoleWriter, $"Algebraic solve attempt {(solveSuccess ? "succeeded" : "failed")}.\n");

                    // 7. Update UI
                    SolutionVM.UpdateResults(variableStore);
                    StatusText = solveSuccess ? "Execution complete. Solver converged." : "Execution complete. Solver did NOT converge or no algebraic equations to solve.";

                    // Any plots generated during _executor.Execute (e.g., from $IntegralTable)
                    // would have been handled by OnPlotCreated.
                    // If PLOT commands are processed after SolveRemainingAlgebraicEquations,
                    // they would also trigger OnPlotCreated.
                }
                else
                {
                    outputBuilder.AppendLine("AST Build Error: Root node is not EesFileNode.");
                    StatusText = "AST Build Error";
                }
            }
            catch (ParsingException pEx)
            {
                outputBuilder.AppendLine($"PARSING FAILED:\n{pEx.Message}");
                StatusText = "Parsing Failed";
                Debug.WriteLine($"Parsing Exception: {pEx}");
            }
            catch (Exception coreEx)
            {
                outputBuilder.AppendLine($"CORE EXECUTION ERROR:\n{coreEx.GetType().Name}: {coreEx.Message}");
                if (coreEx.StackTrace != null)
                {
                    outputBuilder.AppendLine(coreEx.StackTrace);
                }
                StatusText = "Core Execution Error";
                Debug.WriteLine($"Core Exception: {coreEx}");
            }
            finally
            {
                Console.SetOut(originalConsoleOut); // Restore console
                outputBuilder.Insert(0, capturedConsoleWriter.ToString()); // Prepend captured console output
                OutputText = outputBuilder.ToString();

                if (_executor != null)
                {
                    _executor.PlotCreated -= OnPlotCreated; // Unsubscribe to prevent leaks
                    _executor = null; // Release the instance
                }
            }
        }

        private void LogCaptured(TextWriter capturedWriter, string message)
        {
            // Helper to write to the captured stream, useful if Console.Out is redirected
            capturedWriter.Write(message);
        }

        private void OnPlotCreated(object? sender, PlotData plotData)
        {
            // Add a null check for plotData itself
            if (plotData == null)
            {
                Debug.WriteLine("OnPlotCreated received null plotData. Plot will not be displayed.");
                StatusText = "Warning: Plot data was not generated correctly.";
                return;
            }

            // Ensure Settings is not null before accessing its members
            string title = "Plot"; // Default title
            string xLabel = "X-Axis";
            string yLabel = "Y-Axis";
            bool showGrid = true;
            bool showLegend = true;

            if (plotData.Settings != null) // Check if Settings itself is null
            {
                title = string.IsNullOrEmpty(plotData.Settings.Title) ? "Plot" : plotData.Settings.Title;
                xLabel = string.IsNullOrEmpty(plotData.Settings.XLabel) ? "X-Axis" : plotData.Settings.XLabel;
                yLabel = string.IsNullOrEmpty(plotData.Settings.YLabel) ? "Y-Axis" : plotData.Settings.YLabel;
                showGrid = plotData.Settings.ShowGrid;
                showLegend = plotData.Settings.ShowLegend;
            }
            else
            {
                Debug.WriteLine("OnPlotCreated: plotData.Settings is null. Using default plot settings.");
            }

            // Ensure this runs on the UI thread
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                var plotVM = new PlotViewModel();
                var oxyModel = new PlotModel { Title = title }; // Use the safe title

                oxyModel.Axes.Add(new OxyPlot.Axes.LinearAxis
                {
                    Position = OxyPlot.Axes.AxisPosition.Bottom,
                    Title = xLabel, // Use safe xLabel
                    MajorGridlineStyle = showGrid ? LineStyle.Solid : LineStyle.None,
                    MinorGridlineStyle = showGrid ? LineStyle.Dot : LineStyle.None
                });
                oxyModel.Axes.Add(new OxyPlot.Axes.LinearAxis
                {
                    Position = OxyPlot.Axes.AxisPosition.Left,
                    Title = yLabel, // Use safe yLabel
                    MajorGridlineStyle = showGrid ? LineStyle.Solid : LineStyle.None,
                    MinorGridlineStyle = showGrid ? LineStyle.Dot : LineStyle.None
                });

                if (showLegend) // Use safe showLegend
                {
                    oxyModel.IsLegendVisible = true;
                    // Fix: Use available properties in OxyPlot
                    // oxyModel.LegendPosition = OxyPlot.LegendPosition.RightTop;
                    // oxyModel.LegendPlacement = OxyPlot.LegendPlacement.Inside;
                }

                var defaultColors = new OxyColor[]
                {
                    OxyColors.Blue, OxyColors.Red, OxyColors.Green, OxyColors.Orange,
                    OxyColors.Purple, OxyColors.Brown, OxyColors.Magenta, OxyColors.Teal
                };
                int colorIndex = 0;

                if (plotData.Series != null) // Check if Series collection is null
                {
                    foreach (var seriesData in plotData.Series)
                    {
                        if (seriesData == null) continue; // Skip null series data

                        var lineSeries = new OxyPlot.Series.LineSeries
                        {
                            Title = seriesData.Name ?? "Series", // Handle null series name
                            StrokeThickness = 2
                        };

                        // Try to parse color string, fallback to default
                        try
                        {
                            lineSeries.Color = !string.IsNullOrEmpty(seriesData.Color) ?
                                               OxyColor.Parse(seriesData.Color) :
                                               defaultColors[colorIndex % defaultColors.Length];
                        }
                        catch
                        {
                            lineSeries.Color = defaultColors[colorIndex % defaultColors.Length];
                        }

                        if (seriesData.XValues != null && seriesData.YValues != null) // Check XValues and YValues
                        {
                            for (int i = 0; i < seriesData.XValues.Count && i < seriesData.YValues.Count; i++)
                            {
                                lineSeries.Points.Add(new DataPoint(seriesData.XValues[i], seriesData.YValues[i]));
                            }
                        }
                        oxyModel.Series.Add(lineSeries);
                        colorIndex++;
                    }
                }
                plotVM.PlotModel = oxyModel;
                PlotViewModels.Add(plotVM);
                StatusText = $"Plot '{title}' displayed.";
            });
        }
    }
}
