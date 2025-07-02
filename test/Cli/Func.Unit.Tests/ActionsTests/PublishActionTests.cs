// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.Functions.Cli.Actions.AzureActions;
using Azure.Functions.Cli.Arm.Models;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.StacksApi;
using Xunit;
using static Azure.Functions.Cli.Actions.AzureActions.PublishFunctionAppAction;

namespace Azure.Functions.Cli.Unit.Test.ActionsTests
{
    public class PublishActionTests
    {
        private FunctionsStacks GetMockFunctionStacks()
        {
            return new FunctionsStacks
            {
                Languages = new List<Language>
       {
           new Language
           {
               Name = "python",
               Properties = new Properties
               {
                   DisplayText = "Python",
                   MajorVersions = new List<MajorVersion>
                   {
                       new MajorVersion
                       {
                           Value = "3",
                           MinorVersions = new List<MinorVersion>
                           {
                               new MinorVersion
                               {
                                   Value = "3.8",
                                   StackSettings = new StackSettings
                                   {
                                       LinuxRuntimeSettings = new LinuxRuntimeSettings
                                       {
                                           RuntimeVersion = "Python|3.8"
                                       }
                                   }
                               },
                               new MinorVersion
                               {
                                   Value = "3.12",
                                   StackSettings = new StackSettings
                                   {
                                       LinuxRuntimeSettings = new LinuxRuntimeSettings
                                       {
                                           RuntimeVersion = "Python|3.12"
                                       }
                                   }
                               }
                           }
                       }
                   }
               }
           },
           new Language
           {
               Name = "node",
               Properties = new Properties
{
   DisplayText = "Node.js",
   MajorVersions = new List<MajorVersion>
   {
       new MajorVersion
       {
           Value = "14",
           MinorVersions = new List<MinorVersion>
           {
               new MinorVersion
               {
                   Value = "14.17",
                   StackSettings = new StackSettings
                   {
                       WindowsRuntimeSettings = new WindowsRuntimeSettings
                       {
                           RuntimeVersion = "Node|14.17"
                       }
                   }
               },
               new MinorVersion
               {
                   Value = "14.20 LTS", // Ensure an LTS version exists
                   StackSettings = new StackSettings
                   {
                       WindowsRuntimeSettings = new WindowsRuntimeSettings
                       {
                           RuntimeVersion = "Node|14.20 LTS"
                       }
                   }
               }
           }
       },
       new MajorVersion
       {
           Value = "22",
           MinorVersions = new List<MinorVersion>
           {
               new MinorVersion
               {
                   Value = "22.0",
                   StackSettings = new StackSettings
                   {
                       WindowsRuntimeSettings = new WindowsRuntimeSettings
                       {
                           RuntimeVersion = "Node|22.0"
                       }
                   }
               },
               new MinorVersion
               {
                   Value = "22.0 LTS", // Ensure an LTS version exists
                   StackSettings = new StackSettings
                   {
                       WindowsRuntimeSettings = new WindowsRuntimeSettings
                       {
                           RuntimeVersion = "Node|22.0 LTS"
                       }
                   }
               }
           }
       }
   }
}
           },
           new Language
{
   Name = "powershell",
   Properties = new Properties
   {
       DisplayText = "PowerShell",
       MajorVersions = new List<MajorVersion>
       {
           new MajorVersion
           {
               Value = "7",
               MinorVersions = new List<MinorVersion>
               {
                   new MinorVersion
                   {
                       Value = "7",
                       StackSettings = new StackSettings
                       {
                           WindowsRuntimeSettings = new WindowsRuntimeSettings
                           {
                               RuntimeVersion = "PowerShell|7"
                           }
                       }
                   },
                   new MinorVersion
                   {
                       Value = "7.2 LTS",
                       StackSettings = new StackSettings
                       {
                           WindowsRuntimeSettings = new WindowsRuntimeSettings
                           {
                               RuntimeVersion = "PowerShell|7.2 LTS"
                           }
                       }
                   }
               }
           }
       }
   }
}
       }
            };
        }

        [Theory]
        [InlineData("node", "14", "22")] // Node.js 14 should return next supported 22
        public void GetNextRuntimeNodeVersion_ShouldReturnCorrectVersion(string runtime, string currentVersion, string expectedNextVersion)
        {
            // Arrange
            var stacks = GetMockFunctionStacks();

            // Act
            var (nextVersion, _) = stacks.GetNextRuntimeVersion(runtime, currentVersion, p => p.MajorVersions.Select(mv => mv.Value), isNumericVersion: true);

            // Assert
            Assert.Equal(expectedNextVersion, nextVersion);
        }

