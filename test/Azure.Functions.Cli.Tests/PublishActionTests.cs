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

namespace Azure.Functions.Cli.Tests
{
    public class PublishActionTests
    {
        private readonly TestAzureHelperService _helperService = new TestAzureHelperService();

        [Theory]
        [InlineData(null, "6.0")]
        [InlineData("something", "6.0")]
        [InlineData("6.0", "6.0")]
        [InlineData("7.0", "7.0")]
        [InlineData("9.0", "9.0")]
        public async Task NetFrameworkVersion_DotnetIsolated_Linux_Consumption_Updated(string initialLinuxFxVersion, string expectedNetFrameworkVersion)
        {
            var site = new Site("test")
            {
                Kind = "linux",
                Sku = "dynamic",
                LinuxFxVersion = initialLinuxFxVersion
            };

            await PublishFunctionAppAction.UpdateFrameworkVersions(site, WorkerRuntime.DotnetIsolated, expectedNetFrameworkVersion, false, _helperService);

            // update it to empty
            var setting = _helperService.UpdatedSettings.Single();
            Assert.Equal(Constants.LinuxFxVersion, setting.Key);
            Assert.Equal($"DOTNET-ISOLATED|{expectedNetFrameworkVersion}", setting.Value);
        }

        [Theory]
        [InlineData("v6.0")]
        [InlineData("6.0")]
        [InlineData("6.0.1")]
        public async Task NetFrameworkVersion_DotnetIsolated_Linux_Dedicated(string specifiedVersion)
        {
            var site = new Site("test")
            {
                Kind = "linux"
            };

            await PublishFunctionAppAction.UpdateFrameworkVersions(site, WorkerRuntime.DotnetIsolated, specifiedVersion, false, _helperService);

            var setting = _helperService.UpdatedSettings.Single();
            Assert.Equal(Constants.LinuxFxVersion, setting.Key);
            Assert.Equal("DOTNET-ISOLATED|6.0", setting.Value);
        }

        [Theory]
        [InlineData("v6.0")]
        [InlineData("6.0")]
        [InlineData("6.0.1")]
        public async Task NetFrameworkVersion_DotnetIsolated_Windows(string specifiedVersion)
        {
            var site = new Site("test");

            await PublishFunctionAppAction.UpdateFrameworkVersions(site, WorkerRuntime.DotnetIsolated, specifiedVersion, false, _helperService);

            var setting = _helperService.UpdatedSettings.Single();
            Assert.Equal(Constants.DotnetFrameworkVersion, setting.Key);
            Assert.Equal("v6.0", setting.Value);
        }

        [Fact]
        public async Task NetFrameworkVersion_DotnetIsolated_Linux_Null()
        {
            // If not specified, assume 8.0
            var site = new Site("test")
            {
                Kind = "linux"
            };

            await PublishFunctionAppAction.UpdateFrameworkVersions(site, WorkerRuntime.DotnetIsolated, null, false, _helperService);

            var setting = _helperService.UpdatedSettings.Single();
            Assert.Equal(Constants.LinuxFxVersion, setting.Key);
            Assert.Equal("DOTNET-ISOLATED|8.0", setting.Value);
        }

        [Fact]
        public async Task NetFrameworkVersion_DotnetIsolated_Windows_Null()
        {
            // If not specified, assume 8.0
            var site = new Site("test");

            await PublishFunctionAppAction.UpdateFrameworkVersions(site, WorkerRuntime.DotnetIsolated, null, false, _helperService);

            var setting = _helperService.UpdatedSettings.Single();
            Assert.Equal(Constants.DotnetFrameworkVersion, setting.Key);
            Assert.Equal("v8.0", setting.Value);
        }

