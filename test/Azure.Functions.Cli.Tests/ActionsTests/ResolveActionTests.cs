using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ARMClient.Authentication;
using ARMClient.Authentication.AADAuthentication;
using ARMClient.Authentication.Contracts;
using ARMClient.Library;
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

namespace Azure.Functions.Cli.Tests.ActionsTests
{
    public class ResolveActionTests
    {
        [Theory]
        [InlineData("azure functionapp enable-git-repo appName", typeof(EnableGitRepoAction))]
        [InlineData("azure functionapp fetch-app-settings appName", typeof(FetchAppSettingsAction))]
        [InlineData("azure functionapp fetch appName", typeof(FetchAppSettingsAction))]
        [InlineData("azure get-publish-username", typeof(GetPublishUserNameAction))]
        [InlineData("azure account list", typeof(ListAzureAccountsAction))]
        [InlineData("azure subscriptions list", typeof(ListAzureAccountsAction))]
        [InlineData("azure functionapp list", typeof(ListFunctionAppsAction))]
        [InlineData("azure storage list", typeof(ListStorageAction))]
        [InlineData("azure login", typeof(LoginAction))]
        [InlineData("azure logout", typeof(LogoutAction))]
        [InlineData("azure functionapp logstream appName", typeof(LogStreamAction))]
        [InlineData("azure portal appName", typeof(PortalAction))]
        [InlineData("azure account set accountName", typeof(SetAzureAccountAction))]
        [InlineData("azure set-publish-password userName", typeof(SetPublishPasswordAction))]
        [InlineData("azure set-publish-username userName", typeof(SetPublishPasswordAction))]
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
        [InlineData("run functionName", typeof(RunFunctionAction))]
        [InlineData("function run functionName", typeof(RunFunctionAction))]
        [InlineData("-v", typeof(HelpAction))]
        [InlineData("-version", typeof(HelpAction))]
        [InlineData("--version", typeof(HelpAction))]
        [InlineData("", typeof(HelpAction))]
        [InlineData("help", typeof(HelpAction))]
        [InlineData("WrongName", typeof(HelpAction))]
        public void ResolveCommandLineCorrectly(string args, Type type)
        {
            var container = InitializeContainerForTests();
            var app = new ConsoleApp(args.Split(' ').ToArray(), type.Assembly, container);
            var result = app.Parse();
            result.Should().BeOfType(type);
        }

        private IContainer InitializeContainerForTests()
        {
            var builder = new ContainerBuilder();

            builder.RegisterType<FunctionsLocalServer>()
                .As<IFunctionsLocalServer>();

            builder.Register(c => new PersistentAuthHelper { AzureEnvironments = AzureEnvironments.Prod })
                .As<IAuthHelper>();

            builder.Register(_ => new PersistentSettings())
                .As<ISettings>()
                .SingleInstance()
                .ExternallyOwned();

            builder.Register(c => new AzureClient(retryCount: 3, authHelper: c.Resolve<IAuthHelper>()))
                .As<IAzureClient>();

            builder.RegisterType<ArmManager>()
                .As<IArmManager>();

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
