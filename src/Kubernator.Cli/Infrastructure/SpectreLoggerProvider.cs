using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace Kubernator.Cli.Infrastructure;

internal sealed class SpectreLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new SpectreLogger(categoryName);

    public void Dispose() { }

    private sealed class SpectreLogger : ILogger
    {
        private readonly string category;

        public SpectreLogger(string category)
        {
            this.category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }
            var color = logLevel switch
            {
                LogLevel.Error or LogLevel.Critical => "red",
                LogLevel.Warning => "yellow",
                _ => "grey"
            };
            var message = formatter(state, exception);
            AnsiConsole.MarkupLine($"[{color}][[{logLevel}]] {Markup.Escape(category)}: {Markup.Escape(message)}[/]");
            if (exception is not null)
            {
                AnsiConsole.MarkupLine($"[{color}]{Markup.Escape(exception.Message)}[/]");
            }
        }
    }
}
