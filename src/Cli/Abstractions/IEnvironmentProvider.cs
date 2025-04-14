// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Note that this file is copied from: https://github.com/dotnet/sdk
// Once the dotnet cli utils package is in a published consumable state, we will migrate over to use that
namespace Azure.Functions.Cli.Abstractions
{
    public interface IEnvironmentProvider
    {
        IEnumerable<string> ExecutableExtensions { get; }

        string? GetCommandPath(string commandName, params string[] extensions);

        string? GetCommandPathFromRootPath(string rootPath, string commandName, params string[] extensions);

        string? GetCommandPathFromRootPath(string rootPath, string commandName, IEnumerable<string> extensions);

        bool GetEnvironmentVariableAsBool(string name, bool defaultValue);

        int? GetEnvironmentVariableAsNullableInt(string name);

        string? GetEnvironmentVariable(string name);

        string? GetEnvironmentVariable(string variable, EnvironmentVariableTarget target);

        void SetEnvironmentVariable(string variable, string value, EnvironmentVariableTarget target);
    }

}
