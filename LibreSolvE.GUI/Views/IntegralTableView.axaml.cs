using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LibreSolvE.GUI.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LibreSolvE.GUI.Views
{
    public partial class IntegralTableView : UserControl
    {
        private DataGrid? _dataGrid;
        private IntegralTableViewModel? _viewModel;

        public IntegralTableView()
        {
            InitializeComponent();
            _dataGrid = this.FindControl<DataGrid>("IntegralTableGrid");

            if (_dataGrid == null)
            {
                Serilog.Log.Error("[IntegralTableView] Could not find DataGrid with name 'IntegralTableGrid'");
            }

            this.DataContextChanged += IntegralTableView_DataContextChanged;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void IntegralTableView_DataContextChanged(object? sender, EventArgs e)
        {
            Serilog.Log.Debug("[IntegralTableView] DataContextChanged fired.");

            // Clear previous subscription if any
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
                _viewModel = null;
            }

            // Check if the new DataContext is of the expected type
            if (DataContext is IntegralTableViewModel vm)
            {
                Serilog.Log.Debug("[IntegralTableView] DataContext is IntegralTableViewModel.");
                _viewModel = vm;

                // Subscribe to the new ViewModel's PropertyChanged event
                if (_viewModel != null)  // This check is redundant but helps the compiler
                {
                    _viewModel.PropertyChanged += ViewModel_PropertyChanged;
                }

                // Setup DataGrid (already contains null checks internally)
                SetupDataGrid();
            }
            else
            {
                Serilog.Log.Debug("[IntegralTableView] DataContext is NOT IntegralTableViewModel (Type: {Type}). Clearing grid.",
                    DataContext?.GetType()?.Name ?? "null");

                // Clear the DataGrid if the DataContext is not our expected type
                if (_dataGrid != null)
                {
                    _dataGrid.ItemsSource = null;
                    _dataGrid.Columns.Clear();
                }
            }
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Explicitly check 'e' first
            if (e == null)
            {
                Serilog.Log.Debug("[IntegralTableView] ViewModel PropertyChanged: EventArgs 'e' was null.");
                return;
            }

            // Assign PropertyName to a local nullable variable
            string? propertyName = e.PropertyName;

            // Check the local variable for null or empty
            if (string.IsNullOrEmpty(propertyName))
            {
                Serilog.Log.Debug("[IntegralTableView] ViewModel PropertyChanged: PropertyName was null/empty.");
                return; // Cannot proceed without a valid property name
            }

            // Now the compiler *knows* propertyName is not null here
            Serilog.Log.Debug("[IntegralTableView] ViewModel PropertyChanged: {Property}", propertyName);

            // Use the non-null local variable 'propertyName'
            if (propertyName == nameof(IntegralTableViewModel.TableData) ||
                propertyName == nameof(IntegralTableViewModel.ColumnNames) ||
                propertyName == nameof(IntegralTableViewModel.RowCount))
            {
                // Update on UI thread
                Dispatcher.UIThread.Post(() => SetupDataGrid());
            }
        }

        private void SetupDataGrid()
        {
            if (_dataGrid == null)
            {
                Serilog.Log.Error("[IntegralTableView] SetupDataGrid: _dataGrid is null!");
                return;
            }
            if (_viewModel == null)
            {
                Serilog.Log.Warning("[IntegralTableView] SetupDataGrid: _viewModel is null. Clearing grid.");
                _dataGrid.ItemsSource = null;
                _dataGrid.Columns.Clear();
                return;
            }

            Serilog.Log.Debug("[IntegralTableView] SetupDataGrid: Preparing grid. RowCount={RowCount}, Columns={ColumnCount}", _viewModel.RowCount, _viewModel.ColumnNames.Count);


            // Ensure setup happens on UI thread if necessary (though usually called from it)
            // Dispatcher.UIThread.Post(() => { ... }); // Usually not needed here if called from DCChanged/PropChanged handler

            _dataGrid.Columns.Clear(); // Clear previous columns

            if (_viewModel.ColumnNames.Count == 0 || _viewModel.RowCount == 0)
            {
                Serilog.Log.Debug("[IntegralTableView] SetupDataGrid: No columns or rows to display. Clearing ItemsSource.");
                _dataGrid.ItemsSource = null; // Ensure grid is empty
                return;
            }


            // Create data objects for each row - MODIFIED APPROACH
            var rowList = new List<Dictionary<string, object>>();
            for (int i = 0; i < _viewModel.RowCount; i++)
            {
                var rowData = new Dictionary<string, object>();
                foreach (var colName in _viewModel.ColumnNames)
                {
                    // Use TryGetValue for safety
                    rowData[colName] = _viewModel.TableData.TryGetValue(colName, out var values) && i < values.Count
                                          ? (object)values[i] // Cast to object
                                          : double.NaN;       // Use NaN or null for missing data
                }
                rowList.Add(rowData);
            }
            Serilog.Log.Debug("[IntegralTableView] SetupDataGrid: Created rowList with {Count} items.", rowList.Count);


            // Add all columns FIRST before setting ItemsSource
            foreach (var colName in _viewModel.ColumnNames)
            {
                var column = new DataGridTextColumn
                {
                    Header = colName,
                    // Binding uses dictionary key access
                    Binding = new Avalonia.Data.Binding($"[{colName}]")
                    {
                        StringFormat = "{0:F6}" // Format as 6 decimal places
                    },
                    Width = DataGridLength.SizeToCells // Adjust width automatically
                };
                _dataGrid.Columns.Add(column);
            }
            Serilog.Log.Debug("[IntegralTableView] SetupDataGrid: Added {Count} columns to DataGrid.", _dataGrid.Columns.Count);


            // Now set the ItemsSource AFTER columns are defined
            if (rowList.Count > 0)
            {
                _dataGrid.ItemsSource = null; // Try setting to null first
                _dataGrid.ItemsSource = rowList; // Set the source again
                //_dataGrid.InvalidateMeasure(); // Try invalidating measure/visuals
                //_dataGrid.InvalidateVisual();
                Serilog.Log.Debug($"[IntegralTableView] SetupDataGrid: Set ItemsSource with {rowList.Count} rows.");
            }
            else
            {
                Serilog.Log.Debug("IntegralTableView: No rows to display");
                _dataGrid.ItemsSource = null;
            }
        }
    }
}
