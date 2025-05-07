using Avalonia.Controls;
using Avalonia.Interactivity; // Add this for RoutedEventArgs
using LibreSolvE.GUI.ViewModels;
using System;
using System.Diagnostics;

namespace LibreSolvE.GUI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Fix: This needs to be done after the component is initialized
        // and we need to ensure the DataContext is set
        if (DataContext == null)
        {
            DataContext = new MainWindowViewModel();
        }

        // Pass the window reference to the view model
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SetWindow(this);
        }

        // Add Loaded event handler instead of overriding OnLoaded
        this.Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        // Check DataGrid setup
        var dataGrid = this.FindControl<DataGrid>("SolutionDataGrid");
        if (dataGrid != null)
        {
            Debug.WriteLine("DataGrid found in MainWindow");
            if (dataGrid.ItemsSource == null)
            {
                Debug.WriteLine("Warning: DataGrid ItemsSource is null");
            }
        }
        else
        {
            Debug.WriteLine("Warning: Could not find DataGrid in MainWindow");
        }

        // Check ViewModel setup
        if (DataContext is MainWindowViewModel vm)
        {
            Debug.WriteLine($"MainWindowViewModel is set. SolutionVM has {vm.SolutionVM?.Variables?.Count ?? 0} items");
        }
    }

    // Add this to ensure window reference is set even if DataContext changes
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // Check if we have a new DataContext and set the window reference
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SetWindow(this);
        }
    }
}
