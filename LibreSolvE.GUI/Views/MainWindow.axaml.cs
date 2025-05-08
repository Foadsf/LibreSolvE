// File: LibreSolvE.GUI/Views/MainWindow.axaml.cs
using Avalonia.Controls;
using Avalonia.Interactivity; // For RoutedEventArgs
using LibreSolvE.GUI.ViewModels;
using LibreSolvE.GUI.Views;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Threading;
using System.Text; // Add this for StringBuilder
using System.Linq; // For LINQ methods like .Any()
using Serilog; // For logging
using Avalonia.Controls.Presenters;

namespace LibreSolvE.GUI.Views;

public partial class MainWindow : Window
{
    // private IntegralTableViewModel? _integralTableViewModel;

    public MainWindow()
    {
        InitializeComponent();

        // Make sure DataContext is set correctly
        if (DataContext == null)
        {
            DataContext = new MainWindowViewModel();
        }

        // Pass window reference to view model
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SetWindow(this);
        }

        // Add this line for debugging
        this.Loaded += (s, e) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.DebugSolutionTab();

                // Also try to find the DataGrid directly and check its state
                var dataGrid = this.FindControl<Avalonia.Controls.DataGrid>("SolutionDataGrid");
                if (dataGrid != null)
                {
                    Serilog.Log.Debug("Found SolutionDataGrid in MainWindow");
                    Serilog.Log.Debug("DataGrid ItemsSource is {Type}",
                        dataGrid.ItemsSource?.GetType().Name ?? "null");
                }
                else
                {
                    Serilog.Log.Debug("Could not find SolutionDataGrid in MainWindow");
                }
            }
        };
    }

    private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        // Check DataGrid setup
        var dataGrid = this.FindControl<DataGrid>("SolutionDataGrid");
        if (dataGrid != null)
        {
            Debug.WriteLine("DataGrid found in MainWindow");
            if (dataGrid.ItemsSource == null)
            {
                Debug.WriteLine("Warning: DataGrid ItemsSource is null");
            }
        }
        else
        {
            Debug.WriteLine("Warning: Could not find DataGrid in MainWindow");
        }

        // Check ViewModel setup
        if (DataContext is MainWindowViewModel vm)
        {
            Debug.WriteLine($"MainWindowViewModel is set. SolutionVM has {vm.SolutionVM?.Variables?.Count ?? 0} items");
        }
    }

    // Add this to ensure window reference is set even if DataContext changes
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // Check if we have a new DataContext and set the window reference
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SetWindow(this);
        }
    }

    // File: LibreSolvE.GUI/Views/MainWindow.axaml.cs
    // Method: TabControl_SelectionChanged
    // Add using System; if not already present at the top

    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Keep using Serilog for general flow logging
        if (sender is TabControl tabControl && tabControl.SelectedItem is TabItem selectedTabItem)
        {
            string? headerText = selectedTabItem.Header?.ToString();
            Serilog.Log.Debug("TabControl_SelectionChanged: START. Selected Tab: {HeaderText} (Index: {Index})", headerText ?? "Unknown", tabControl.SelectedIndex);

            if (DataContext is MainWindowViewModel vm)
            {
                if (headerText == "Plots")
                {
                    Serilog.Log.Debug("Plots tab selected. PlotViewModels count: {Count}", vm.PlotViewModels.Count);
                    if (vm.PlotViewModels.Any())
                    {
                        Serilog.Log.Debug("Plots tab: PlotViewModels collection is NOT empty.");
                        var firstPlotVM = vm.PlotViewModels.First();
                        Serilog.Log.Debug("Plots tab: Logging details for the first ScottPlotViewModel directly.");
                        // We removed LogPlotDetails, so just log the title maybe
                        Serilog.Log.Debug("Plots tab: First ScottPlotViewModel Title: {Title}", firstPlotVM.Title);

                        // REMOVE or COMMENT OUT the Dispatcher.UIThread.Post block
                        // as it references old/deleted types and isn't needed for basic functionality.
                        /*
                        Dispatcher.UIThread.Post(async () => {
                            Console.WriteLine("[STDOUT_DEBUG] Plots tab: Dispatcher.UIThread.Post - STARTING visual tree inspection.");
                            // ... (rest of the removed block) ...
                            Console.WriteLine("[STDOUT_DEBUG] Plots tab: Dispatcher.UIThread.Post - FINISHED visual tree inspection (successful execution).");
                        }, DispatcherPriority.Background);
                        */
                    }
                    else
                    {
                        Serilog.Log.Debug("Plots tab: PlotViewModels collection IS EMPTY.");
                    }
                }
                // Keep using Serilog for other tabs
                else if (headerText == "Integral Table")
                {
                    // ... (Integral Table logic using Serilog remains the same) ...
                    Serilog.Log.Debug("Integral Table tab selected.");
                    if (selectedTabItem.Content is IntegralTableView integralTableView)
                    {
                        Serilog.Log.Debug("Integral Table View's DataContext: {DataContext}", integralTableView.DataContext?.GetType().Name ?? "null");
                        if (integralTableView.DataContext == null && vm.IntegralTableVM != null)
                        {
                            Serilog.Log.Debug("Reapplying IntegralTableVM to IntegralTableView.");
                            integralTableView.DataContext = vm.IntegralTableVM;
                        }
                        integralTableView.ForceRefreshGrid();
                        integralTableView.CheckGridVisibility();
                    }
                    else
                    {
                        Serilog.Log.Warning("Integral Table tab selected, but content is not IntegralTableView. Content type: {ContentType}", selectedTabItem.Content?.GetType().Name ?? "null");
                    }
                }
            }
            else
            {
                Serilog.Log.Warning("TabControl_SelectionChanged: DataContext is not MainWindowViewModel. Actual DC: {DataContextType}", DataContext?.GetType().Name ?? "null");
            }
            Serilog.Log.Debug("TabControl_SelectionChanged: END for Tab: {HeaderText}", headerText ?? "Unknown");
        }
        else if (sender is TabControl tc)
        {
            Serilog.Log.Warning("TabControl_SelectionChanged: SelectedItem is not TabItem or is null. SelectedItem Type: {Type}", tc.SelectedItem?.GetType().Name ?? "null");
        }
    }



    /*
    private void DisplayDataManually()
    {
        var textBlock = this.FindControl<TextBlock>("ManualDataText");
        if (textBlock == null || _viewModel == null || _viewModel.RowCount == 0 || _viewModel.ColumnNames.Count == 0)
        {
            return;
        }

        StringBuilder text = new StringBuilder();
        text.AppendLine($"Manual data display (first 20 rows):");

        // Add header
        text.Append("Row | ");
        foreach (var column in _viewModel.ColumnNames)
        {
            text.Append($"{column} | ");
        }
        text.AppendLine();

        // Add separator
        text.Append("-----|");
        foreach (var _ in _viewModel.ColumnNames)
        {
            text.Append("------|");
        }
        text.AppendLine();

        // Add data rows (limit to 20)
        int rowsToShow = Math.Min(_viewModel.RowCount, 20);
        for (int i = 0; i < rowsToShow; i++)
        {
            text.Append($"{i + 1,4} | ");
            foreach (var column in _viewModel.ColumnNames)
            {
                double value = _viewModel.GetValueAt(column, i);
                text.Append($"{value,6:F3} | ");
            }
            text.AppendLine();
        }

        textBlock.Text = text.ToString();
    }
    */
}
