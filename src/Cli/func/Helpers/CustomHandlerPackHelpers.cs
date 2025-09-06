// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Helpers
{
    /// <summary>
    /// Helper class for custom handler logic shared between pack and publish commands
    /// </summary>
    public static class CustomHandlerPackHelpers
    {
        /// <summary>
        /// Gets the custom handler executable path and returns it as a collection for packaging
        /// </summary>
        /// <param name="functionAppRoot">The root directory of the function app</param>
        /// <returns>Collection containing the custom handler executable if present, empty otherwise</returns>
        public static async Task<IEnumerable<string>> GetCustomHandlerExecutablesAsync(string functionAppRoot)
        {
            var customHandler = await HostHelpers.GetCustomHandlerExecutable(functionAppRoot);
            return !string.IsNullOrEmpty(customHandler)
                ? new[] { customHandler }
                : Enumerable.Empty<string>();
        }

        /// <summary>
        /// Creates a zip stream for custom handler apps with proper executable permissions
        /// </summary>
        /// <param name="functionAppRoot">The root directory of the function app</param>
        /// <param name="ignoreParser">Optional git ignore parser for filtering files</param>
        /// <returns>Stream containing the zip file with custom handler executables marked as executable</returns>
        public static async Task<Stream> CreateCustomHandlerZipAsync(string functionAppRoot, GitIgnoreParser ignoreParser = null)
        {
            var executables = await GetCustomHandlerExecutablesAsync(functionAppRoot);
            var files = FileSystemHelpers.GetLocalFiles(functionAppRoot, ignoreParser, false);
            return await ZipHelper.CreateZip(files, functionAppRoot, executables);
        }
    }
}