using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Linq;
using System.Windows.Input;
using Avalonia.Controls;
using LibreSolvE.Core.Plotting;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using OxyPlot;

namespace LibreSolvE.GUI.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private string _fileContent = "";
        private string _outputText = "";
        private string _filePath = "";
        private string _statusText = "Ready";
        private PlotData? _currentPlot;

        public string FileContent
        {
            get => _fileContent;
            set => SetProperty(ref _fileContent, value);
        }

        public string OutputText
        {
            get => _outputText;
            set => SetProperty(ref _outputText, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public PlotData? CurrentPlot
        {
            get => _currentPlot;
            set => SetProperty(ref _currentPlot, value);
        }

        public ICommand OpenCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand RunCommand { get; }
        public ICommand ExitCommand { get; }

        public MainWindowViewModel()
        {
            OpenCommand = new AsyncRelayCommand(OpenFile);
            SaveCommand = new AsyncRelayCommand(SaveFile);
            RunCommand = new RelayCommand(RunFile);
            ExitCommand = new RelayCommand(() => Environment.Exit(0));
        }

        private async Task OpenFile()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Open LSE File",
                Filters = new List<FileDialogFilter>
                {
                    new FileDialogFilter { Name = "LSE Files", Extensions = new List<string> { "lse" } },
                    new FileDialogFilter { Name = "All Files", Extensions = new List<string> { "*" } }
                }
            };

            var result = await dialog.ShowAsync(new Window());
            if (result != null && result.Length > 0)
            {
                _filePath = result[0];
                FileContent = File.ReadAllText(_filePath);
                StatusText = $"Opened: {_filePath}";
            }
        }

        private async Task SaveFile()
        {
            if (string.IsNullOrEmpty(_filePath))
            {
                var dialog = new SaveFileDialog
                {
                    Title = "Save LSE File",
                    Filters = new List<FileDialogFilter>
                    {
                        new FileDialogFilter { Name = "LSE Files", Extensions = new List<string> { "lse" } },
                        new FileDialogFilter { Name = "All Files", Extensions = new List<string> { "*" } }
                    }
                };

                var result = await dialog.ShowAsync(new Window());
                if (!string.IsNullOrEmpty(result))
                {
                    _filePath = result;
                }
                else
                {
                    return;
                }
            }

            File.WriteAllText(_filePath, FileContent);
            StatusText = $"Saved: {_filePath}";
        }

        private void RunFile()
        {
            try
            {
                // Save content to a temporary file
                string tempFile;
                if (string.IsNullOrEmpty(_filePath))
                {
                    tempFile = Path.Combine(Path.GetTempPath(), $"libresolvE_{DateTime.Now:yyyyMMddHHmmss}.lse");
                }
                else
                {
                    tempFile = _filePath;
                }

                File.WriteAllText(tempFile, FileContent);

                // Run the CLI process
                var processInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"run --project LibreSolvE.CLI/LibreSolvE.CLI.csproj -- \"{tempFile}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                var process = new Process { StartInfo = processInfo };
                var outputBuilder = new StringBuilder();

                process.OutputDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                    {
                        outputBuilder.AppendLine(args.Data);
                    }
                };

                process.ErrorDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                    {
                        outputBuilder.AppendLine($"ERROR: {args.Data}");
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                OutputText = outputBuilder.ToString();
                StatusText = process.ExitCode == 0 ? "Execution successful" : $"Execution failed with code {process.ExitCode}";

                // Check for generated plots
                CheckForPlots();
            }
            catch (Exception ex)
            {
                OutputText = $"Error executing file: {ex.Message}";
                StatusText = "Execution failed";
            }
        }

        private void CheckForPlots()
        {
            // Find the most recent SVG plot file (if any)
            var currentDir = Directory.GetCurrentDirectory();
            var svgFiles = Directory.GetFiles(currentDir, "plot_*.svg");

            if (svgFiles.Length > 0)
            {
                // Find most recent
                var mostRecent = svgFiles.OrderByDescending(f => File.GetCreationTime(f)).First();
                StatusText = $"Plot created: {Path.GetFileName(mostRecent)}";

                try
                {
                    // Read the SVG content
                    string svgContent = File.ReadAllText(mostRecent);

                    // Update the plot view
                    if (PlotView != null && PlotView is Views.PlotView plotView)
                    {
                        // Create a simple OxyPlot model based on the found plot
                        var model = new PlotModel { Title = "Generated Plot" };
                        model.Axes.Add(new OxyPlot.Axes.LinearAxis { Position = OxyPlot.Axes.AxisPosition.Bottom, Title = "X" });
                        model.Axes.Add(new OxyPlot.Axes.LinearAxis { Position = OxyPlot.Axes.AxisPosition.Left, Title = "Y" });

                        // Set the plot model to the view
                        plotView.PlotModel = model;
                    }
                }
                catch (Exception ex)
                {
                    StatusText = $"Error loading plot: {ex.Message}";
                }
            }
        }

        // Reference to the PlotView control
        public Views.PlotView? PlotView { get; set; }
    }
}
