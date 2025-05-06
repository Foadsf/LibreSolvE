using OxyPlot;

namespace LibreSolvE.GUI.ViewModels
{
    public class PlotViewModel : ViewModelBase
    {
        private PlotModel _plotModel;

        public PlotModel PlotModel
        {
            get => _plotModel;
            set => SetProperty(ref _plotModel, value);
        }

        public PlotViewModel()
        {
            _plotModel = new PlotModel { Title = "No Data" };
        }
    }
}
