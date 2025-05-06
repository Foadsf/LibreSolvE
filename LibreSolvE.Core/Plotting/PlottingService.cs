using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LibreSolvE.Core.Plotting
{
    public class PlottingService
    {
        public event EventHandler<PlotData>? PlotCreated;

        public PlotData CreatePlot(string commandText, Dictionary<string, List<double>> tableData)
        {
            // Parse the PLOT command
            // Format: PLOT x, y1, y2, ... [WITH TITLE "title" XLABEL "x" YLABEL "y"]

            // Remove "PLOT" from the beginning
            string args = commandText.StartsWith("PLOT", StringComparison.OrdinalIgnoreCase)
                ? commandText.Substring(4).Trim()
                : commandText.Trim();

            // Split by WITH
            string[] parts = args.Split(new[] { "WITH" }, StringSplitOptions.RemoveEmptyEntries);
            string varPart = parts[0].Trim();

            // Parse variables
            string[] variables = varPart.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (variables.Length < 2)
            {
                throw new ArgumentException("PLOT command requires at least X and Y variables");
            }

            // First variable is X axis, others are Y axes
            string xAxisName = variables[0];
            var yAxisNames = variables.Skip(1).ToArray();

            // Create plot settings
            var settings = new PlotSettings
            {
                Title = $"Plot of {string.Join(", ", yAxisNames)} vs {xAxisName}",
                XLabel = xAxisName,
                YLabel = string.Join(", ", yAxisNames),
                ShowLegend = true,
                ShowGrid = true
            };

            // Parse optional settings
            if (parts.Length > 1)
            {
                string settingsPart = parts[1].Trim();

                // Parse TITLE
                var titleMatch = System.Text.RegularExpressions.Regex.Match(
                    settingsPart,
                    @"TITLE\s+""([^""]*)""\s*",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (titleMatch.Success)
                {
                    settings.Title = titleMatch.Groups[1].Value;
                }

                // Parse XLABEL
                var xLabelMatch = System.Text.RegularExpressions.Regex.Match(
                    settingsPart,
                    @"XLABEL\s+""([^""]*)""\s*",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (xLabelMatch.Success)
                {
                    settings.XLabel = xLabelMatch.Groups[1].Value;
                }

                // Parse YLABEL
                var yLabelMatch = System.Text.RegularExpressions.Regex.Match(
                    settingsPart,
                    @"YLABEL\s+""([^""]*)""\s*",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (yLabelMatch.Success)
                {
                    settings.YLabel = yLabelMatch.Groups[1].Value;
                }
            }

            // Create plot data
            var plotData = new PlotData
            {
                Settings = settings
            };

            // Create series from table data
            if (tableData.TryGetValue(xAxisName, out var xValues))
            {
                foreach (var yName in yAxisNames)
                {
                    if (tableData.TryGetValue(yName, out var yValues))
                    {
                        var series = new PlotSeries
                        {
                            Name = yName,
                            XValues = xValues,
                            YValues = yValues,
                            Color = GetColorForIndex(plotData.Series.Count)
                        };
                        plotData.Series.Add(series);
                    }
                }
            }

            // Raise event
            PlotCreated?.Invoke(this, plotData);

            return plotData;
        }

        private string GetColorForIndex(int index)
        {
            string[] colors = { "#1E88E5", "#FF0000", "#00C853", "#FFA000", "#6A1B9A", "#795548", "#F06292", "#00ACC1" };
            return colors[index % colors.Length];
        }

        public string CreateSvg(PlotData plotData)
        {
            StringBuilder svg = new StringBuilder();

            // SVG generation code
            // Basic parameters
            int width = 800;
            int height = 600;
            int margin = 50;

            // Start SVG document
            svg.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>");
            svg.AppendLine($"<svg width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\" xmlns=\"http://www.w3.org/2000/svg\">");

            // Plot area
            int plotX = margin;
            int plotY = margin;
            int plotWidth = width - 2 * margin;
            int plotHeight = height - 2 * margin;

            // Draw background
            svg.AppendLine($"<rect x=\"0\" y=\"0\" width=\"{width}\" height=\"{height}\" fill=\"white\"/>");
            svg.AppendLine($"<rect x=\"{plotX}\" y=\"{plotY}\" width=\"{plotWidth}\" height=\"{plotHeight}\" fill=\"#f8f9fa\" stroke=\"black\"/>");

            // Find min/max values
            if (plotData.Series.Count == 0)
            {
                // Draw error message
                svg.AppendLine($"<text x=\"{width / 2}\" y=\"{height / 2}\" text-anchor=\"middle\" font-size=\"16\">No data available for plotting</text>");
                svg.AppendLine("</svg>");
                return svg.ToString();
            }

            double xMin = plotData.Settings.XMin ?? plotData.Series.SelectMany(s => s.XValues).Min();
            double xMax = plotData.Settings.XMax ?? plotData.Series.SelectMany(s => s.XValues).Max();
            double yMin = plotData.Settings.YMin ?? plotData.Series.SelectMany(s => s.YValues).Min();
            double yMax = plotData.Settings.YMax ?? plotData.Series.SelectMany(s => s.YValues).Max();

            // Add padding
            double xPadding = (xMax - xMin) * 0.05;
            double yPadding = (yMax - yMin) * 0.05;
            xMin -= xPadding;
            xMax += xPadding;
            yMin -= yPadding;
            yMax += yPadding;

            // Draw grid if enabled
            if (plotData.Settings.ShowGrid)
            {
                // Grid lines
                svg.AppendLine("<g stroke=\"#e0e0e0\" stroke-width=\"1\">");

                // X grid lines
                for (int i = 0; i <= 10; i++)
                {
                    double x = plotX + (i / 10.0) * plotWidth;
                    svg.AppendLine($"<line x1=\"{x}\" y1=\"{plotY}\" x2=\"{x}\" y2=\"{plotY + plotHeight}\"/>");
                }

                // Y grid lines
                for (int i = 0; i <= 10; i++)
                {
                    double y = plotY + (i / 10.0) * plotHeight;
                    svg.AppendLine($"<line x1=\"{plotX}\" y1=\"{y}\" x2=\"{plotX + plotWidth}\" y2=\"{y}\"/>");
                }

                svg.AppendLine("</g>");
            }

            // Draw axes
            svg.AppendLine("<g stroke=\"black\" stroke-width=\"2\">");
            svg.AppendLine($"<line x1=\"{plotX}\" y1=\"{plotY + plotHeight}\" x2=\"{plotX + plotWidth}\" y2=\"{plotY + plotHeight}\"/>"); // X axis
            svg.AppendLine($"<line x1=\"{plotX}\" y1=\"{plotY}\" x2=\"{plotX}\" y2=\"{plotY + plotHeight}\"/>"); // Y axis
            svg.AppendLine("</g>");

            // Draw title
            svg.AppendLine($"<text x=\"{width / 2}\" y=\"{plotY - 20}\" text-anchor=\"middle\" font-size=\"16\" font-family=\"Arial\">{plotData.Settings.Title}</text>");

            // Draw axis labels
            svg.AppendLine($"<text x=\"{width / 2}\" y=\"{height - 10}\" text-anchor=\"middle\" font-family=\"Arial\">{plotData.Settings.XLabel}</text>");
            svg.AppendLine($"<text x=\"{plotX - 35}\" y=\"{plotY + plotHeight / 2}\" text-anchor=\"middle\" font-family=\"Arial\" transform=\"rotate(-90,{plotX - 35},{plotY + plotHeight / 2})\">{plotData.Settings.YLabel}</text>");

            // Draw series
            for (int s = 0; s < plotData.Series.Count; s++)
            {
                var series = plotData.Series[s];

                // Draw line
                svg.Append($"<polyline fill=\"none\" stroke=\"{series.Color}\" stroke-width=\"2\" points=\"");

                for (int i = 0; i < Math.Min(series.XValues.Count, series.YValues.Count); i++)
                {
                    // Transform data coordinates to SVG coordinates
                    double x = plotX + (series.XValues[i] - xMin) / (xMax - xMin) * plotWidth;
                    double y = plotY + plotHeight - (series.YValues[i] - yMin) / (yMax - yMin) * plotHeight;

                    svg.Append($"{x},{y} ");
                }

                svg.AppendLine("\"/>");
            }

            // Draw legend if enabled
            if (plotData.Settings.ShowLegend && plotData.Series.Count > 0)
            {
                int legendX = plotX + plotWidth - 150;
                int legendY = plotY + 20;
                int legendWidth = 130;
                int legendHeight = plotData.Series.Count * 25 + 10;

                // Legend background
                svg.AppendLine($"<rect x=\"{legendX}\" y=\"{legendY}\" width=\"{legendWidth}\" height=\"{legendHeight}\" fill=\"white\" fill-opacity=\"0.8\" stroke=\"black\"/>");

                // Legend entries
                for (int i = 0; i < plotData.Series.Count; i++)
                {
                    var series = plotData.Series[i];
                    double y = legendY + 20 + i * 25;

                    // Line sample
                    svg.AppendLine($"<line x1=\"{legendX + 10}\" y1=\"{y}\" x2=\"{legendX + 30}\" y2=\"{y}\" stroke=\"{series.Color}\" stroke-width=\"2\"/>");

                    // Series name
                    svg.AppendLine($"<text x=\"{legendX + 40}\" y=\"{y + 5}\" font-family=\"Arial\" font-size=\"12\">{series.Name}</text>");
                }
            }

            // Close SVG
            svg.AppendLine("</svg>");

            return svg.ToString();
        }

        public void SaveToSvg(PlotData plotData, string filePath)
        {
            string svg = CreateSvg(plotData);
            File.WriteAllText(filePath, svg);
        }
    }
}
