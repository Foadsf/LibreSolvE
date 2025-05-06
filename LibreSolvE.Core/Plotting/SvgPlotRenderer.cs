// LibreSolvE.Core/Plotting/SvgPlotRenderer.cs
using System.IO;
using System.Text;

namespace LibreSolvE.Core.Plotting;

public class SvgPlotRenderer
{
    private const int Width = 800;
    private const int Height = 600;
    private const int Margin = 50;

    public string RenderToSvg(PlotData plotData)
    {
        StringBuilder svg = new StringBuilder();

        // Start SVG document
        svg.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>");
        svg.AppendLine($"<svg width=\"{Width}\" height=\"{Height}\" viewBox=\"0 0 {Width} {Height}\" xmlns=\"http://www.w3.org/2000/svg\">");

        // Add title
        svg.AppendLine($"  <title>{plotData.Settings.Title}</title>");

        // Draw plot background
        svg.AppendLine($"  <rect x=\"0\" y=\"0\" width=\"{Width}\" height=\"{Height}\" fill=\"white\"/>");

        // Calculate plot area
        int plotX = Margin;
        int plotY = Margin;
        int plotWidth = Width - 2 * Margin;
        int plotHeight = Height - 2 * Margin;

        // Draw plot area
        svg.AppendLine($"  <rect x=\"{plotX}\" y=\"{plotY}\" width=\"{plotWidth}\" height=\"{plotHeight}\" fill=\"#F8F9FA\" stroke=\"#333333\"/>");

        // Find data min/max values
        double xMin = plotData.Settings.XMin ?? plotData.Series.SelectMany(s => s.XValues).Min();
        double xMax = plotData.Settings.XMax ?? plotData.Series.SelectMany(s => s.XValues).Max();
        double yMin = plotData.Settings.YMin ?? plotData.Series.SelectMany(s => s.YValues).Min();
        double yMax = plotData.Settings.YMax ?? plotData.Series.SelectMany(s => s.YValues).Max();

        // Add padding to min/max values
        double xPadding = (xMax - xMin) * 0.05;
        double yPadding = (yMax - yMin) * 0.05;
        xMin -= xPadding;
        xMax += xPadding;
        yMin -= yPadding;
        yMax += yPadding;

        // Draw grid if enabled
        if (plotData.Settings.ShowGrid)
        {
            // Code to draw grid lines
            // ...
        }

        // Draw axes
        svg.AppendLine($"  <line x1=\"{plotX}\" y1=\"{plotY + plotHeight}\" x2=\"{plotX + plotWidth}\" y2=\"{plotY + plotHeight}\" stroke=\"#333333\"/>");
        svg.AppendLine($"  <line x1=\"{plotX}\" y1=\"{plotY}\" x2=\"{plotX}\" y2=\"{plotY + plotHeight}\" stroke=\"#333333\"/>");

        // Draw axes labels
        svg.AppendLine($"  <text x=\"{plotX + plotWidth / 2}\" y=\"{plotY + plotHeight + 35}\" text-anchor=\"middle\">{plotData.Settings.XLabel}</text>");
        svg.AppendLine($"  <text x=\"{plotX - 35}\" y=\"{plotY + plotHeight / 2}\" text-anchor=\"middle\" transform=\"rotate(-90,{plotX - 35},{plotY + plotHeight / 2})\">{plotData.Settings.YLabel}</text>");

        // Draw title
        svg.AppendLine($"  <text x=\"{plotX + plotWidth / 2}\" y=\"{plotY - 10}\" text-anchor=\"middle\" font-size=\"16\">{plotData.Settings.Title}</text>");

        // Plot each series
        int seriesIndex = 0;
        foreach (var series in plotData.Series)
        {
            // Prepare path data for the series
            StringBuilder pathData = new StringBuilder();

            for (int i = 0; i < series.XValues.Count; i++)
            {
                // Transform data coordinates to SVG coordinates
                double x = plotX + (series.XValues[i] - xMin) / (xMax - xMin) * plotWidth;
                double y = plotY + plotHeight - (series.YValues[i] - yMin) / (yMax - yMin) * plotHeight;

                if (i == 0)
                    pathData.Append($"M {x} {y}");
                else
                    pathData.Append($" L {x} {y}");

                // Add markers if specified
                if (series.MarkerStyle != "none")
                {
                    // Draw marker based on style (circle, square, etc.)
                    // ...
                }
            }

            // Draw the line
            svg.AppendLine($"  <path d=\"{pathData}\" fill=\"none\" stroke=\"{series.Color}\" stroke-width=\"2\"/>");

            seriesIndex++;
        }

        // Draw legend if enabled
        if (plotData.Settings.ShowLegend && plotData.Series.Count > 0)
        {
            int legendX = plotX + plotWidth - 120;
            int legendY = plotY + 20;
            int legendWidth = 100;
            int legendHeight = plotData.Series.Count * 20 + 10;

            // Draw legend background
            svg.AppendLine($"  <rect x=\"{legendX}\" y=\"{legendY}\" width=\"{legendWidth}\" height=\"{legendHeight}\" fill=\"white\" fill-opacity=\"0.8\" stroke=\"#333333\"/>");

            // Draw legend entries
            for (int i = 0; i < plotData.Series.Count; i++)
            {
                int entryY = legendY + 15 + i * 20;
                svg.AppendLine($"  <line x1=\"{legendX + 10}\" y1=\"{entryY}\" x2=\"{legendX + 30}\" y2=\"{entryY}\" stroke=\"{plotData.Series[i].Color}\" stroke-width=\"2\"/>");
                svg.AppendLine($"  <text x=\"{legendX + 35}\" y=\"{entryY + 5}\" font-size=\"12\">{plotData.Series[i].Name}</text>");
            }
        }

        // End SVG document
        svg.AppendLine("</svg>");

        return svg.ToString();
    }

    public void SaveToFile(PlotData plotData, string filePath)
    {
        string svg = RenderToSvg(plotData);
        File.WriteAllText(filePath, svg);
    }
}
