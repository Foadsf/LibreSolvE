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
using LibreSolvE.GUI.AOP;
using Serilog;
using ScottPlot;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic; // For List and Dictionary
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Antlr4.Runtime;

namespace LibreSolvE.GUI.ViewModels
{
    public class ScottPlotViewModel : ViewModelBase
    {
        private Plot _plot = new();
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
        #region Fields
        private string _fileContent = "";
        private string _outputText = "";
        private string _filePath = "";
        private string _statusText = "Ready";
        private Avalonia.Controls.Window? _window;

        private EquationsViewModel _equationsVM;
        private SolutionViewModel _solutionVM;
        private ObservableCollection<ScottPlotViewModel> _plotViewModels;
        private IntegralTableViewModel _integralTableVM;
        private DebugLogViewModel _debugLogVM;
        #endregion Fields

        #region Properties
        public string FileContent
        {
            get => _fileContent;
            set
            {
                if (SetProperty(ref _fileContent, value))
                {
                    if (EquationsVM != null && EquationsVM.EquationText != value)
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

        public IntegralTableViewModel IntegralTableVM
        {
            get => _integralTableVM;
            set => SetProperty(ref _integralTableVM, value);
        }

        public DebugLogViewModel DebugLogVM
        {
            get => _debugLogVM;
            set => SetProperty(ref _debugLogVM, value);
        }
        #endregion Properties

        #region Commands
        public ICommand OpenCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand SaveAsCommand { get; }
        public ICommand RunCommand { get; }
        public ICommand ExitCommand { get; }
        #endregion Commands

        #region Constructor
        [Log]
        public MainWindowViewModel()
        {
            _equationsVM = new EquationsViewModel();
            _solutionVM = new SolutionViewModel();
            _plotViewModels = new ObservableCollection<ScottPlotViewModel>();
            _integralTableVM = new IntegralTableViewModel();
            _debugLogVM = new DebugLogViewModel();

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
                else { Environment.Exit(0); }
            });

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
            Log.Debug("[MainWindowVM] Constructor finished.");
        }
        #endregion Constructor

        #region Public Methods
        public void SetWindow(Avalonia.Controls.Window window)
        {
            _window = window;
            StatusText = _window != null ? "Ready - Window reference set" : "Error: Window reference is null";
            Log.Debug("[MainWindowVM] SetWindow called. Window is {Status}", _window != null ? "set" : "null");
        }

        [Log]
        public void DebugSolutionTab()
        {
            Log.Debug("[MainWindowVM.DebugSolutionTab] SolutionVM has {Count} variables", SolutionVM?.Variables?.Count ?? 0);
        }
        #endregion Public Methods

        #region File Operations
        [Log]
        private async Task OpenFileAsync()
        {
            if (_window == null) { StatusText = "Error: Window for file dialog not set."; return; }
            var storageProvider = _window.StorageProvider;
            if (!storageProvider.CanOpen) { StatusText = "Error: File open not supported."; return; }

            var lseFileType = new FilePickerFileType("LibreSolvE Files (*.lse)") { Patterns = new[] { "*.lse" } };
            var result = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open LibreSolvE File",
                AllowMultiple = false,
                FileTypeFilter = new[] { lseFileType, FilePickerFileTypes.All }
            });

            if (result?.Count > 0)
            {
                var file = result[0];
                _filePath = file.TryGetLocalPath() ?? file.Name;
                try
                {
                    using var stream = await file.OpenReadAsync();
                    using var reader = new StreamReader(stream);
                    FileContent = await reader.ReadToEndAsync();
                    StatusText = $"Opened: {Path.GetFileName(_filePath)}";
                }
                catch (Exception ex) { StatusText = $"Error opening '{file.Name}': {ex.Message}"; OutputText = $"Error: {ex}"; Log.Error(ex, "Error opening file"); }
            }
        }

        [Log]
        private async Task SaveFileAsync()
        {
            if (string.IsNullOrEmpty(_filePath)) { await SaveFileAsAsync(); return; }
            try
            {
                await File.WriteAllTextAsync(_filePath, EquationsVM.EquationText);
                StatusText = $"Saved: {Path.GetFileName(_filePath)}";
            }
            catch (Exception ex) { StatusText = $"Error saving '{_filePath}': {ex.Message}"; OutputText = $"Error: {ex}"; Log.Error(ex, "Error saving file"); }
        }

