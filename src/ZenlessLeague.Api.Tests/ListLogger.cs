using Microsoft.Extensions.Logging;

namespace ZenlessLeague.Api.Tests;

// Captures log output so tests can assert on what was (or wasn't) logged.
public sealed class ListLogger<T> : ILogger<T>
{
	public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

	public IEnumerable<string> Messages => Entries.Select(e => e.Message);

	public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

	public bool IsEnabled(LogLevel logLevel) => true;

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
		Entries.Add((logLevel, formatter(state, exception), exception));
}
