using CommunityToolkit.Mvvm.ComponentModel;
using LibreSolvE.Core.Evaluation; // For VariableStore
using System;
using System.Collections.Generic; // Add this for List<T>
using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
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
            // Log entry point
            Debug.WriteLine("[SolutionViewModel.UpdateResults] Method called.");

            if (store == null)
            {
                Debug.WriteLine("[SolutionViewModel.UpdateResults] Input VariableStore is null. Clearing variables.");
                // Ensure clear happens on UI thread if needed
                Dispatcher.UIThread.Post(() => Variables.Clear());
                return;
            }

            var varNames = store.GetAllVariableNames().ToList(); // Get names once
            Debug.WriteLine($"[SolutionViewModel.UpdateResults] Store contains {varNames.Count} variables: {string.Join(", ", varNames)}");

            var newItems = new List<VariableResultItem>();
            foreach (var varName in varNames.OrderBy(name => name)) // Order them
            {
                try
                {
                    string source;
                    if (store.IsExplicitlySet(varName)) source = "Explicit";
                    else if (store.HasVariable(varName)) source = "Solved/Calculated"; // Changed terminology slightly
                    else if (store.HasGuessValue(varName)) source = "Guess";
                    else source = "Default";

                    double value = store.GetVariable(varName); // GetVariable handles default/guess internally
                    string units = store.GetUnit(varName);

                    newItems.Add(new VariableResultItem
                    {
                        Name = varName,
                        Value = value,
                        Units = units,
                        Source = source
                    });
                    // Log each item prepared
                    // Debug.WriteLine($"[SolutionViewModel.UpdateResults] Prepared item: {varName}={value} [{units}] ({source})");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SolutionViewModel.UpdateResults] Error getting value/unit for variable {varName}: {ex.Message}");
                    // Optionally add an error item to the list
                    newItems.Add(new VariableResultItem { Name = varName, Units = "Error", Source = ex.Message });
                }
            }
            Debug.WriteLine($"[SolutionViewModel.UpdateResults] Prepared temporary list with {newItems.Count} items.");

            // --- UI Thread Update ---
            Dispatcher.UIThread.Post(() =>
            {
                Debug.WriteLine($"[SolutionViewModel.UpdateResults] Executing on UI thread. Current Variables count BEFORE Clear: {Variables.Count}");
                try
                {
                    Variables.Clear();
                    Debug.WriteLine($"[SolutionViewModel.UpdateResults] Variables collection cleared on UI thread.");
                    foreach (var item in newItems)
                    {
                        Variables.Add(item);
                    }
                    Debug.WriteLine($"[SolutionViewModel.UpdateResults] Finished updating Variables collection on UI thread. New count AFTER Add: {Variables.Count}");
                }
                catch (Exception uiEx)
                {
                    Debug.WriteLine($"[SolutionViewModel.UpdateResults] *** ERROR updating ObservableCollection on UI thread: {uiEx.Message} ***");
                }
            }, DispatcherPriority.Background);
        }
    }
}
