using Serilog.Core;
using Serilog.Events;
using System;
using System.Reactive.Subjects;

namespace LibreSolvE.GUI.Logging
{
    public class CustomSubjectSink : ILogEventSink, IDisposable
    {
        private readonly Subject<LogEvent> _subject = new Subject<LogEvent>();

        public IObservable<LogEvent> Events => _subject;

        public void Emit(LogEvent logEvent)
        {
            _subject.OnNext(logEvent);
        }

        public void Dispose()
        {
            _subject.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    // Extension methods to make our sink easier to use with Serilog
    public static class CustomSubjectSinkExtensions
    {
        public static Serilog.LoggerConfiguration CustomSubject(
            this Serilog.Configuration.LoggerSinkConfiguration sinkConfiguration,
            LogEventLevel restrictedToMinimumLevel = LogEventLevel.Verbose)
        {
            var sink = new CustomSubjectSink();
            App.CustomSink = sink;
            return sinkConfiguration.Sink(sink, restrictedToMinimumLevel);
        }
    }
}
