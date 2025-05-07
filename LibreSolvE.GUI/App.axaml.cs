using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using LibreSolvE.GUI.Logging;
using LibreSolvE.GUI.ViewModels;
using LibreSolvE.GUI.Views;
using Serilog;
using System;
using System.Reactive.Linq;
using System.Linq;

namespace LibreSolvE.GUI;

public partial class App : Application
{
    // Custom observable sink for UI updates
    public static CustomSubjectSink? CustomSink { get; internal set; }

    public override void Initialize()
    {
        // --- Serilog Configuration ---
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug() // Log everything from Debug level up
            .WriteTo.Debug()      // Write to Visual Studio Debug output
            .WriteTo.File("logs/gui_debug_.log", // Log to a rolling file
                          rollingInterval: RollingInterval.Day,
                          retainedFileCountLimit: 7,
                          outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.CustomSubject(Serilog.Events.LogEventLevel.Debug) // Send logs to our custom observable sink
            .CreateLogger();

        Log.Information("--- Application Starting ---");
        // --- End Serilog Configuration ---

        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Register exit event handler for cleanup
            desktop.Exit += (s, e) =>
            {
                Log.Information("--- Application Exiting ---");
                Log.CloseAndFlush(); // Ensure logs are flushed on exit
            };

            DisableAvaloniaDataAnnotationValidation();
            var viewModel = new MainWindowViewModel(); // Create VM
            var mainWindow = new MainWindow { DataContext = viewModel }; // Set DC
            viewModel.SetWindow(mainWindow); // Pass window ref

            desktop.MainWindow = mainWindow;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            // Handle single view if needed
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
