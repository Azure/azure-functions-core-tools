// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;
using AwesomeAssertions;
using Azure.Functions.Cli.Common;
using Xunit;

namespace Azure.Functions.Cli.UnitTests
{
    public class CommandCheckerTests
    {
        private const string MissingShell = "/nonexistent/shell-that-does-not-exist";

        // Shells commonly present on Unix-like systems. The tests use whichever one exists, so they do not
        // assume that /bin/sh, /bin/bash or "sh" on PATH is available.
        private static readonly string[] KnownShells = ["/bin/sh", "/bin/bash", "/usr/bin/sh", "/usr/bin/bash", "/bin/dash"];

        [Fact]
        public void CommandCheckerShouldWork()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var exists = CommandChecker.CommandExists("cmd");
                var doesntExist = CommandChecker.CommandExists("fooo");

                exists.Should().BeTrue(because: "checking if cmd command exists should always be true on Windows");
                doesntExist.Should().BeFalse(because: "checking if fooo command exists on windows should be false");
            }
            else if (FindShell() is not null)
            {
                // "command" is a shell builtin, so it resolves in any POSIX shell without relying on PATH.
                var exists = CommandChecker.CommandExists("command");
                var doesntExist = CommandChecker.CommandExists("fooo");

                exists.Should().BeTrue(because: "checking if a shell builtin exists should always be true on Unix-like");
                doesntExist.Should().BeFalse(because: "checking if fooo command exists on Unix-like should be false");
            }
        }

        [Fact]
        public void UnixCommandExists_SkipsMissingShellsAndUsesNextAvailable()
        {
            var shell = FindShell();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || shell is null)
            {
                return;
            }

            // Simulates a system where the preferred shell (for example /bin/sh) is absent but another one exists.
            CommandChecker.UnixCommandExists("command", [MissingShell, shell], MissingShell).Should().BeTrue();
            CommandChecker.UnixCommandExists("fooo", [MissingShell, shell], MissingShell).Should().BeFalse();
        }

        [Fact]
        public void UnixCommandExists_ReturnsFalseWhenNoShellIsAvailable()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            // No candidate exists and the PATH fallback cannot be resolved, so the check must report false instead of throwing.
            var result = CommandChecker.UnixCommandExists("command", [MissingShell], "no-such-shell-on-path-for-tests");

            result.Should().BeFalse();
        }

        private static string FindShell() => KnownShells.FirstOrDefault(File.Exists);
    }
}
