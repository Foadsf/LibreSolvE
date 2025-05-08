// LibreSolvE.GUI/ViewModels/PlotViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel; // Add this if ViewModelBase doesn't inherit from ObservableObject directly
using OxyPlot;
using Serilog; // Assuming Serilog is used for logging
using System;

namespace LibreSolvE.GUI.ViewModels
{
    // Make sure ViewModelBase inherits from ObservableObject or implements INotifyPropertyChanged
    public class PlotViewModel : ViewModelBase
    {
        // Fields
        private PlotModel? _plotModel; // Make nullable
        private string _errorMessage = ""; // Initialize to non-null

        // Properties
        public PlotModel? PlotModel // Allow PlotModel to be null
        {
            get => _plotModel;
            // Use InitializePlotModel for setting external models to ensure logging/error handling
            // This setter is primarily for internal updates from InitializePlotModel
            internal set => SetProperty(ref _plotModel, value);
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            // Make setter private as errors should be set internally
            private set => SetProperty(ref _errorMessage, value);
        }

        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

        // Add a Title property for easier binding in XAML
        // Handle potential null _plotModel
        public string Title => PlotModel?.Title ?? (HasError ? "Error" : "Plot");

        // Constructor
        public PlotViewModel()
        {
            try
            {
                // Initialize with a default empty model
                _plotModel = new PlotModel { Title = "No Data" };
                _errorMessage = ""; // Ensure initialized state has no error message
                Log.Debug("PlotViewModel: Default constructor finished.");
            }
            catch (Exception ex)
            {
                // Log error during construction and set error state
                Log.Error(ex, "Error creating default PlotModel in PlotViewModel constructor");
                _errorMessage = $"Error creating plot view: {ex.Message}";
                _plotModel = null; // Set model to null to indicate failure
            }
        }

        // Method to set or update the plot model from external data
        public void InitializePlotModel(PlotModel? model)
        {
            try
            {
                if (model == null)
                {
                    Log.Warning("InitializePlotModel called with null PlotModel. Setting to 'No Plot Data'.");
                    ErrorMessage = "No plot data provided";
                    // Create a new model instead of setting PlotModel to null directly
                    // This ensures the binding target (`oxy:PlotView Model="{Binding PlotModel}"`)
                    // always has a valid PlotModel instance.
                    PlotModel = new PlotModel { Title = "No Plot Data" };
                    return;
                }

                // Assign the new model and clear any previous error message
                PlotModel = model;
                ErrorMessage = ""; // Clear error on successful initialization
                Log.Debug($"PlotViewModel: Successfully initialized plot with title: '{model.Title}', {model.Series?.Count ?? 0} series.");
            }
            catch (Exception ex)
            {
                // Log the error during initialization
                ErrorMessage = $"Error initializing plot: {ex.Message}";
                Log.Error(ex, "Error initializing PlotModel in InitializePlotModel");

                // Create an error plot model as fallback
                PlotModel = new PlotModel { Title = "Error Loading Plot" };
            }
        }

        // Method for logging details (useful for debugging)
        public void LogPlotDetails()
        {
            if (PlotModel == null) // Check for null before accessing properties
            {
                Log.Warning("PlotViewModel.LogPlotDetails: PlotModel is currently null.");
                return;
            }

            Log.Debug($"PlotViewModel.LogPlotDetails - Title: '{PlotModel.Title}'");
            Log.Debug($"  Series Count: {PlotModel.Series?.Count ?? 0}"); // Null check for Series

            if (PlotModel.Series != null)
            {
                foreach (var series in PlotModel.Series)
                {
                    if (series == null) continue; // Skip null series

                    Log.Debug($"    Series: {series.GetType().Name}, Title: '{series.Title ?? "(unnamed)"}'");

                    if (series is OxyPlot.Series.LineSeries lineSeries)
                    {
                        Log.Debug($"      Points: {lineSeries.Points?.Count ?? 0}, Color: {lineSeries.Color}");
                    }
                    // Add checks for other series types if needed
                }
            }

            Log.Debug($"  Axes Count: {PlotModel.Axes?.Count ?? 0}"); // Null check for Axes
            if (PlotModel.Axes != null)
            {
                foreach (var axis in PlotModel.Axes)
                {
                    if (axis == null) continue; // Skip null axes
                    Log.Debug($"    Axis: {axis.GetType().Name}, Title: '{axis.Title ?? "(untitled)"}', Position: {axis.Position}");
                }
            }
        }
    }
}
