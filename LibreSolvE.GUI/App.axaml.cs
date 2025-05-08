// File: LibreSolvE.GUI/App.axaml.cs
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Threading; // Added for DispatcherUnhandledExceptionEventArgs
using LibreSolvE.GUI.Logging;
using LibreSolvE.GUI.ViewModels;
using LibreSolvE.GUI.Views;
using Serilog;
using System;
using System.Diagnostics; // Added for Debug.WriteLine
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

        try
        {
            AvaloniaXamlLoader.Load(this);
        }
        catch (Exception ex)
        {
            // Log XAML loading errors which can happen before the main exception handler is set up
            Log.Fatal(ex, "FATAL ERROR during AvaloniaXamlLoader.Load(this)");
            // Optionally write to Debug output as well
            Debug.WriteLine($"FATAL XAML Load Error: {ex}");
            // Rethrow or handle appropriately - maybe show a basic OS message box if possible
            throw; // Rethrow to ensure the app doesn't continue in a broken state
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Add this handler *before* setting up the main window
        Dispatcher.UIThread.UnhandledException += Dispatcher_UnhandledException;
        Log.Debug("Registered Dispatcher_UnhandledException handler.");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Log.Debug("ApplicationLifetime is IClassicDesktopStyleApplicationLifetime.");
            // Register exit event handler for cleanup
            desktop.Exit += (s, e) =>
            {
                Log.Information("--- Application Exiting ---");
                Log.CloseAndFlush(); // Ensure logs are flushed on exit
            };
            Log.Debug("Registered Desktop Application Exit handler.");

            DisableAvaloniaDataAnnotationValidation();

            try
            {
                Log.Debug("Creating MainWindowViewModel...");
                var viewModel = new MainWindowViewModel(); // Create VM
                Log.Debug("Creating MainWindow...");
                var mainWindow = new MainWindow { DataContext = viewModel }; // Set DC
                Log.Debug("Setting window reference in ViewModel...");
                viewModel.SetWindow(mainWindow);

                Log.Debug("Setting desktop.MainWindow...");
                desktop.MainWindow = mainWindow;
                Log.Debug("MainWindow assigned successfully.");
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "FATAL ERROR during MainWindow creation/assignment.");
                // Handle fatal error - maybe show a message box if UI is partially available
                // or just ensure logging happens before exiting.
                throw;
            }
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            Log.Debug("ApplicationLifetime is ISingleViewApplicationLifetime.");
            // Handle single view if needed
        }
        else
        {
            Log.Warning("ApplicationLifetime is neither Classic Desktop nor Single View. Type: {LifetimeType}", ApplicationLifetime?.GetType().Name ?? "null");
        }

        Log.Debug("Calling base.OnFrameworkInitializationCompleted().");
        try
        {
            base.OnFrameworkInitializationCompleted();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "FATAL ERROR during base.OnFrameworkInitializationCompleted().");
            throw;
        }
        Log.Debug("Framework initialization completed.");
    }

    private void Dispatcher_UnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Log the exception
        Log.Fatal(e.Exception, "!!! Unhandled UI exception occurred !!!");
        Debug.WriteLine($"!!! Unhandled UI exception: {e.Exception}"); // Also write to Debug output

        // Prevent default OS handling ONLY if you want to try and keep the app running,
        // which is generally NOT recommended for fatal errors unless you know exactly what you're doing.
        // For debugging, it's often better to let the app crash to get a full stack trace if possible.
        // e.Handled = true;
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        if (dataValidationPluginsToRemove.Any())
        {
            Log.Debug("Removing {Count} DataAnnotationsValidationPlugin(s).", dataValidationPluginsToRemove.Length);
            // remove each entry found
            foreach (var plugin in dataValidationPluginsToRemove)
            {
                BindingPlugins.DataValidators.Remove(plugin);
            }
        }
        else
        {
            Log.Debug("No DataAnnotationsValidationPlugin found to remove.");
        }
    }
}
