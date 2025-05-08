using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
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

            this.DataContextChanged += IntegralTableView_DataContextChanged;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void IntegralTableView_DataContextChanged(object? sender, EventArgs e)
        {
            if (DataContext is IntegralTableViewModel vm)
            {
                _viewModel = vm;
                SetupDataGrid();

                // Subscribe to property changes to update columns when needed
                _viewModel.PropertyChanged += (s, args) =>
                {
                    if (args.PropertyName == nameof(IntegralTableViewModel.TableData) ||
                        args.PropertyName == nameof(IntegralTableViewModel.ColumnNames))
                    {
                        SetupDataGrid();
                    }
                };
            }
        }

        private void SetupDataGrid()
        {
            if (_dataGrid == null || _viewModel == null || _viewModel.ColumnNames.Count == 0)
                return;

            _dataGrid.Columns.Clear();

            // Create data objects for each row
            var rowList = new List<Dictionary<string, object>>();
            for (int i = 0; i < _viewModel.RowCount; i++)
            {
                var rowData = new Dictionary<string, object>();
                foreach (var colName in _viewModel.ColumnNames)
                {
                    rowData[colName] = _viewModel.GetValueAt(colName, i);
                }
                rowList.Add(rowData);
            }

            // Set ItemsSource property (not Items)
            _dataGrid.ItemsSource = rowList;

            // Add all columns
            foreach (var colName in _viewModel.ColumnNames)
            {
                var column = new DataGridTextColumn
                {
                    Header = colName,
                    // Use binding with StringFormat instead of FormatString
                    Binding = new Avalonia.Data.Binding($"[{colName}]")
                    {
                        StringFormat = "{0:F6}" // Format as 6 decimal places
                    }
                };
                _dataGrid.Columns.Add(column);
            }
        }
    }
}
