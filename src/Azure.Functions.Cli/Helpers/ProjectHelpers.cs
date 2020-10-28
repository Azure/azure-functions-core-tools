using System.Linq;
using Microsoft.Build.Construction;
using Azure.Functions.Cli.Common;
using System.IO;
using System.Xml;
using System;
using Azure.Functions.Cli.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Helpers
{
    internal static class ProjectHelpers
    {
        public static string GetUserSecretsId(string scriptPath, LoggingFilterHelper loggingFilterHelper, LoggerFilterOptions loggerFilterOptions)
        {
            if (string.IsNullOrEmpty(scriptPath))
            {
                return null;
            }
            string projectFilePath = ProjectHelpers.FindProjectFile(scriptPath, loggingFilterHelper, loggerFilterOptions);
            if (projectFilePath == null)
            {
                return null;
            }

            var projectRoot = ProjectHelpers.GetProject(projectFilePath);
            var userSecretsId = ProjectHelpers.GetPropertyValue(projectRoot, Constants.UserSecretsIdElementName);

            return userSecretsId;
        }

        public static string FindProjectFile(string path, LoggingFilterHelper loggingFilterHelper, LoggerFilterOptions loggerFilterOptions)
        {
            ColoredConsoleLogger logger = new ColoredConsoleLogger("ProjectHelpers", loggingFilterHelper, loggerFilterOptions);
            DirectoryInfo filePath = new DirectoryInfo(path);
            do
            {
                var projectFiles = filePath.GetFiles("*.csproj");
                if (projectFiles.Any())
                {
                    foreach (FileInfo file in projectFiles)
                    {
                        if (string.Equals(projectFiles[0].Name, Constants.ExtenstionsCsProjFile, StringComparison.OrdinalIgnoreCase)) continue;
                        logger.LogDebug($"Found {file.FullName}. Using for user secrets file configuration.");
                        return file.FullName;
                    }
                }
                filePath = filePath.Parent;
            }
            while (filePath.FullName != filePath.Root.FullName);

            logger.LogDebug($"Csproj not found in {path} directory tree. Skipping user secrets file configuration.");
            return null;
        }

        public static ProjectRootElement GetProject(string path)
        {
            ProjectRootElement root = null;

            if (File.Exists(path))
            {
                var reader = XmlTextReader.Create(new StringReader(File.ReadAllText(path)));
                root = ProjectRootElement.Create(reader);
            }

            return root;
        }

        public static bool PackageReferenceExists(this ProjectRootElement project, string packageId)
        {
            ProjectItemElement existingPackageReference = project.Items
                .FirstOrDefault(item => item.ItemType == Constants.PackageReferenceElementName && item.Include.ToLowerInvariant() == packageId.ToLowerInvariant());
            return existingPackageReference != null;
        }

        public static string GetPropertyValue(this ProjectRootElement project, string propertyName)
        {
            var property = project.Properties
                .FirstOrDefault(item => string.Equals(item.Name, propertyName, StringComparison.OrdinalIgnoreCase));

            return property == null ? null : property.Value;
        }


    }
}
