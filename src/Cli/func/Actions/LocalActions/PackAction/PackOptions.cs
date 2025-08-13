// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Actions.LocalActions.PackAction
{
    public class PackOptions
    {
        public string FolderPath { get; set; } = string.Empty;

        public string OutputPath { get; set; } = string.Empty;

        public bool NoBuild { get; set; }

        public string[] PreserveExecutables { get; set; } = Array.Empty<string>();
    }
}
