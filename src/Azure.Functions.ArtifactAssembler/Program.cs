// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.ArtifactAssembler
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {
            try
            {
                var currentWorkingDirectory = Environment.CurrentDirectory;

                // Check if an argument for zipping is passed
                if (args.Length > 0 && args[0].Equals("zip", StringComparison.OrdinalIgnoreCase))
                {
                    var zipCliArtifacts = new CliArtifactZipper(currentWorkingDirectory);
                    zipCliArtifacts.ZipCliArtifacts();
                }
                else if (args.Length > 0 && args[0].Equals("visual-studio", StringComparison.OrdinalIgnoreCase))
                {
                    var artifactAssembler = new ArtifactAssembler(currentWorkingDirectory);
                    await artifactAssembler.AssembleArtifactsAsync(true);
                }
                else
                {
                    var artifactAssembler = new ArtifactAssembler(currentWorkingDirectory);
                    await artifactAssembler.AssembleArtifactsAsync();
                }

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