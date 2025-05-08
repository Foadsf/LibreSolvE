using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LibreSolvE.GUI.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.ObjectModel; // Add this for ObservableCollection

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
                Serilog.Log.Debug("[IntegralTableView] DataContext is NOT IntegralTableViewModel (Type: {Type}). Preserving grid.",
                    DataContext?.GetType()?.Name ?? "null");
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
            DisplayDataManually();
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

            Serilog.Log.Debug("[IntegralTableView] SetupDataGrid: Preparing grid. RowCount={RowCount}, Columns={ColumnCount}",
                             _viewModel.RowCount, _viewModel.ColumnNames.Count);

            if (_viewModel.ColumnNames.Count == 0 || _viewModel.RowCount == 0)
            {
                Serilog.Log.Debug("[IntegralTableView] SetupDataGrid: No columns or rows to display. Clearing grid.");
                _dataGrid.ItemsSource = null;
                _dataGrid.Columns.Clear();
                return;
            }

            // Execute this UI update on the UI thread for safety
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    // Clear all previous columns first
                    _dataGrid.Columns.Clear();

                    // Temporarily set ItemsSource to null to avoid binding issues during column setup
                    _dataGrid.ItemsSource = null;

                    // Create columns with specified properties
                    foreach (var colName in _viewModel.ColumnNames)
                    {
                        var column = new DataGridTextColumn
                        {
                            Header = colName,
                            Width = DataGridLength.Auto, // Use auto sizing
                            MinWidth = 80,
                            // Binding uses dictionary key access
                            Binding = new Avalonia.Data.Binding($"[{colName}]")
                            {
                                StringFormat = "{0:F4}" // Format as 4 decimal places
                            }
                        };
                        _dataGrid.Columns.Add(column);
                    }
                    Serilog.Log.Debug("[IntegralTableView] SetupDataGrid: Added {Count} columns to DataGrid.", _dataGrid.Columns.Count);

                    // Set the ItemsSource AFTER all columns are defined
                    if (_viewModel.TableItems != null && _viewModel.TableItems.Count > 0)
                    {
                        // Make a copy to prevent modification issues
                        var items = new ObservableCollection<Dictionary<string, object>>();
                        foreach (var item in _viewModel.TableItems)
                        {
                            items.Add(new Dictionary<string, object>(item));
                        }

                        _dataGrid.ItemsSource = items;

                        // Log first row to verify data
                        if (items.Count > 0)
                        {
                            var firstItem = items[0];
                            Serilog.Log.Debug("[IntegralTableView] First row data: {First}",
                                string.Join(", ", firstItem.Select(kv => $"{kv.Key}={kv.Value}")));
                        }

                        Serilog.Log.Debug($"[IntegralTableView] SetupDataGrid: Set ItemsSource with {items.Count} rows from TableItems property.");
                    }
                    else
                    {
                        Serilog.Log.Debug("IntegralTableView: No TableItems rows to display");
                    }

                    // Force visual update
                    _dataGrid.InvalidateVisual();
                    _dataGrid.InvalidateMeasure();
                    _dataGrid.UpdateLayout();
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "[IntegralTableView] Error setting up DataGrid in UI thread");
                }
            });

            DisplayDataManually();
        }

        // Public method for MainWindow to call
        public void ForceRefreshGrid()
        {
            SetupDataGrid();
        }

        public void CheckGridVisibility()
        {
            Serilog.Log.Debug("[IntegralTableView] Grid visibility check:");

            if (_dataGrid != null)
            {
                Serilog.Log.Debug("  DataGrid exists, IsVisible={IsVisible}, Opacity={Opacity}, " +
                    "Width={Width}, Height={Height}, ItemsSource={HasItems}",
                    _dataGrid.IsVisible,
                    _dataGrid.Opacity,
                    _dataGrid.Width,
                    _dataGrid.Height,
                    _dataGrid.ItemsSource != null);

                if (_dataGrid.ItemsSource != null)
                {
                    // Try to get count from different collection types
                    int count = 0;
                    if (_dataGrid.ItemsSource is System.Collections.ICollection collection)
                    {
                        count = collection.Count;
                    }
                    else if (_dataGrid.ItemsSource is System.Collections.IEnumerable enumerable)
                    {
                        count = enumerable.Cast<object>().Count();
                    }

                    Serilog.Log.Debug("  ItemsSource has approximately {Count} items", count);
                }

                Serilog.Log.Debug("  Columns: {Count}", _dataGrid.Columns?.Count ?? 0);
            }
            else
            {
                Serilog.Log.Debug("  DataGrid is null");
            }

            Serilog.Log.Debug("  UserControl.IsVisible={IsVisible}, Opacity={Opacity}, " +
                "Width={Width}, Height={Height}",
                this.IsVisible,
                this.Opacity,
                this.Width,
                this.Height);
        }

        public void DisplayDataManually()
        {
            var textBlock = this.FindControl<TextBlock>("ManualDataText");
            if (textBlock == null)
            {
                Serilog.Log.Debug("[IntegralTableView] ManualDataText TextBlock not found");
                return;
            }

            if (_viewModel == null || _viewModel.RowCount == 0 || _viewModel.ColumnNames.Count == 0)
            {
                textBlock.Text = "No data available";
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
            Serilog.Log.Debug("[IntegralTableView] Manual data display updated with {Count} rows", rowsToShow);
        }
    }
}
