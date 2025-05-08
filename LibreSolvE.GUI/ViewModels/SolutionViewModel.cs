// LibreSolvE.GUI/ViewModels/SolutionViewModel.cs

using Avalonia.Threading; // Add this for Dispatcher
using CommunityToolkit.Mvvm.ComponentModel;
using LibreSolvE.Core.Evaluation; // For VariableStore
using System;
using System.Collections.Generic; // Add this for List<T>
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
// Make sure Serilog is imported if you use its Log calls directly
using Serilog;

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
            // Log entry point using Serilog
            Log.Debug("[SolutionViewModel.UpdateResults] Method called.");

            if (store == null)
            {
                Log.Debug("[SolutionViewModel.UpdateResults] Input VariableStore is null. Clearing variables.");
                // Ensure clear happens on UI thread if needed
                Dispatcher.UIThread.Post(() => Variables.Clear());
                return;
            }

            var varNames = store.GetAllVariableNames().ToList(); // Get names once
            Log.Debug($"[SolutionViewModel.UpdateResults] Store contains {varNames.Count} variables: {string.Join(", ", varNames)}");

            var newItems = new List<VariableResultItem>();
            foreach (var varName in varNames.OrderBy(name => name)) // Order them
            {
                try
                {
                    string source;
                    // Order matters: Explicit > Solved > Calculated/Default/Guess
                    if (store.IsExplicitlySet(varName)) source = "Explicit";
                    else if (store.IsSolvedSet(varName)) source = "Solved"; // Check solved status
                    else if (store.HasVariable(varName)) source = "Calculated"; // It has a value but wasn't explicit or solved
                    // Below states might not be reached if HasVariable is true, but included for completeness
                    else if (store.HasGuessValue(varName)) source = "Guess";
                    else source = "Default";

                    double value = store.GetVariable(varName);
                    string units = store.GetUnit(varName); // Get the potentially preserved unit

                    newItems.Add(new VariableResultItem
                    {
                        Name = varName,
                        Value = value,
                        Units = string.IsNullOrEmpty(units) ? "-" : units, // Add a dash for empty units
                        Source = source
                    });
                    Log.Debug($"[SolutionViewModel.UpdateResults] Prepared item: {varName}={value} [{units}] ({source})");
                }
                catch (Exception ex)
                {
                    Log.Error($"[SolutionViewModel.UpdateResults] Error getting value/unit for variable {varName}: {ex.Message}");
                    // Optionally add an error item to the list
                    newItems.Add(new VariableResultItem { Name = varName, Units = "Error", Source = ex.Message });
                }
            }
            Log.Debug($"[SolutionViewModel.UpdateResults] Prepared temporary list with {newItems.Count} items.");

            // --- UI Thread Update ---
            // Use Dispatcher.UIThread.Post for fire-and-forget update
            Dispatcher.UIThread.Post(() =>
            {
                Log.Debug($"[SolutionViewModel.UpdateResults] Executing on UI thread. Current Variables count BEFORE Clear: {Variables.Count}");
                try
                {
                    Variables.Clear(); // Clear the existing collection bound to the UI
                    Log.Debug($"[SolutionViewModel.UpdateResults] Variables collection cleared on UI thread.");
                    foreach (var item in newItems)
                    {
                        Variables.Add(item); // Add the newly created items
                    }
                    Log.Debug($"[SolutionViewModel.UpdateResults] Finished updating Variables collection on UI thread. New count AFTER Add: {Variables.Count}");
                }
                catch (Exception uiEx)
                {
                    // Log any errors during the UI update itself
                    Log.Error(uiEx, "[SolutionViewModel.UpdateResults] *** ERROR updating ObservableCollection on UI thread ***");
                }
            }, DispatcherPriority.Background); // Use Background priority as it's just updating display data
        }

        // Add this method back if needed for direct debugging from MainWindow code-behind
        public void DebugDataGridBinding(Avalonia.Controls.DataGrid dataGrid)
        {
            if (dataGrid == null)
            {
                Log.Debug("DebugDataGridBinding: DataGrid is null!");
                return;
            }

            Log.Debug("DataGrid ItemsSource Type: {Type}", dataGrid.ItemsSource?.GetType().Name ?? "null");

            if (dataGrid.ItemsSource is ObservableCollection<VariableResultItem> items)
            {
                Log.Debug("DataGrid ItemsSource has {Count} items", items.Count);
                foreach (var item in items)
                {
                    Log.Debug("DataGrid Item: {Name} = {Value} {Units} ({Source})",
                        item.Name, item.Value, item.Units, item.Source);
                }
            }
            else if (dataGrid.ItemsSource != null)
            {
                Log.Warning("DataGrid ItemsSource is not an ObservableCollection<VariableResultItem>. Type: {Type}", dataGrid.ItemsSource.GetType().FullName);
            }
            else
            {
                Log.Warning("DataGrid ItemsSource is null.");
            }
        }

        // Add this method back if needed for debugging item properties
        public void LogItemProperties()
        {
            Dispatcher.UIThread.Post(() => // Ensure access happens on UI thread if called from background
            {
                if (Variables.Count > 0)
                {
                    var item = Variables[0]; // Just check the first item
                    Log.Debug("First VariableResultItem Properties in Collection:");
                    Log.Debug("  Name: '{Name}'", item.Name);
                    Log.Debug("  Value: {Value}", item.Value);
                    Log.Debug("  Units: '{Units}'", item.Units);
                    Log.Debug("  Source: '{Source}'", item.Source);
                    Log.Debug("  Name is null or empty: {IsEmpty}", string.IsNullOrEmpty(item.Name));
                    Log.Debug("  Units is null or empty: {IsEmpty}", string.IsNullOrEmpty(item.Units));
                    Log.Debug("  Source is null or empty: {IsEmpty}", string.IsNullOrEmpty(item.Source));
                }
                else
                {
                    Log.Debug("No items in Variables collection to inspect");
                }
            });
        }
    }
}
