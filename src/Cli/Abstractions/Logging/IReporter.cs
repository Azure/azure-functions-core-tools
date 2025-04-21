// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

// Copied from: https://github.com/dotnet/sdk/blob/4a81a96a9f1bd661592975c8269e078f6e3f18c9/src/Cli/Microsoft.DotNet.Cli.Utils/IReporter.cs
namespace Azure.Functions.Cli.Abstractions
{
    public interface IReporter
    {
        public void WriteLine(string message);

        public void WriteLine();

        public void WriteLine(string format, params object?[] args);

        public void Write(string message);
    }
}
