// LibreSolvE.Core/Plotting/PlotData.cs
namespace LibreSolvE.Core.Plotting;

public class PlotSeries
{
    public string Name { get; set; } = string.Empty;
    public List<double> XValues { get; set; } = new List<double>();
    public List<double> YValues { get; set; } = new List<double>();
    public string Color { get; set; } = "#1E88E5"; // Default blue color
    public string LineStyle { get; set; } = "solid"; // solid, dashed, dotted, etc.
    public string MarkerStyle { get; set; } = "none"; // none, circle, square, etc.
}

public class PlotSettings
{
    public string Title { get; set; } = string.Empty;
    public string XLabel { get; set; } = string.Empty;
    public string YLabel { get; set; } = string.Empty;
    public bool ShowLegend { get; set; } = true;
    public bool ShowGrid { get; set; } = true;
    public double? XMin { get; set; } = null;
    public double? XMax { get; set; } = null;
    public double? YMin { get; set; } = null;
    public double? YMax { get; set; } = null;
}

public class PlotData
{
    public PlotSettings Settings { get; set; } = new PlotSettings();
    public List<PlotSeries> Series { get; set; } = new List<PlotSeries>();
    public string FilePath { get; set; } = string.Empty;
}
