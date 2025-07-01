// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

// Copied from: https://github.com/dotnet/sdk/blob/4a81a96a9f1bd661592975c8269e078f6e3f18c9/src/Cli/Microsoft.DotNet.Cli.Utils/Env.cs
namespace Azure.Functions.Cli.Abstractions
{
    public static class Env
    {
        private static readonly IEnvironmentProvider _environment = new EnvironmentProvider();

        public static IEnumerable<string> ExecutableExtensions => _environment.ExecutableExtensions;

        public static string? GetCommandPath(string commandName, params string[] extensions) =>
            _environment.GetCommandPath(commandName, extensions);

        public static string? GetCommandPathFromRootPath(string rootPath, string commandName, params string[] extensions) =>
            _environment.GetCommandPathFromRootPath(rootPath, commandName, extensions);

        public static string? GetCommandPathFromRootPath(string rootPath, string commandName, IEnumerable<string> extensions) =>
            _environment.GetCommandPathFromRootPath(rootPath, commandName, extensions);

        public static bool GetEnvironmentVariableAsBool(string name, bool defaultValue = false) =>
            _environment.GetEnvironmentVariableAsBool(name, defaultValue);

        public static int? GetEnvironmentVariableAsNullableInt(string name) =>
            _environment.GetEnvironmentVariableAsNullableInt(name);

        public static string? GetEnvironmentVariable(string name) =>
            _environment.GetEnvironmentVariable(name);
    }
}
