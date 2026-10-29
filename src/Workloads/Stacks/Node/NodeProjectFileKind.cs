// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Node;

internal enum NodeProjectFileKind
{
    Absent,
    RegularFile,
    Directory,
    Unsupported,
}
