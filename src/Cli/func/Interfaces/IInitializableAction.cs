﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Interfaces
{
    internal interface IInitializableAction : IAction
    {
        internal Task Initialize();
    }
}
