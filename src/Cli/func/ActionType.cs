// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli
{
    internal class ActionType
    {
        public Type Type { get; set; }

        public IEnumerable<Context> Contexts { get; set; }

        public IEnumerable<Context> SubContexts { get; set; }

        public IEnumerable<string> Names { get; set; }
    }
}
