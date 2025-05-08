using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibreSolvE.GUI.Logging;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace LibreSolvE.GUI.ViewModels
{
    public class DebugLogViewModel : ViewModelBase, IDisposable
    {
        private ObservableCollection<string> _logs = new ObservableCollection<string>();
        private IDisposable? _logSubscription;
        private const int MaxLogEntries = 10000; // Increased limit

        public ObservableCollection<string> Logs
        {
            get => _logs;
            set => SetProperty(ref _logs, value);
        }

        // Commands
        public IRelayCommand CopyToClipboardCommand { get; }
        public IRelayCommand ExportToFileCommand { get; }
        public IRelayCommand ClearLogCommand { get; }

        public DebugLogViewModel()
        {
            // Initialize commands
            CopyToClipboardCommand = new RelayCommand(CopyToClipboard);
            ExportToFileCommand = new AsyncRelayCommand(ExportToFileAsync);
            ClearLogCommand = new RelayCommand(ClearLog);

            // Subscribe to the custom observable sink from App.axaml.cs
            if (App.CustomSink != null)
            {
                _logSubscription = App.CustomSink.Events
                    // Ensure updates happen on the UI thread - use the correct scheduler approach
                    .ObserveOn(System.Reactive.Concurrency.Scheduler.Default)
                    .Subscribe(logEvent =>
                    {
                        // Need to explicitly dispatch to UI thread
                        Dispatcher.UIThread.Post(() =>
                        {
                            // Format the log event (customize as needed)
                            string formattedLog = $"{logEvent.Timestamp:HH:mm:ss.fff} [{logEvent.Level}] {logEvent.RenderMessage()}";
                            if (logEvent.Exception != null)
                            {
                                formattedLog += $"\n    Exception: {logEvent.Exception.Message}";
                            }

                            // Add to collection, manage size
                            if (Logs.Count >= MaxLogEntries)
                            {
                                Logs.RemoveAt(0); // Remove oldest entry
                            }
                            Logs.Add(formattedLog);
                        });
                    }, onError: ex =>
                    {
                        // Handle errors in the subscription if necessary
                        Dispatcher.UIThread.Post(() =>
                            Logs.Add($"!!! LOGGING ERROR: {ex.Message}")
                        );
                    });
            }
            else
            {
                Logs.Add("!!! ERROR: Custom logging sink not initialized! Logging not available. !!!");
            }
        }

        private void CopyToClipboard()
        {
            try
            {
                // Build log content
                StringBuilder sb = new StringBuilder();
                foreach (var log in Logs)
                {
                    sb.AppendLine(log);
                }

                // Use the TopLevel to access the clipboard
                var topLevel = GetTopLevel();
                if (topLevel?.Clipboard != null)
                {
                    Dispatcher.UIThread.Post(async () =>
                    {
                        await topLevel.Clipboard.SetTextAsync(sb.ToString());
                        Logs.Add($"{DateTime.Now:HH:mm:ss.fff} [Info] Copied log to clipboard.");
                    });
                }
                else
                {
                    Logs.Add($"{DateTime.Now:HH:mm:ss.fff} [Error] Failed to access clipboard.");
                }
            }
            catch (Exception ex)
            {
                Logs.Add($"{DateTime.Now:HH:mm:ss.fff} [Error] Failed to copy to clipboard: {ex.Message}");
            }
        }

        private async Task ExportToFileAsync()
        {
            try
            {
                var topLevel = GetTopLevel();
                if (topLevel?.StorageProvider == null)
                {
                    Logs.Add($"{DateTime.Now:HH:mm:ss.fff} [Error] Failed to access storage provider.");
                    return;
                }

                var options = new Avalonia.Platform.Storage.FilePickerSaveOptions
                {
                    Title = "Save Debug Log",
                    DefaultExtension = "log",
                    SuggestedFileName = $"LibreSolvE_Debug_{DateTime.Now:yyyyMMdd_HHmmss}.log"
                };

                var file = await topLevel.StorageProvider.SaveFilePickerAsync(options);
                if (file != null)
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (var log in Logs)
                    {
                        sb.AppendLine(log);
                    }

                    await using var stream = await file.OpenWriteAsync();
                    using var writer = new StreamWriter(stream);
                    await writer.WriteAsync(sb.ToString());

                    Logs.Add($"{DateTime.Now:HH:mm:ss.fff} [Info] Log exported to {file.Name}");
                }
            }
            catch (Exception ex)
            {
                Logs.Add($"{DateTime.Now:HH:mm:ss.fff} [Error] Failed to export log: {ex.Message}");
            }
        }

        private void ClearLog()
        {
            Logs.Clear();
            Logs.Add($"{DateTime.Now:HH:mm:ss.fff} [Info] Log cleared.");
        }

        // Helper method to get the TopLevel
        private Avalonia.Controls.TopLevel? GetTopLevel()
        {
            if (App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                return Avalonia.Controls.TopLevel.GetTopLevel(desktop.MainWindow);
            }
            return null;
        }

        // Implement IDisposable to unsubscribe
        public void Dispose()
        {
            _logSubscription?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
