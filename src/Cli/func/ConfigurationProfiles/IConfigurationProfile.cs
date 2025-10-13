// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Helpers;

namespace Azure.Functions.Cli.ConfigurationProfiles
{
    internal interface IConfigurationProfile
    {
        /// <summary>
        /// Gets the name of the profile.
        /// </summary>
        internal string Name { get; }

        /// <summary>
        /// Applies the profile by generating necessary configuration artifacts.
        /// </summary>
        /// <param name="runtime">The worker runtime of the function app.</param>
        /// <param name="force">If true, forces overwriting existing configurations.</param>
        internal Task ApplyAsync(WorkerRuntime runtime, bool force = false);
    }
}
