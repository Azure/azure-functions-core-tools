// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Copied from: https://github.com/dotnet/sdk/blob/4a81a96a9f1bd661592975c8269e078f6e3f18c9/src/Cli/Microsoft.DotNet.Cli.Utils/IReporter.cs

namespace Azure.Functions.Cli.Abstractions.Logging
{
    public interface IReporter
    {
        void WriteLine(string message);

        void WriteLine();

        void WriteLine(string format, params object?[] args);

        void Write(string message);
    }
}
