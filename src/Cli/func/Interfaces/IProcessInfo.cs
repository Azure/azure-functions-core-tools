// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Interfaces
{
    internal interface IProcessInfo
    {
        internal int Id { get; }

        internal string FileName { get; }

        internal string ProcessName { get; }

        internal void Kill();
    }
}
