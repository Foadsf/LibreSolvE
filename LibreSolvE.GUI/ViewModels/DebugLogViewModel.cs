using Avalonia.Threading;  // Required for Dispatcher
using CommunityToolkit.Mvvm.ComponentModel;
using LibreSolvE.GUI.Logging;
using System;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Reactive.Concurrency; // Add this for IScheduler

namespace LibreSolvE.GUI.ViewModels
{
    public class DebugLogViewModel : ViewModelBase, IDisposable
    {
        private ObservableCollection<string> _logs = new ObservableCollection<string>();
        private IDisposable? _logSubscription;
        private const int MaxLogEntries = 5000; // Limit history size

        public ObservableCollection<string> Logs
        {
            get => _logs;
            set => SetProperty(ref _logs, value);
        }

        public DebugLogViewModel()
        {
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

        // Implement IDisposable to unsubscribe
        public void Dispose()
        {
            _logSubscription?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
