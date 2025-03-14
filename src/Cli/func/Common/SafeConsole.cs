using System;

namespace Azure.Functions.Cli.Common
{
    public static class SafeConsole
    {
        public static int BufferWidth { get; } = SafeGet(() => Console.BufferWidth, int.MaxValue);

        private static T SafeGet<T>(Func<T> func, T @default)
        {
            try
            {
                return func();
            }
            catch
            {
                return @default;
            }
        }
    }
}
