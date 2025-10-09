// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Helpers;

namespace Azure.Functions.Cli.ConfigurationProfiles
{
    internal interface IConfigurationProfile
    {
        /// <summary>
        /// Gets or sets the name of the profile.
        /// </summary>
        internal string Name { get; set; }

        /// <summary>
        /// Applies the profile by generating necessary configuration artifacts.
        /// </summary>
        internal Task ApplyAsync(WorkerRuntime runtime, bool shouldForce = false);
    }
}
