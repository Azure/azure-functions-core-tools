// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start;

internal static class LegacyStartArgumentNormalizer
{
    public static string[] Normalize(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length < 2
            || !string.Equals(args[0], "host", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(args[1], "start", StringComparison.OrdinalIgnoreCase))
        {
            return args;
        }

        return ["start", .. args[2..]];
    }
}
