using Avalonia.Controls;
using LibreSolvE.GUI.ViewModels;

namespace LibreSolvE.GUI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Set the window reference for the view model to use the storage provider
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SetWindow(this);

            // Find the PlotView control and set the reference
            if (this.FindControl<PlotView>("PlotView") is PlotView plotView)
            {
                viewModel.PlotView = plotView;
            }
        }
    }
}
