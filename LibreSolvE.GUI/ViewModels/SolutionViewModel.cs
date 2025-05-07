using CommunityToolkit.Mvvm.ComponentModel;
using LibreSolvE.Core.Evaluation; // For VariableStore
using System;
using System.Collections.Generic; // Add this for List<T>
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;

namespace LibreSolvE.GUI.ViewModels
{
    public class VariableResultItem : ObservableObject
    {
        private string _name = "";
        public string Name { get => _name; set => SetProperty(ref _name, value); }

        private double _value;
        public double Value { get => _value; set => SetProperty(ref _value, value); }

        private string _units = "";
        public string Units { get => _units; set => SetProperty(ref _units, value); }

        private string _source = ""; // e.g., "explicit", "solved", "guess"
        public string Source { get => _source; set => SetProperty(ref _source, value); }
    }

    public class SolutionViewModel : ViewModelBase
    {
        private ObservableCollection<VariableResultItem> _variables = new ObservableCollection<VariableResultItem>();
        public ObservableCollection<VariableResultItem> Variables
        {
            get => _variables;
            set => SetProperty(ref _variables, value);
        }

        public void UpdateResults(VariableStore store)
        {
            if (store == null) return;

            // Create a temporary list to hold all items before updating the observable collection
            var newItems = new List<VariableResultItem>();

            var allVarNames = store.GetAllVariableNames().OrderBy(name => name);
            foreach (var varName in allVarNames)
            {
                string source;
                if (store.IsExplicitlySet(varName)) source = "Explicit";
                else if (store.HasVariable(varName)) source = "Solved";
                else if (store.HasGuessValue(varName)) source = "Guess";
                else source = "Default";

                try
                {
                    double value = store.GetVariable(varName);
                    string units = store.GetUnit(varName);

                    newItems.Add(new VariableResultItem
                    {
                        Name = varName,
                        Value = value,
                        Units = units,
                        Source = source
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error getting value for variable {varName}: {ex.Message}");
                }
            }

            // Update the UI on the UI thread - fix the delegate reference
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Variables.Clear();
                foreach (var item in newItems)
                {
                    Variables.Add(item);
                }

                // Debug output to confirm data was added
                Debug.WriteLine($"Added {newItems.Count} variables to solution view");
            });
        }
    }
}
