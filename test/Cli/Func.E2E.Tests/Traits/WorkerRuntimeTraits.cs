// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.E2E.Tests.Traits
{
    /// <summary>
    /// Defines trait constants used to categorize tests by their target worker runtime.
    /// These traits are used with xUnit's [Trait] attribute to organize and filter tests.
    /// </summary>
    internal static class WorkerRuntimeTraits
    {
        /// <summary>
        /// The trait category name for worker runtime traits.
        /// Used as the first parameter in [Trait] attributes.
        /// </summary>
        public const string WorkerRuntime = "WorkerRuntime";

        /// <summary>
        /// Indicates tests that target the in-process .NET runtime.
        /// </summary>
        public const string Dotnet = "Dotnet";

        /// <summary>
        /// Indicates tests that target the out-of-process .NET isolated runtime.
        /// </summary>
        public const string DotnetIsolated = "DotnetIsolated";

        /// <summary>
        /// Indicates tests that target the Node.js runtime.
        /// </summary>
        public const string Node = "Node";

        /// <summary>
        /// Indicates tests that target the PowerShell runtime.
        /// </summary>
        public const string Powershell = "Powershell";

        /// <summary>
        /// Indicates tests that target the Python runtime.
        /// </summary>
        public const string Python = "Python";

        /// <summary>
        /// Indicates tests that target the Java runtime.
        /// </summary>
        public const string Java = "Java";
    }
}
