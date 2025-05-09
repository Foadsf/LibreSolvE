// File: LibreSolvE.GUI/Views/IntegralTableView.axaml.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data; // Needed for Binding class
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LibreSolvE.GUI.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks; // For Task.Delay

// using Serilog;

namespace LibreSolvE.GUI.Views
{
    public partial class IntegralTableView : UserControl
    {
        private DataGrid? _dataGrid;
        // Keep track if columns have been generated
        private bool _columnsGenerated = false;

        public IntegralTableView()
        {
            InitializeComponent();
            _dataGrid = this.FindControl<DataGrid>("IntegralTableGrid");

            if (_dataGrid == null)
            {
                Serilog.Log.Error("[IntegralTableView] Could not find DataGrid with name 'IntegralTableGrid'");
            }

            // Handle DataContext changes to potentially regenerate columns/set items
            this.DataContextChanged += (sender, args) =>
            {
                Serilog.Log.Debug("[IntegralTableView] DataContextChanged. New type: {Type}", this.DataContext?.GetType().Name ?? "null");
                // Reset flag when DataContext changes, assuming new data might have different columns
                _columnsGenerated = false;
                // Trigger an update when the DataContext is set (or becomes null)
                // Use InvokeAsync to ensure it happens after current layout pass potentially
                Dispatcher.UIThread.InvokeAsync(UpdateGridColumnsAndItemsSource, DispatcherPriority.Background);
            };
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        // Renamed and refactored method
        private async void UpdateGridColumnsAndItemsSource()
        {
            if (!Dispatcher.UIThread.CheckAccess()) // Should already be on UI thread due to InvokeAsync
            {
                Serilog.Log.Warning("[IntegralTableView] UpdateGridColumnsAndItemsSource unexpectedly called from non-UI thread.");
                await Dispatcher.UIThread.InvokeAsync(UpdateGridColumnsAndItemsSource, DispatcherPriority.Background);
                return;
            }

            if (_dataGrid == null) { Serilog.Log.Error("[IntegralTableView] UpdateGridColumnsAndItemsSource: _dataGrid is null!"); return; }

            var vm = DataContext as IntegralTableViewModel; // Use safe cast

            // Detach ItemsSource before modifying columns to prevent potential issues
            var currentItemsSource = _dataGrid.ItemsSource; // Store current source
            if (currentItemsSource != null)
            {
                _dataGrid.ItemsSource = null;
                Serilog.Log.Debug("[IntegralTableView] UpdateGridColumnsAndItemsSource: Detached ItemsSource.");
            }

            // Only clear columns if they haven't been generated or if ViewModel is invalid
            if (!_columnsGenerated || vm == null || vm.ColumnNames == null || vm.ColumnNames.Count == 0)
            {
                _dataGrid.Columns.Clear();
                _columnsGenerated = false; // Reset flag if clearing
                Serilog.Log.Debug("[IntegralTableView] UpdateGridColumnsAndItemsSource: Cleared Columns. ColumnsGenerated set to false.");
            }


            if (vm != null && vm.ColumnNames != null && vm.ColumnNames.Count > 0 && vm.TableItems != null)
            {
                Serilog.Log.Debug("[IntegralTableView] UpdateGridColumnsAndItemsSource: ViewModel valid. Columns: {Cols}, Items: {Items}. ColumnsGenerated: {Generated}",
                   vm.ColumnNames.Count, vm.TableItems.Count, _columnsGenerated);

                // Generate Columns ONLY IF they haven't been generated yet
                if (!_columnsGenerated)
                {
                    foreach (var colName in vm.ColumnNames)
                    {
                        var column = new DataGridTextColumn
                        {
                            Header = colName,
                            Binding = new Binding($"[{colName}]") { StringFormat = "{0:F6}" },
                            Width = DataGridLength.Auto,
                            MinWidth = 80
                        };
                        _dataGrid.Columns.Add(column);
                    }
                    _columnsGenerated = true; // Set the flag AFTER adding columns
                    Serilog.Log.Debug("[IntegralTableView] UpdateGridColumnsAndItemsSource: Generated {Count} new columns.", _dataGrid.Columns.Count);
                }
                else
                {
                    Serilog.Log.Debug("[IntegralTableView] UpdateGridColumnsAndItemsSource: Columns already generated ({Count}), skipping regeneration.", _dataGrid.Columns.Count);
                }

                // --- Introduce a small delay --- (Keep this experimental delay)
                await Task.Delay(50);
                Serilog.Log.Debug("[IntegralTableView] UpdateGridColumnsAndItemsSource: Delay completed.");


                // Now set the ItemsSource if it's different or was null
                if (_dataGrid.ItemsSource != vm.TableItems) // Check if update is needed
                {
                    if (vm.TableItems.Count > 0)
                    {
                        _dataGrid.ItemsSource = vm.TableItems;
                        Serilog.Log.Debug("[IntegralTableView] UpdateGridColumnsAndItemsSource: Set ItemsSource ({Count} items).", vm.TableItems.Count);
                    }
                    else
                    {
                        // If the new source is empty, ensure ItemsSource is null
                        _dataGrid.ItemsSource = null;
                        Serilog.Log.Debug("[IntegralTableView] UpdateGridColumnsAndItemsSource: ViewModel has no items. ItemsSource set to null.");
                    }
                }
                else
                {
                    Serilog.Log.Debug("[IntegralTableView] UpdateGridColumnsAndItemsSource: ItemsSource is already set to the correct collection.");
                }
            }
            else
            {
                Serilog.Log.Warning("[IntegralTableView] UpdateGridColumnsAndItemsSource: ViewModel is null/invalid or has no columns/items. Grid cleared.");
                // Grid columns already cleared if needed, ensure ItemsSource is null
                if (_dataGrid.ItemsSource != null) _dataGrid.ItemsSource = null;
            }
        }


        public void ForceRefreshGrid()
        {
            Serilog.Log.Debug("[IntegralTableView] ForceRefreshGrid called. Triggering UpdateGridColumnsAndItemsSource.");
            // Use InvokeAsync here as well
            Dispatcher.UIThread.InvokeAsync(UpdateGridColumnsAndItemsSource, DispatcherPriority.Background);
        }

        public void CheckGridVisibility()
        {
            Serilog.Log.Debug("[IntegralTableView] Grid visibility check:");

            if (_dataGrid != null)
            {
                Serilog.Log.Debug("  DataGrid exists, IsVisible={IsVisible}, Opacity={Opacity}, " +
                    "Bounds W={W:F1} H={H:F1}, ItemsSource={HasItems}", // Use Bounds
                    _dataGrid.IsVisible,
                    _dataGrid.Opacity,
                    _dataGrid.Bounds.Width,
                    _dataGrid.Bounds.Height,
                    _dataGrid.ItemsSource != null);

                if (_dataGrid.ItemsSource != null)
                {
                    int count = 0;
                    if (_dataGrid.ItemsSource is System.Collections.ICollection collection) count = collection.Count;
                    else if (_dataGrid.ItemsSource is System.Collections.IEnumerable enumerable) count = enumerable.Cast<object>().Count();
                    Serilog.Log.Debug("  ItemsSource has approximately {Count} items", count);
                }

                Serilog.Log.Debug("  Columns Count: {Count}", _dataGrid.Columns?.Count ?? 0);
                if (_dataGrid.Columns?.Any() == true)
                {
                    Serilog.Log.Debug("  First Column Header: {Header}, Binding Path: {Path}", _dataGrid.Columns[0].Header, (_dataGrid.Columns[0] as DataGridBoundColumn)?.Binding?.ToString() ?? "N/A");
                }
            }
            else
            {
                Serilog.Log.Debug("  DataGrid is null");
            }

            Serilog.Log.Debug("  UserControl.IsVisible={IsVisible}, Opacity={Opacity}, " +
                "Bounds W={W:F1} H={H:F1}", // Use Bounds
                this.IsVisible,
                this.Opacity,
                this.Bounds.Width,
                this.Bounds.Height);
        }
    }
}
