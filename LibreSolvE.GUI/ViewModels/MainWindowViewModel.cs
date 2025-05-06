using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Linq;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
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
        private Window? _window;

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

        // Set the window reference (should be called after initialization)
        public void SetWindow(Window window)
        {
            _window = window;
        }

        private async Task OpenFile()
        {
            if (_window == null)
            {
                StatusText = "Error: Window reference not set";
                return;
            }

            // Get the storage provider
            var storageProvider = _window.StorageProvider;

            // Set up file types filter
            var lseFileType = new FilePickerFileType("LSE Files")
            {
                Patterns = new[] { "*.lse" },
                MimeTypes = new[] { "application/octet-stream" }
            };

            var allFileType = new FilePickerFileType("All Files")
            {
                Patterns = new[] { "*" }
            };

            // Create file picker options
            var options = new FilePickerOpenOptions
            {
                Title = "Open LSE File",
                AllowMultiple = false,
                FileTypeFilter = new[] { lseFileType, allFileType }
            };

            // Show the file picker
            var result = await storageProvider.OpenFilePickerAsync(options);

            // Check if a file was selected
            if (result != null && result.Count > 0)
            {
                var file = result[0];
                _filePath = file.Path.LocalPath;

                // Read the file content
                using var stream = await file.OpenReadAsync();
                using var reader = new StreamReader(stream);
                FileContent = await reader.ReadToEndAsync();

                StatusText = $"Opened: {_filePath}";
            }
        }

        private async Task SaveFile()
        {
            if (_window == null)
            {
                StatusText = "Error: Window reference not set";
                return;
            }

            // Get the storage provider
            var storageProvider = _window.StorageProvider;

            // If no file path is set, show save as dialog
            if (string.IsNullOrEmpty(_filePath))
            {
                // Set up file types filter
                var lseFileType = new FilePickerFileType("LSE Files")
                {
                    Patterns = new[] { "*.lse" },
                    MimeTypes = new[] { "application/octet-stream" }
                };

                var allFileType = new FilePickerFileType("All Files")
                {
                    Patterns = new[] { "*" }
                };

                // Create file picker options
                var options = new FilePickerSaveOptions
                {
                    Title = "Save LSE File",
                    SuggestedFileName = Path.GetFileName(_filePath) ?? "untitled.lse",
                    DefaultExtension = ".lse",
                    FileTypeChoices = new[] { lseFileType, allFileType }
                };

                // Show the file picker
                var result = await storageProvider.SaveFilePickerAsync(options);

                // Check if a file was selected
                if (result != null)
                {
                    _filePath = result.Path.LocalPath;
                }
                else
                {
                    return; // User canceled
                }
            }

            // Write content to file
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
