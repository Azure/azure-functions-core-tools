// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Func.TestFramework.Assertions
{
    public static class CommandResultExtensions
    {
        public static CommandResultAssertions Should(this CommandResult commandResult) => new CommandResultAssertions(commandResult);
    }
}