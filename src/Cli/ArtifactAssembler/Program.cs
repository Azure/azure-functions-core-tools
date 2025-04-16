// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.Cli.ArtifactAssembler
{
    internal class Program
    {
        static int Main(string[] args)
        {
            try
            {
                var currentWorkingDirectory = Environment.CurrentDirectory;

                // Check if an argument for zipping is passed
                if (args.Length > 0 && args[0].Equals("zip", StringComparison.OrdinalIgnoreCase))
                {
                    var zipArtifacts = new ArtifactZipper(currentWorkingDirectory);
                    zipArtifacts.ZipCliArtifacts();
                    zipArtifacts.ZipVisualStudioArtifacts();
                }
                else
                {
                    string? artifactName = args.Length > 0 ? args[0] : string.Empty;
                    var artifactAssembler = new ArtifactAssembler(currentWorkingDirectory, artifactName);
                    artifactAssembler.Assemble();
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