        [Log]
        private async Task SaveFileAsAsync()
        {
            if (_window == null) { StatusText = "Error: Window for file dialog not set."; return; }
            var storageProvider = _window.StorageProvider;
            if (!storageProvider.CanSave) { StatusText = "Error: File save not supported."; return; }

            var lseFileType = new FilePickerFileType("LibreSolvE Files (*.lse)") { Patterns = new[] { "*.lse" } };
            var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save LibreSolvE File As",
                SuggestedFileName = Path.GetFileName(_filePath) ?? "untitled.lse",
                DefaultExtension = ".lse",
                FileTypeChoices = new[] { lseFileType }
            });

            if (file != null)
            {
                _filePath = file.TryGetLocalPath() ?? file.Name;
                try
                {
                    // Corrected: Get stream before writer, and ensure writer is disposed
                    await using var stream = await file.OpenWriteAsync(); // ensures stream is disposed
                    using var writer = new StreamWriter(stream); // ensures writer is disposed
                    await writer.WriteAsync(EquationsVM.EquationText);
                    StatusText = $"Saved as: {Path.GetFileName(_filePath)}";
                }
                catch (Exception ex) { StatusText = $"Error saving as '{file.Name}': {ex.Message}"; OutputText = $"Error: {ex}"; Log.Error(ex, "Error saving file as"); }
            }
        }
        #endregion File Operations

        #region Execution Logic
        [Log]
        private void RunFile()
        {
            StatusText = "Executing...";
            OutputText = "";

            Dispatcher.UIThread.Post(() =>
            {
                SolutionVM.Variables.Clear();
                PlotViewModels.Clear();
                IntegralTableVM.UpdateFromIntegralTable(new Dictionary<string, List<double>>());
            }, DispatcherPriority.Normal);

            string currentInputText = EquationsVM.EquationText;
            if (string.IsNullOrWhiteSpace(currentInputText))
            {
                StatusText = "No equations to execute.";
                return;
            }

            Task.Run(() =>
            {
                var outputBuilder = new StringBuilder();
                TextWriter originalConsoleOut = Console.Out;
                StringWriter capturedConsoleWriter = new StringWriter();
                StatementExecutor? executor = null;
                VariableStore? variableStore = null;
                bool solveSuccess = false;

                try
                {
                    Console.SetOut(capturedConsoleWriter);

                    variableStore = new VariableStore();
                    var functionRegistry = new FunctionRegistry();
                    var solverSettings = new SolverSettings();

                    var unitsDictionary = UnitParser.ExtractUnitsFromSource(currentInputText);
                    UnitParser.ApplyUnitsToVariableStore(variableStore, unitsDictionary);

                    AntlrInputStream inputStream = new AntlrInputStream(currentInputText);
                    EesLexer lexer = new EesLexer(inputStream);
                    CommonTokenStream commonTokenStream = new CommonTokenStream(lexer);
                    EesParser parser = new EesParser(commonTokenStream);
                    var errorListener = new BetterErrorListener();
                    parser.RemoveErrorListeners(); lexer.RemoveErrorListeners();
                    parser.AddErrorListener(errorListener); lexer.AddErrorListener(errorListener);

                    EesParser.EesFileContext parseTreeContext = parser.eesFile();
                    var astBuilder = new AstBuilderVisitor();
                    AstNode rootAstNode = astBuilder.VisitEesFile(parseTreeContext);

                    if (rootAstNode is EesFileNode fileNode)
                    {
                        executor = new StatementExecutor(variableStore, functionRegistry, solverSettings);
                        executor.PlotCreated += OnPlotCreated;
                        executor.Execute(fileNode);
                        solveSuccess = executor.SolveRemainingAlgebraicEquations();
                        // StatusText updated in finally block on UI thread
                    }
                    else
                    {
                        outputBuilder.AppendLine("AST Build Error: Root node is not EesFileNode.");
                        // StatusText updated in finally block
                        solveSuccess = false; // Mark as not successful
                    }
                }
                catch (ParsingException pEx)
                {
                    outputBuilder.AppendLine($"\n--- PARSING FAILED ---\n{pEx.Message}");
                    Log.Error(pEx, "Parsing failed in RunFile");
                    solveSuccess = false;
                }
                catch (InvalidOperationException coreEx)
                {
                    outputBuilder.AppendLine($"\n--- CORE EXECUTION ERROR ---\n{coreEx.Message}");
                    Log.Error(coreEx, "Core execution error in RunFile (InvalidOperationException)");
                    solveSuccess = false;
                }
                catch (Exception ex)
                {
                    outputBuilder.AppendLine($"\n--- UNEXPECTED ERROR ---\n{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                    Log.Error(ex, "Unexpected error in RunFile");
                    solveSuccess = false;
                }
                finally
                {
                    Console.SetOut(originalConsoleOut);
                    outputBuilder.Insert(0, capturedConsoleWriter.ToString());

                    Dispatcher.UIThread.Post(() =>
                    {
                        OutputText = outputBuilder.ToString();
                        if (variableStore != null)
                        {
                            SolutionVM.UpdateResults(variableStore);
                        }

                        if (executor != null)
                        {
                            var integralTableData = executor.GetIntegralTableData(); // CORRECTED
                            if (integralTableData != null && integralTableData.Count > 0 && integralTableData.Values.Any(v => v != null && v.Count > 0)) // CORRECTED
                            {
                                Log.Debug("[MainWindowVM.RunFile.Finally] Updating IntegralTableVM from executor.");
                                IntegralTableVM.UpdateFromIntegralTable(integralTableData);
                            }
                            else
                            {
                                Log.Debug("[MainWindowVM.RunFile.Finally] No significant integral table data from executor.");
                                IntegralTableVM.UpdateFromIntegralTable(new Dictionary<string, List<double>>());
                            }
                            executor.PlotCreated -= OnPlotCreated;
                        }
                        StatusText = solveSuccess ? "Execution complete. Results updated." : (StatusText.EndsWith("Error") || StatusText.EndsWith("Failed") || StatusText.Contains("ERROR") ? StatusText : "Execution finished with issues.");
                    }, DispatcherPriority.Normal);
                }
            });
        }

        [Log]
        private void OnPlotCreated(object? sender, PlotData plotData)
        {
            Log.Debug("[MainWindowVM.OnPlotCreated] Received PlotData: Title='{PlotTitle}'", plotData?.Settings?.Title ?? "N/A");
            if (plotData == null) return;

            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var scottPlotModel = new ScottPlot.Plot();
                    scottPlotModel.Title(plotData.Settings.Title ?? "Plot");
                    scottPlotModel.XLabel(plotData.Settings.XLabel ?? "X-Axis");
                    scottPlotModel.YLabel(plotData.Settings.YLabel ?? "Y-Axis");
                    if (plotData.Settings.ShowLegend) scottPlotModel.ShowLegend(Alignment.UpperRight);

                    if (plotData.Series?.Any() == true)
                    {
                        foreach (var seriesData in plotData.Series)
                        {
                            if (seriesData?.XValues?.Any() == true && seriesData.YValues?.Any() == true)
                            {
                                var validPoints = seriesData.XValues
                                    .Zip(seriesData.YValues, (x, y) => new { X = x, Y = y })
                                    .Where(pt => double.IsFinite(pt.X) && double.IsFinite(pt.Y))
                                    .ToList();
                                if (validPoints.Any())
                                {
                                    var scatter = scottPlotModel.Add.Scatter(
                                        validPoints.Select(p => p.X).ToArray(),
                                        validPoints.Select(p => p.Y).ToArray()
                                    );
                                    // CORRECTED: Use Count() method
                                    scatter.LegendText = seriesData.Name ?? $"Series {scottPlotModel.GetPlottables().Count()}";
                                    Log.Debug("Added Scatter plot for series '{SeriesName}' with {PointCount} valid points to ScottPlot '{PlotTitle}'", scatter.LegendText, validPoints.Count, plotData.Settings.Title ?? "Plot");
                                }
                            }
                        }
                    }
                    else
                    {
                        scottPlotModel.Add.Annotation("No data series in plot.", Alignment.MiddleCenter);
                    }
                    scottPlotModel.Axes.AutoScale();

                    var plotVM = new ScottPlotViewModel { Plot = scottPlotModel, Title = plotData.Settings.Title ?? "Plot" };
                    PlotViewModels.Add(plotVM);
                    Log.Debug("[MainWindowVM.OnPlotCreated] ScottPlotViewModel added to collection on UI thread.");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[MainWindowVM.OnPlotCreated] Error creating ScottPlot on UI thread for plot '{PlotTitle}'", plotData.Settings.Title ?? "Plot");
                    StatusText = "Error displaying plot.";
                }
            }, DispatcherPriority.Normal);
        }
        #endregion Execution Logic
    }
}
