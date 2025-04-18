// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

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
