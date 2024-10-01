// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;

namespace Azure.Functions.ArtifactAssembler
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {
            try
            {
                var currentWorkingDirectory = Environment.CurrentDirectory;
                var artifactAssembler = new ArtifactAssembler(currentWorkingDirectory);

                await artifactAssembler.AssembleArtifactsAsync();
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error running {typeof(Program).FullName} Exception: {ex}");
                return 1;
            }
        }
    }
}