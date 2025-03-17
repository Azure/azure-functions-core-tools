﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
