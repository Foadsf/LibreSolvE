using CommunityToolkit.Mvvm.ComponentModel;

namespace LibreSolvE.GUI.ViewModels
{
    public class EquationsViewModel : ViewModelBase
    {
        private string _equationText = "";
        public string EquationText
        {
            get => _equationText;
            set => SetProperty(ref _equationText, value);
        }

        // Later: FormattedEquationContent, etc.
    }
}
