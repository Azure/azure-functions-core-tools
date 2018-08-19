using System;
using System.Linq;
using Autofac;
using FluentAssertions;
using Azure.Functions.Cli.Actions;
using Azure.Functions.Cli.Actions.AzureActions;
using Azure.Functions.Cli.Actions.HostActions;
using Azure.Functions.Cli.Actions.LocalActions;
using Azure.Functions.Cli.Arm;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Interfaces;
using NSubstitute;
using Xunit;
using System.IO.Abstractions;

namespace Azure.Functions.Cli.Tests.ActionsTests
{
    public class ResolveActionTests
    {
        [Theory]
        [InlineData("azure functionapp enable-git-repo appName", typeof(DeprecatedAzureActions))]
        [InlineData("azure functionapp fetch-app-settings appName", typeof(FetchAppSettingsAction))]
        [InlineData("azure functionapp fetch appName", typeof(FetchAppSettingsAction))]
        [InlineData("azure get-publish-username", typeof(DeprecatedAzureActions))]
        [InlineData("azure account list", typeof(DeprecatedAzureActions))]
        [InlineData("azure subscriptions list", typeof(DeprecatedAzureActions))]
        [InlineData("azure functionapp list", typeof(DeprecatedAzureActions))]
        [InlineData("azure storage list", typeof(DeprecatedAzureActions))]
        [InlineData("azure login", typeof(DeprecatedAzureActions))]
        [InlineData("azure logout", typeof(DeprecatedAzureActions))]
        [InlineData("azure functionapp logstream appName", typeof(LogStreamAction))]
        [InlineData("azure portal appName", typeof(DeprecatedAzureActions))]
        [InlineData("azure account set accountName", typeof(DeprecatedAzureActions))]
        [InlineData("azure set-publish-password userName", typeof(DeprecatedAzureActions))]
        [InlineData("azure set-publish-username userName", typeof(DeprecatedAzureActions))]
        [InlineData("host start", typeof(StartHostAction))]
        [InlineData("settings add settingName", typeof(AddSettingAction))]
        [InlineData("azure storage fetch-connection-string storageName", typeof(AddStorageAccountSettingAction))]
        [InlineData("new", typeof(CreateFunctionAction))]
        [InlineData("function new", typeof(CreateFunctionAction))]
        [InlineData("function create", typeof(CreateFunctionAction))]
        [InlineData("settings decrypt", typeof(DecryptSettingAction))]
        [InlineData("settings encrypt", typeof(EncryptSettingsAction))]
        [InlineData("settings delete settingName", typeof(DeleteSettingAction))]
        [InlineData("settings list", typeof(ListSettingsAction))]
        [InlineData("init", typeof(InitAction))]
        [InlineData("-v", null)]
        [InlineData("-version", null)]
        [InlineData("--version", null)]
        [InlineData("", typeof(HelpAction))]
        [InlineData("help", typeof(HelpAction))]
        [InlineData("WrongName", typeof(HelpAction))]
        [InlineData("azure functionapp --help", typeof(HelpAction))]
        [InlineData("azure --help", typeof(HelpAction))]
        [InlineData("--help", typeof(HelpAction))]
        public void ResolveCommandLineCorrectly(string args, Type returnType)
        {
            var fileSystem = Substitute.For<IFileSystem>();
            fileSystem.File.Exists(Arg.Any<string>()).Returns(true);
            FileSystemHelpers.Instance = fileSystem;

            var container = InitializeContainerForTests();
            var app = new ConsoleApp(args.Split(' ').ToArray(), typeof(Program).Assembly, container);
            var result = app.Parse();
            if (returnType == null)
            {
                result.Should().BeNull();
            }
            else
            {
                result.Should().BeOfType(returnType);
            }
        }

        private IContainer InitializeContainerForTests()
        {
            var builder = new ContainerBuilder();

            builder.RegisterType<FunctionsLocalServer>()
                .As<IFunctionsLocalServer>();

            builder.Register(_ => new PersistentSettings())
                .As<ISettings>()
                .SingleInstance();

            builder.RegisterType<ProcessManager>()
                .As<IProcessManager>();

            var mockedSecretsManager = Substitute.For<ISecretsManager>();
            builder.RegisterInstance(mockedSecretsManager)
                .As<ISecretsManager>();
            mockedSecretsManager.GetHostStartSettings().Returns(new HostStartSettings());

            builder.RegisterType<TemplatesManager>()
                .As<ITemplatesManager>();

            return builder.Build();
        }
    }
}
