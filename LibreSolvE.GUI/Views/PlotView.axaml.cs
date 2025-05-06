using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using OxyPlot;
using OxyPlot.Series;
using OxyPlot.Axes;
using System.Collections.Generic;
using System;
using System.Linq;
using LibreSolvE.GUI.ViewModels;

namespace LibreSolvE.GUI.Views
{
    public partial class PlotView : UserControl
    {
        private readonly PlotViewModel _viewModel;

        public static readonly StyledProperty<PlotModel> PlotModelProperty =
            AvaloniaProperty.Register<PlotView, PlotModel>(nameof(PlotModel));

        public PlotModel PlotModel
        {
            get => GetValue(PlotModelProperty);
            set => SetValue(PlotModelProperty, value);
        }

        public PlotView()
        {
            InitializeComponent();
            _viewModel = new PlotViewModel();
            DataContext = _viewModel;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public void UpdateFromIntegralTable(
            string title,
            string xAxisName,
            Dictionary<string, List<double>> tableData,
            IEnumerable<string> yAxisNames)
        {
            if (!tableData.ContainsKey(xAxisName))
            {
                _viewModel.PlotModel = new PlotModel { Title = "Error: X-axis variable not found" };
                return;
            }

            var model = new PlotModel { Title = title };

            // Configure axes
            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Title = xAxisName,
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineStyle = LineStyle.Dot
            });

            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = string.Join(", ", yAxisNames),
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineStyle = LineStyle.Dot
            });

            // Get X values
            var xValues = tableData[xAxisName];

            // Standard colors for series
            var colors = new OxyColor[]
            {
                OxyColors.Blue,
                OxyColors.Red,
                OxyColors.Green,
                OxyColors.Orange,
                OxyColors.Purple,
                OxyColors.Teal,
                OxyColors.Brown,
                OxyColors.Magenta
            };

            int colorIndex = 0;
            foreach (var yName in yAxisNames)
            {
                if (!tableData.ContainsKey(yName))
                    continue;

                var yValues = tableData[yName];

                // Create a line series
                var series = new LineSeries
                {
                    Title = yName,
                    Color = colors[colorIndex % colors.Length],
                    StrokeThickness = 2
                };

                // Add data points
                for (int i = 0; i < Math.Min(xValues.Count, yValues.Count); i++)
                {
                    series.Points.Add(new DataPoint(xValues[i], yValues[i]));
                }

                model.Series.Add(series);
                colorIndex++;
            }

            // Update the plot model
            _viewModel.PlotModel = model;
        }
    }
}
