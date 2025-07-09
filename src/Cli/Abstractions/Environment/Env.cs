// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

// Copied from: https://github.com/dotnet/sdk/blob/4a81a96a9f1bd661592975c8269e078f6e3f18c9/src/Cli/Microsoft.DotNet.Cli.Utils/Env.cs
namespace Azure.Functions.Cli.Abstractions
{
    public static class Env
    {
        private static readonly IEnvironmentProvider s_environment = new EnvironmentProvider();

        public static IEnumerable<string> ExecutableExtensions => s_environment.ExecutableExtensions;

        public static string? GetCommandPath(string commandName, params string[] extensions) =>
            s_environment.GetCommandPath(commandName, extensions);

        public static string? GetCommandPathFromRootPath(string rootPath, string commandName, params string[] extensions) =>
            s_environment.GetCommandPathFromRootPath(rootPath, commandName, extensions);

        public static string? GetCommandPathFromRootPath(string rootPath, string commandName, IEnumerable<string> extensions) =>
            s_environment.GetCommandPathFromRootPath(rootPath, commandName, extensions);

        public static bool GetEnvironmentVariableAsBool(string name, bool defaultValue = false) =>
            s_environment.GetEnvironmentVariableAsBool(name, defaultValue);

        public static int? GetEnvironmentVariableAsNullableInt(string name) =>
            s_environment.GetEnvironmentVariableAsNullableInt(name);

        public static string? GetEnvironmentVariable(string name) =>
            s_environment.GetEnvironmentVariable(name);
    }
}
