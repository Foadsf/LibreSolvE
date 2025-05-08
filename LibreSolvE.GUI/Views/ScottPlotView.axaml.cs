using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ScottPlot; // Required for Plot type
using ScottPlot.Avalonia; // Required for AvaPlot type
using System; // Required for EventHandler

namespace LibreSolvE.GUI.Views
{
    public partial class ScottPlotView : UserControl
    {
        // Define the AvaloniaProperty for binding
        // We'll call it 'PlotSource' to avoid confusion with the internal control's 'Plot' property
        public static readonly StyledProperty<Plot?> PlotSourceProperty =
            AvaloniaProperty.Register<ScottPlotView, Plot?>(nameof(PlotSource));

        // Standard CLR property wrapper for the AvaloniaProperty
        public Plot? PlotSource
        {
            get => GetValue(PlotSourceProperty);
            set => SetValue(PlotSourceProperty, value);
        }

        // Reference to the internal AvaPlot control from XAML
        private AvaPlot? _avaPlotControl;

        public ScottPlotView()
        {
            InitializeComponent();

            // Find the control defined in XAML by its name
            _avaPlotControl = this.FindControl<AvaPlot>("AvaPlotControl");

            if (_avaPlotControl == null)
            {
                // Log or handle the error if the control isn't found
                Serilog.Log.Error("[ScottPlotView] Could not find AvaPlot control named 'AvaPlotControl' in XAML.");
                return;
            }

            // Subscribe to changes in the PlotSource property
            PlotSourceProperty.Changed.Subscribe(OnPlotSourceChanged);
        }

        // Called by Avalonia when the control is loaded
        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        // Event handler called when the PlotSource property changes (e.g., due to binding)
        private void OnPlotSourceChanged(AvaloniaPropertyChangedEventArgs<Plot?> e)
        {
            var newPlot = e.NewValue.GetValueOrDefault(); // Get the new Plot object
            UpdatePlot(newPlot);
        }

        // Helper method to update the internal AvaPlot control
        private void UpdatePlot(Plot? plot)
        {
            if (_avaPlotControl != null)
            {
                if (plot != null)
                {
                    // Use Reset to replace the plot displayed by the control
                    _avaPlotControl.Reset(plot);
                    _avaPlotControl.Refresh(); // Ensure it redraws
                                               // CHANGE THIS LINE: Get title from the 'plot' object directly or its Layout property
                                               // Simplest is to just log that we received a plot object
                    Serilog.Log.Debug("[ScottPlotView] Updated internal AvaPlot with new PlotSource (object hash: {PlotHashCode})", plot.GetHashCode());
                }
                else
                {
                    // If the source is null, maybe clear the plot or show a placeholder
                    _avaPlotControl.Reset(new Plot()); // Reset with an empty plot
                    _avaPlotControl.Plot.Add.Annotation("No Plot Data", Alignment.MiddleCenter);
                    _avaPlotControl.Refresh();
                    Serilog.Log.Warning("[ScottPlotView] PlotSource was null. Resetting internal AvaPlot.");
                }
            }
            else
            {
                Serilog.Log.Error("[ScottPlotView] Cannot update plot because internal AvaPlotControl is null.");
            }
        }

        // Optional: Handle initial DataContext change if needed, though direct property binding is usually sufficient
        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            // You could potentially update the plot here too if the binding doesn't work immediately,
            // but the PropertyChanged subscription is generally the cleaner way.
            // UpdatePlot(PlotSource);
        }


    }
}
