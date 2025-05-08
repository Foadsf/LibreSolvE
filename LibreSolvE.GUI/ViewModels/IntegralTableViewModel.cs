using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LibreSolvE.GUI.ViewModels
{
    public class IntegralTableViewModel : ViewModelBase
    {
        private Dictionary<string, List<double>> _tableData = new Dictionary<string, List<double>>();
        private ObservableCollection<string> _columnNames = new ObservableCollection<string>();
        private int _rowCount = 0;

        public Dictionary<string, List<double>> TableData
        {
            get => _tableData;
            set
            {
                if (SetProperty(ref _tableData, value))
                {
                    RefreshColumnNames();
                    UpdateRowCount();
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
            TableData = integralTable;
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

        public double GetValueAt(string columnName, int rowIndex)
        {
            if (TableData.TryGetValue(columnName, out var values) && rowIndex < values.Count)
            {
                return values[rowIndex];
            }
            return double.NaN;
        }
    }
}