        [Theory]
        [InlineData("python", "3.8", "3.12")] // Python 3.8 should return next supported 3.12
        public void GetNextRuntimePythonVersion_ShouldReturnCorrectVersion(string runtime, string currentVersion, string expectedNextVersion)
        {
            // Arrange
            var stacks = GetMockFunctionStacks();

            // Act
            var (nextVersion, _) = stacks.GetNextRuntimeVersion(runtime, currentVersion, p => p.MajorVersions.SelectMany(mv => mv.MinorVersions, (major, minor) => minor.Value));

            // Assert
            Assert.Equal(expectedNextVersion, nextVersion);
        }

        [Theory]
        [InlineData("node", "14.17", true)] // Test for a known valid version
        [InlineData("node", "14.20 LTS", true)] // Test for an LTS version
        public void GetRuntimeSettingsForNode_ShouldReturnValidSettings(string runtime, string version, bool expectedNotNull)
        {
            // Arrange
            var stacks = GetMockFunctionStacks();

            // Act
            var settings = stacks.GetOtherRuntimeSettings(runtime, version, s => s.WindowsRuntimeSettings);

            // Assert
            Assert.Equal(expectedNotNull, settings != null);
        }

        [Theory]
        [InlineData("python", "3.8", true)] // Python 3.8 should return runtime settings
        public void GetRuntimeSettingsForPython_ShouldReturnValidSettings(string runtime, string version, bool expectedNotNull)
        {
            // Arrange
            var stacks = GetMockFunctionStacks();

            // Act
            var settings = stacks.GetOtherRuntimeSettings(runtime, version, s => s.LinuxRuntimeSettings);

            // Assert
            Assert.Equal(expectedNotNull, settings != null);
        }

        [Theory]
        [InlineData("powershell", "7", true)] // PowerShell 7 should return runtime settings
        [InlineData("powershell", "7.2 LTS", true)] // PowerShell 7.2 LTS should return runtime settings
        public void GetRuntimeSettingsForPowerShell_ShouldReturnValidSettings(string runtime, string version, bool expectedNotNull)
        {
            // Arrange
            var stacks = GetMockFunctionStacks();

            // Act
            var settings = stacks.GetOtherRuntimeSettings(runtime, version, s => s.WindowsRuntimeSettings);

            // Assert
            Assert.Equal(expectedNotNull, settings != null);
        }

        [Theory]
        [InlineData("node", "22", "22")] // Node.js 22 is highest → should return itself
        [InlineData("python", "3.12", "3.12")] // Python 3.12 is highest → should return itself
        public void GetNextRuntimeVersion_ShouldReturnCurrentIfNoNewerExists(string runtime, string currentVersion, string expectedVersion)
        {
            // Arrange
            var stacks = GetMockFunctionStacks();
            var selector = runtime == "node"
                ? (Func<Properties, IEnumerable<string>>)(p => p.MajorVersions.Select(mv => mv.Value))
                : p => p.MajorVersions.SelectMany(mv => mv.MinorVersions.Select(minor => minor.Value));
            bool isNumeric = runtime == "node";

            // Act
            var (nextVersion, _) = stacks.GetNextRuntimeVersion(runtime, currentVersion, selector, isNumeric);

            // Assert
            Assert.Equal(expectedVersion, nextVersion);
        }

        [Fact]
        public void GetNextRuntimeVersion_ShouldReturnNull_WhenNoVersionsAvailable()
        {
            // Arrange
            var stacks = new FunctionsStacks
            {
                Languages = new List<Language>
                {
                    new Language
                    {
                        Name = "java",
                        Properties = new Properties
                        {
                            DisplayText = "Java",
                            MajorVersions = new List<MajorVersion>() // No versions
                        }
                    }
                }
            };

            // Act
            var (nextVersion, displayName) = stacks.GetNextRuntimeVersion(
                "java", "11", p => p.MajorVersions.Select(mv => mv.Value), isNumericVersion: true);

            // Assert
            Assert.Null(nextVersion);
            Assert.Equal("Java", displayName);
        }

        private class TestAzureHelperService : AzureHelperService
        {
            public TestAzureHelperService()
                : base(null, null)
            {
                UpdatedSettings = new Dictionary<string, string>();
            }

            public Dictionary<string, string> UpdatedSettings { get; private set; }

            public override Task<HttpResult<string, string>> UpdateWebSettings(Site functionApp, Dictionary<string, string> updatedSettings)
            {
                UpdatedSettings = updatedSettings;
                return Task.FromResult(new HttpResult<string, string>(string.Empty));
            }
        }
    }
}
