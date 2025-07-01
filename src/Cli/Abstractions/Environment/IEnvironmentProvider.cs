// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

// Copied from: https://github.com/dotnet/sdk/blob/4a81a96a9f1bd661592975c8269e078f6e3f18c9/src/Cli/Microsoft.DotNet.Cli.Utils/IEnvironmentProvider.cs
namespace Azure.Functions.Cli.Abstractions
{
    public interface IEnvironmentProvider
    {
        public IEnumerable<string> ExecutableExtensions { get; }

        public string? GetCommandPath(string commandName, params string[] extensions);

        public string? GetCommandPathFromRootPath(string rootPath, string commandName, params string[] extensions);

        public string? GetCommandPathFromRootPath(string rootPath, string commandName, IEnumerable<string> extensions);

        public bool GetEnvironmentVariableAsBool(string name, bool defaultValue);

        public int? GetEnvironmentVariableAsNullableInt(string name);

        public string? GetEnvironmentVariable(string name);

        public string? GetEnvironmentVariable(string variable, EnvironmentVariableTarget target);

        public void SetEnvironmentVariable(string variable, string value, EnvironmentVariableTarget target);
    }
}
