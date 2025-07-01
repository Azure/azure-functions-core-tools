// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Common
{
    public enum DeployStatus
    {
        Unknown = -1,
        Pending = 0,
        Building = 1,
        Deploying = 2,
        Failed = 3,
        Success = 4,
        Conflict = 5,
        PartialSuccess = 6
    }
}
