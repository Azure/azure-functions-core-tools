using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace Build
{
    public static class Settings
    {
        static Settings()
        {
            Directory.CreateDirectory(OutputDir);
        }

        private static string config(string @default = null, [CallerMemberName] string key = null)
        {
            var value = System.Environment.GetEnvironmentVariable(key);
            return string.IsNullOrEmpty(value)
                ? @default
                : value;
        }

        public static readonly string SrcProjectPath = Path.GetFullPath("../src/Azure.Functions.Cli/");

        public static readonly string TestProjectPath = Path.GetFullPath("../test/Azure.Functions.Cli.Tests/");

        public static readonly string ProjectFile = Path.Combine(SrcProjectPath, "Azure.Functions.Cli.csproj");

        public static readonly string TestProjectFile = Path.Combine(TestProjectPath, "Azure.Functions.Cli.Test.csproj");

        public static readonly string[] TargetRuntimes = new[] { "win-x86", "win-x64", "linux-x64", "osx-x64" };

        public const string DistLibVersion = "distlib-15dba58a827f56195b0fa0afe80a8925a92e8bf5";

        public const string DistLibUrl = "https://github.com/vsajip/distlib/archive/15dba58a827f56195b0fa0afe80a8925a92e8bf5.zip";

        public const string PythonWorkerUrl = "";
        public const string Pu

        public static readonly string OutputDir = Path.Combine(Path.GetFullPath(".."), "artifacts");
    }
}