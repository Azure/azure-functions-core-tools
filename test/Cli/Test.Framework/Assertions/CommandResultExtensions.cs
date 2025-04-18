// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

// Copied from: https://github.com/dotnet/sdk/blob/e793aa4709d28cd783712df40413448250e26fea/test/Microsoft.NET.TestFramework/Assertions/CommandResultExtensions.cs
using Azure.Functions.Cli.Abstractions.Command;

namespace Azure.Functions.Cli.Test.Framework.Assertions
{
    public static class CommandResultExtensions
    {
        public static CommandResultAssertions Should(this CommandResult commandResult) => new(commandResult);
    }
}
