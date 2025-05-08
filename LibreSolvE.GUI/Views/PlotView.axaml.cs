// LibreSolvE.GUI/Views/PlotView.axaml.cs
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
// Add only necessary usings if any remain after cleaning up methods
// using OxyPlot; // Likely not needed here anymore
// using LibreSolvE.GUI.ViewModels; // Still needed if x:DataType is used in XAML
// using System;
// using System.Collections.Generic;
// using System.Linq;

namespace LibreSolvE.GUI.Views
{
    /// <summary>
    /// Interaction logic for PlotView.xaml
    /// This UserControl is designed to display a plot.
    /// Its DataContext should be set to a PlotViewModel instance by its parent container.
    /// The actual plot rendering is handled by the OxyPlot.Avalonia.PlotView control
    /// defined within PlotView.axaml, which binds to the PlotModel property of the ViewModel.
    /// </summary>
    public partial class PlotView : UserControl
    {
        // No fields like _viewModel should be here anymore,
        // as the view purely relies on its DataContext being set correctly.

        public PlotView()
        {
            InitializeComponent();
            // The DataContext (which should be a PlotViewModel instance)
            // is expected to be set by the parent view/container using this UserControl,
            // for example, through a binding in an ItemsControl's DataTemplate
            // or via direct assignment if used standalone.
        }

        // Standard method required by Avalonia to load the XAML content.
        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        // No other methods should be present here unless they are specifically
        // for UI interaction logic confined to this view (e.g., handling a button click
        // within the PlotView itself, which is unlikely for a simple display view).
        // The UpdateFromIntegralTable method was removed as that logic now resides
        // in the MainWindowViewModel's OnPlotCreated handler.
    }
}
