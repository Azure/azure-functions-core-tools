using System;
using System.Text;

namespace Build
{
    public static class StaticLogger
    {
        public static string Log => _sb.ToString();
        private static StringBuilder _sb = new StringBuilder();
        private static object _lock = new object();

        public static void WriteLine(string message)
        {
            lock (_lock)
            {
                message = $"[{DateTimeOffset.UtcNow}] {message}";
                Console.WriteLine(message);
                _sb.AppendLine(Cut(message));
            }
        }

        public static void WriteErrorLine(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                lock (_lock)
                {
                    message = $"[{DateTimeOffset.UtcNow}] ERROR: {message}";
                    Console.Error.WriteLine(message);
                    _sb.AppendLine(Cut(message));
                }
            }
        }

        private static string Cut(string message)
        {
            return message?.Length < 500 ? message : message?.Substring(0, 500);
        }
    }
}