using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System;

namespace LibreSolvE.GUI.ViewModels
{
    public class IntegralTableViewModel : ViewModelBase
    {
        private Dictionary<string, List<double>> _tableData = new Dictionary<string, List<double>>();
        private ObservableCollection<string> _columnNames = new ObservableCollection<string>();
        private int _rowCount = 0;

        // New property for direct DataGrid binding
        private ObservableCollection<Dictionary<string, object>> _tableItems = new ObservableCollection<Dictionary<string, object>>();
        public ObservableCollection<Dictionary<string, object>> TableItems
        {
            get => _tableItems;
            private set => SetProperty(ref _tableItems, value);
        }

        public Dictionary<string, List<double>> TableData
        {
            get => _tableData;
            set
            {
                if (SetProperty(ref _tableData, value))
                {
                    RefreshColumnNames();
                    UpdateRowCount();
                    UpdateTableItems(); // Add this call
                }
            }
        }

        public ObservableCollection<string> ColumnNames
        {
            get => _columnNames;
            set => SetProperty(ref _columnNames, value);
        }

        public int RowCount
        {
            get => _rowCount;
            set => SetProperty(ref _rowCount, value);
        }

        public void UpdateFromIntegralTable(Dictionary<string, List<double>> integralTable)
        {
            Serilog.Log.Debug("[IntegralTableVM] UpdateFromIntegralTable called with {Count} columns.", integralTable?.Count ?? 0);
            TableData = integralTable ?? new Dictionary<string, List<double>>(); // Assign the data
            Serilog.Log.Debug("[IntegralTableVM] TableData property set. RowCount={RowCount}, Columns={ColumnCount}", RowCount, ColumnNames.Count);
            // RefreshColumnNames(), UpdateRowCount(), and UpdateTableItems() are called automatically by the setter's logic
        }

        private void RefreshColumnNames()
        {
            ColumnNames.Clear();
            foreach (var key in TableData.Keys)
            {
                ColumnNames.Add(key);
            }
        }

        private void UpdateRowCount()
        {
            RowCount = 0;
            foreach (var values in TableData.Values)
            {
                if (values.Count > RowCount)
                    RowCount = values.Count;
            }
        }

        // New method to update the TableItems collection for direct DataGrid binding
        private void UpdateTableItems()
        {
            TableItems.Clear();

            // If there's no data, exit early
            if (RowCount == 0 || ColumnNames.Count == 0)
            {
                Serilog.Log.Debug("[IntegralTableVM] UpdateTableItems: No rows or columns, clearing TableItems.");
                return;
            }

            Serilog.Log.Debug("[IntegralTableVM] UpdateTableItems: Creating {RowCount} item dictionaries for TableItems.", RowCount);
            // Create a row dictionary for each row of data
            for (int i = 0; i < RowCount; i++)
            {
                var rowData = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase); // Use case-insensitive dictionary just in case
                foreach (var colName in ColumnNames)
                {
                    // Get the value for this column at this row, or a placeholder if out of range
                    double value = GetValueAt(colName, i); // GetValueAt uses case-insensitive TableData lookup
                    // *** Ensure the key added here EXACTLY matches the colName used in DataGrid Binding ***
                    rowData[colName] = value;
                }
                TableItems.Add(rowData);
            }

            Serilog.Log.Debug("[IntegralTableVM] TableItems updated with {Count} rows", TableItems.Count);
        }

        public double GetValueAt(string columnName, int rowIndex)
        {
            // TableData dictionary already uses OrdinalIgnoreCase comparer
            if (TableData.TryGetValue(columnName, out var values) && rowIndex >= 0 && rowIndex < values.Count)
            {
                return values[rowIndex];
            }
            Serilog.Log.Warning("[IntegralTableVM] GetValueAt: Could not find value for column '{ColumnName}' at row {RowIndex}. Returning NaN.", columnName, rowIndex);
            return double.NaN;
        }
    }
}
