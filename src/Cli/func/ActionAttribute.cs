// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    internal sealed class ActionAttribute : Attribute
    {
        public Context Context { get; set; }

        public Context SubContext { get; set; }

        public string Name { get; set; }

        public string HelpText { get; set; } = "placeholder";

        public bool ShowInHelp { get; set; } = true;

        public string ParentCommandName { get; set; } = string.Empty;
    }
}
