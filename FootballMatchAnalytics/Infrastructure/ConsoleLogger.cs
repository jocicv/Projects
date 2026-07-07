using System;

namespace FootballMatchAnalytics.Infrastructure;

public sealed class ConsoleLogger
{
    private readonly object _lock = new();

    private void Write(string level, ConsoleColor color, string message)
    {
        lock (_lock)
        {
            ConsoleColor previous = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}");
            Console.ForegroundColor = previous;
        }
    }

    public void Info(string message) => Write("INFO", ConsoleColor.Gray, message);

    public void Warn(string message) => Write("WARN", ConsoleColor.Yellow, message);

    public void Error(string message) => Write("ERR ", ConsoleColor.Red, message);
}
