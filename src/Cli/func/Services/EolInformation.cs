// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Services
{
    internal class EolInformation
    {
        public string RuntimeName { get; set; }

        public string CurrentVersion { get; set; }

        public string RecommendedVersion { get; set; }

        public DateTime EolDate { get; set; }
    }
}
