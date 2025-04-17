// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli
{
    internal class TypeAttributePair
    {
        public Type Type { get; set; }

        public ActionAttribute Attribute { get; set; }
    }
}
