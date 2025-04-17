// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Helpers
{
    public static class EnumerationHelper
    {
        public static string Join<T>(string separator, IEnumerable<T> enumerable)
        {
             return enumerable.Select(t => t.ToString())
                        .Aggregate((total, next) => total + separator + next);
        }
    }
}
