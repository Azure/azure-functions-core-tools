// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Azurite;

/// <summary>
/// Abstraction over the wall clock so time-sensitive code can be substituted in tests.
/// </summary>
internal interface IClock
{
    public DateTimeOffset UtcNow { get; }
}

/// <summary>
/// Production <see cref="IClock"/> backed by <see cref="DateTimeOffset.UtcNow"/>.
/// </summary>
internal sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
