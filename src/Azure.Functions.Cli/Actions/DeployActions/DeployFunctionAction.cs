using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Azure.Functions.Cli.Actions.DeployActions.Platforms;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Fclp;
using Microsoft.Azure.WebJobs.Script;
using static Azure.Functions.Cli.Common.OutputTheme;
using static Colors.Net.StringStaticMethods;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "deploy", HelpText = "Deploy a function app to custom hosting backends")]
    internal class DeployAction : BaseAction
    {
        public string Registry { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public int MinInstances { get; set; } = 1;
        public int MaxInstances { get; set; } = 1000;
        public string FolderName { get; set; } = string.Empty;
        public string ConfigPath { get; set; } = string.Empty;

        public List<string> Platforms { get; set; } = new List<string>() { "kubernetes" };
        private readonly ITemplatesManager _templatesManager;

        public DeployAction(ITemplatesManager templatesManager)
        {
            _templatesManager = templatesManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>("registry")
                .WithDescription("A Docker Registry name that you are logged into")
                .Callback(t => Registry = t).Required();

            Parser
                .Setup<string>("platform")
                .WithDescription("Hosting platform for the function app. Valid options: " + String.Join(",", Platforms))
                .Callback(t => Platform = t).Required();

            Parser
                .Setup<string>("name")
                .WithDescription("Function name")
                .Callback(t => Name = t).Required();

            Parser
                .Setup<int>("min")
                .WithDescription("[Optional] Minimum number of function instances")
                .Callback(t => MinInstances = t);

            Parser
                .Setup<int>("max")
                .WithDescription("[Optional] Maximum number of function instances")
                .Callback(t => MaxInstances = t);

            Parser
                .Setup<string>("config")
                .WithDescription("[Optional] Config file")
                .Callback(t => ConfigPath = t);

            if (args.Any() && !args.First().StartsWith("-"))
            {
                FolderName = args.First();
            }

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            if (!string.IsNullOrEmpty(FolderName))
            {
                var folderPath = Path.Combine(Environment.CurrentDirectory, FolderName);
                FileSystemHelpers.EnsureDirectory(folderPath);
                Environment.CurrentDirectory = folderPath;
            }

            if (!Platforms.Contains(Platform))
            {
                ColoredConsole.Error.WriteLine(ErrorColor($"platform {Platform} is not supported. Valid options are: {String.Join(",", Platforms)}"));
                return;
            }

            if (!CommandChecker.CommandExists("kubectl"))
            {
                ColoredConsole.Error.WriteLine(ErrorColor($"kubectl is required for deploying to kubernetes. Please make sure to install kubectl and try again."));
                return;
            }

            var dockerFilePath = Path.Combine(Environment.CurrentDirectory, "Dockerfile");

            if (!FileSystemHelpers.FileExists(dockerFilePath))
            {
                ColoredConsole.Error.WriteLine(ErrorColor($"Dockerfile not found in directory {Environment.CurrentDirectory}"));
                return;
            }

            var image = $"{Registry}/{Name}";

            ColoredConsole.WriteLine("Building Docker image...");
            await DockerHelpers.DockerBuild(image, Environment.CurrentDirectory);

            ColoredConsole.WriteLine("Pushing function image to registry...");
            await DockerHelpers.DockerPush(image);

            var platform = PlatformFactory.CreatePlatform(Platform, ConfigPath);

            if (platform == null)
            {
                ColoredConsole.Error.WriteLine(ErrorColor($"Platform {Platform} is not supported"));
                return;
            }

            await platform.DeployContainerizedFunction(Name, image, MinInstances, MaxInstances);
        }
    }
}
