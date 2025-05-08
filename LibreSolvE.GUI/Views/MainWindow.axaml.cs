using Avalonia.Controls;
using Avalonia.Interactivity; // For RoutedEventArgs
using LibreSolvE.GUI.ViewModels;
using LibreSolvE.GUI.Views;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Threading;
using System.Text; // Add this for StringBuilder

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

    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is TabControl tabControl)
        {
            Serilog.Log.Debug("Tab selection changed to index: {Index}", tabControl.SelectedIndex);

            // If the Integral Table tab is selected
            if (tabControl.SelectedIndex == 3) // Adjust this index to match your Integral Table tab position
            {
                // Force redraw of content
                if (tabControl.SelectedContent is IntegralTableView integralTableView)
                {
                    Serilog.Log.Debug("Integral Table View's DataContext: {DataContext}",
                        integralTableView.DataContext?.GetType().Name ?? "null");

                    // Ensure DataContext is set
                    if (integralTableView.DataContext == null && DataContext is MainWindowViewModel vm && vm.IntegralTableVM != null)
                    {
                        Serilog.Log.Debug("Reapplying IntegralTableVM to view");
                        integralTableView.DataContext = vm.IntegralTableVM;
                    }

                    // Force refresh the grid
                    integralTableView.ForceRefreshGrid();

                    // Check grid visibility
                    integralTableView.CheckGridVisibility();
                }
            }
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
