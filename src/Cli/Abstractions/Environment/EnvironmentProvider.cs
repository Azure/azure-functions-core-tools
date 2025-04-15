﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Note that this file is copied from: https://github.com/dotnet/sdk/blob/4a81a96a9f1bd661592975c8269e078f6e3f18c9/src/Cli/Microsoft.DotNet.Cli.Utils/EnvironmentProvider.cs
// Once the dotnet cli utils package is in a published consumable state, we will migrate over to use that

using System;
using Azure.Functions.Cli.Abstractions.Extensions;
using System.Runtime.InteropServices;

namespace Azure.Functions.Cli.Abstractions.Environment
{
    public class EnvironmentProvider : IEnvironmentProvider
    {
        private static char[] s_pathSeparator = new char[] { Path.PathSeparator };
        private static char[] s_quote = new char[] { '"' };
        private IEnumerable<string>? _searchPaths;
        private readonly Lazy<string> _userHomeDirectory = new(() => Environment.GetEnvironmentVariable("HOME") ?? string.Empty);
        private IEnumerable<string>? _executableExtensions;

        public IEnumerable<string> ExecutableExtensions
        {
            get
            {
                if (_executableExtensions == null)
                {
                    _executableExtensions = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                        ? Environment.GetEnvironmentVariable("PATHEXT")?
                            .Split(';')
                            .Select(e => e.ToLower().Trim('"')) ?? [string.Empty]
                        : [string.Empty];
                }

                return _executableExtensions;
            }
        }

        private IEnumerable<string> SearchPaths
        {
            get
            {
                if (_searchPaths == null)
                {
                    var searchPaths = new List<string> { AppContext.BaseDirectory };

                    searchPaths.AddRange(Environment
                        .GetEnvironmentVariable("PATH")?
                        .Split(s_pathSeparator)
                        .Select(p => p.Trim(s_quote))
                        .Where(p => !string.IsNullOrWhiteSpace(p))
                        .Select(p => ExpandTildeSlash(p)) ?? []);

                    _searchPaths = searchPaths;
                }

                return _searchPaths;
            }
        }

        private string ExpandTildeSlash(string path)
        {
            const string tildeSlash = "~/";
            if (path.StartsWith(tildeSlash, StringComparison.Ordinal) && !string.IsNullOrEmpty(_userHomeDirectory.Value))
            {
                return Path.Combine(_userHomeDirectory.Value, path.Substring(tildeSlash.Length));
            }
            else
            {
                return path;
            }
        }

        public EnvironmentProvider(
            IEnumerable<string>? extensionsOverride = null,
            IEnumerable<string>? searchPathsOverride = null)
        {
            _executableExtensions = extensionsOverride;
            _searchPaths = searchPathsOverride;
        }

        public string? GetCommandPath(string commandName, params string[] extensions)
        {
            if (!extensions.Any())
            {
                extensions = ExecutableExtensions.ToArray();
            }

            var commandPath = SearchPaths.Join(
                extensions,
                    p => true, s => true,
                    (p, s) => Path.Combine(p, commandName + s))
                .FirstOrDefault(File.Exists);

            return commandPath;
        }

        public string? GetCommandPathFromRootPath(string rootPath, string commandName, params string[] extensions)
        {
            if (!extensions.Any())
            {
                extensions = ExecutableExtensions.ToArray();
            }

            var commandPath = extensions.Select(e => Path.Combine(rootPath, commandName + e))
                .FirstOrDefault(File.Exists);

            return commandPath;
        }

        public string? GetCommandPathFromRootPath(string rootPath, string commandName, IEnumerable<string> extensions)
        {
            var extensionsArr = extensions.OrEmptyIfNull().ToArray();

            return GetCommandPathFromRootPath(rootPath, commandName, extensionsArr);
        }

        public string? GetEnvironmentVariable(string name)
        {
            return Environment.GetEnvironmentVariable(name);
        }

        public bool GetEnvironmentVariableAsBool(string name, bool defaultValue)
        {
            var str = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(str))
            {
                return defaultValue;
            }

            switch (str.ToLowerInvariant())
            {
                case "true":
                case "1":
                case "yes":
                    return true;
                case "false":
                case "0":
                case "no":
                    return false;
                default:
                    return defaultValue;
            }
        }

        public string? GetEnvironmentVariable(string variable, EnvironmentVariableTarget target)
        {
            return Environment.GetEnvironmentVariable(variable, target);
        }

        public void SetEnvironmentVariable(string variable, string value, EnvironmentVariableTarget target)
        {
            Environment.SetEnvironmentVariable(variable, value, target);
        }

        public int? GetEnvironmentVariableAsNullableInt(string variable)
        {
            if (Environment.GetEnvironmentVariable(variable) is string strValue && int.TryParse(strValue, out int intValue))
            {
                return intValue;
            }

            return null;
        }
    }
}
