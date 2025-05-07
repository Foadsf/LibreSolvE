// LibreSolvE.CLI/PlotExporter.cs
using System;
using System.IO;
using LibreSolvE.Core.Plotting;

namespace LibreSolvE.CLI;

public static class PlotExporter
{
    public static void ExportToFormat(PlotData plotData, string outputPath, PlotFormat format)
    {
        // For now, we only support SVG format natively
        var svgRenderer = new SvgPlotRenderer();

        switch (format)
        {
            case PlotFormat.SVG:
                // Just save the SVG directly
                svgRenderer.SaveToFile(plotData, outputPath);
                break;

            case PlotFormat.PNG:
            case PlotFormat.PDF:
            default:
                // For formats other than SVG, save as SVG instead
                Console.WriteLine($"Note: {format} export not currently supported. Saving as SVG instead.");
                string svgPath = Path.ChangeExtension(outputPath, ".svg");
                svgRenderer.SaveToFile(plotData, svgPath);
                break;
        }
    }
}
