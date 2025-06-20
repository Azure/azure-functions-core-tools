﻿using System.Linq;
using System.Net;
using static Build.BuildSteps;

namespace Build
{
    class Program
    {
        static void Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            Orchestrator
                .CreateForTarget(args)
                .Then(TestSignedArtifacts, skip: !args.Contains("--signTest"))
                .Then(Clean)
                .Then(LogIntoAzure, skip: !args.Contains("--ci"))
                .Then(UpdatePackageVersionForIntegrationTests, skip: !args.Contains("--integrationTests"))
                .Then(RestorePackages)
                .Then(ReplaceTelemetryInstrumentationKey, skip: !args.Contains("--ci"))
                .Then(DotnetPublishForZips)
                .Then(FilterPowershellRuntimes)
                .Then(FilterPythonRuntimes)
                .Then(AddTemplatesNupkgs)
                .Then(AddTemplatesJson)
                .Then(AddGoZip)
                .Then(TestPreSignedArtifacts, skip: !args.Contains("--ci"))
                .Then(CopyBinariesToSign, skip: !args.Contains("--ci"))
                .Then(TestNewE2EProject)
                .Then(TestNewE2EProjectDotnetInProc)
                .Then(Test)
                .Then(Zip)
                .Then(DotnetPublishForNupkg)
                .Then(DotnetPack)
                .Then(CreateIntegrationTestsBuildManifest, skip: !args.Contains("--integrationTests"))
                .Run();
        }
    }
}

