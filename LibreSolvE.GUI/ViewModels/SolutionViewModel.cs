using CommunityToolkit.Mvvm.ComponentModel;
using LibreSolvE.Core.Evaluation; // For VariableStore
using System.Collections.ObjectModel;
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
            Variables.Clear();
            if (store == null) return;

            var allVarNames = store.GetAllVariableNames().OrderBy(name => name);
            foreach (var varName in allVarNames)
            {
                string source;
                if (store.IsExplicitlySet(varName)) source = "Explicit";
                else if (store.HasVariable(varName)) source = "Solved"; // Assuming if not explicit, it's solved or from guess
                else if (store.HasGuessValue(varName)) source = "Guess";
                else source = "Default";


                Variables.Add(new VariableResultItem
                {
                    Name = varName,
                    Value = store.GetVariable(varName), // GetVariable handles default/guess internally if not solved
                    Units = store.GetUnit(varName),
                    Source = source
                });
            }
        }
    }
}