        [Fact]
        public async Task NetFrameworkVersion_Dotnet_Windows_Null()
        {
            var site = new Site("test")
            {
                NetFrameworkVersion = "v4.0"
            };

            // If not supported specified, assume 8.0
            await PublishFunctionAppAction.UpdateFrameworkVersions(site, WorkerRuntime.Dotnet, null, false, _helperService);

            var setting = _helperService.UpdatedSettings.Single();
            Assert.Equal(Constants.DotnetFrameworkVersion, setting.Key);
            Assert.Equal("v8.0", setting.Value);
        }

        [Fact]
        public async Task NetFrameworkVersion_Dotnet_Windows_NoOp()
        {
            var site = new Site("test")
            {
                NetFrameworkVersion = "v8.0"
            };

            await PublishFunctionAppAction.UpdateFrameworkVersions(site, WorkerRuntime.Dotnet, null, false, _helperService);

            // Should be a no-op as site is already v6.0
            Assert.Null(_helperService.UpdatedSettings);
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("6.0.a.b")]
        public async Task NetFrameworkVersion_Invalid(string specifiedVersion)
        {
            var site = new Site("test");

            var exception = await Assert.ThrowsAsync<CliException>(() =>
                PublishFunctionAppAction.UpdateFrameworkVersions(site, WorkerRuntime.DotnetIsolated, specifiedVersion, false, _helperService));

            Assert.StartsWith($"The dotnet-version value of '{specifiedVersion}' is invalid.", exception.Message);
        }

        [Theory]
        [InlineData("dotnet-isolated", WorkerRuntime.DotnetIsolated)]
        [InlineData("c#-isolated", WorkerRuntime.DotnetIsolated)]
        [InlineData("csharp", WorkerRuntime.Dotnet)]
        [InlineData("typescript", WorkerRuntime.Node)]
        public void NormalizeWorkerRuntime_ReturnsExpectedWorkerRuntime(string input, WorkerRuntime expected)
        {

            var result = WorkerRuntimeLanguageHelper.NormalizeWorkerRuntime(input);

            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("invalid-runtime")]
        [InlineData("unknown")]
        [InlineData("dotnet-isol")]
        [InlineData("c-sharp")]
        [InlineData("")]
        [InlineData(null)]
        public void NormalizeWorkerRuntime_InvalidInput(string inputString)
        {
            if (string.IsNullOrWhiteSpace(inputString))
            {
                var exception = Assert.Throws<ArgumentNullException>(() => WorkerRuntimeLanguageHelper.NormalizeWorkerRuntime(inputString));
                Assert.StartsWith($"Worker runtime cannot be null or empty.", exception.Message);
                Assert.Equal("workerRuntime", exception.ParamName);
            }
            else
            {
                var exception = Assert.Throws<ArgumentException>(() => WorkerRuntimeLanguageHelper.NormalizeWorkerRuntime(inputString));
                Assert.Equal($"Worker runtime '{inputString}' is not a valid option. Options are {WorkerRuntimeLanguageHelper.AvailableWorkersRuntimeString}", exception.Message);
            }
        }

        [Fact]
        public void ValidateFunctionAppPublish_ThrowException_WhenWorkerRuntimeIsNone()
        {
            GlobalCoreToolsSettings.SetWorkerRuntime(WorkerRuntime.None);

            var ex = Assert.Throws<CliException>(() => GlobalCoreToolsSettings.CurrentWorkerRuntime);
            Assert.Equal($"Worker runtime cannot be '{WorkerRuntime.None}'. Please set a valid runtime.", ex.Message);
        }

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
        [InlineData("node", "14.17", true)]  // Test for a known valid version
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
        [InlineData("powershell", "7", true)]  // PowerShell 7 should return runtime settings
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

        private class TestAzureHelperService : AzureHelperService
        {
            public Dictionary<string, string> UpdatedSettings { get; private set; }

            public TestAzureHelperService()
                : base(null, null)
            {
            }

            public override Task<HttpResult<string, string>> UpdateWebSettings(Site functionApp, Dictionary<string, string> updatedSettings)
            {
                UpdatedSettings = updatedSettings;
                return Task.FromResult(new HttpResult<string, string>(string.Empty));
            }
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
    }
}
