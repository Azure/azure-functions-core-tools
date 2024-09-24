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

                // print child directories present in currentWorkingDirectory.
                Console.WriteLine("Child directories present in current working directory:");
                foreach (var directory in Directory.GetDirectories(currentWorkingDirectory))
                {
                    Console.WriteLine("  " + directory);
                    // Print child folder or files in this directory.
                    foreach (var file in Directory.GetFiles(directory))
                    {
                        Console.WriteLine("  -----F " + file);
                    }
                    // Print child directories in this directory.
                    foreach (var childDirectory in Directory.GetDirectories(directory))
                    {
                        Console.WriteLine("  -----D " + childDirectory);
                        if (childDirectory.Contains("drop-coretools-host-windows") || childDirectory.Contains("drop-inproc6"))
                        {
                            // print contents of this directory.
                            foreach (var file in Directory.GetFiles(childDirectory))
                            {
                                Console.WriteLine("  ----- ----- F1 " + file);
                            }
                            // print child folders
                            foreach (var subChildDirectory in Directory.GetDirectories(childDirectory))
                            {
                                Console.WriteLine("  ----- ----- D1 " + subChildDirectory);
                                // print contents of this directory.
                                foreach (var file in Directory.GetFiles(subChildDirectory))
                                {
                                    Console.WriteLine("  ----- ----- ----- F2 " + file);
                                }
                            }
                        }
                    }
                }

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