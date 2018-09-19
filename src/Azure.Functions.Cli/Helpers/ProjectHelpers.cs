using System.Linq;
using Microsoft.Build.Construction;
using Azure.Functions.Cli.Common;
using System.Threading.Tasks;
using System.IO;
using System.Xml;

namespace Azure.Functions.Cli.Helpers
{
    internal static class ProjectHelpers
    {
        public static bool PackageReferenceExists(this ProjectRootElement project, string packageId)
        {
            ProjectItemElement existingPackageReference = project.Items
                .FirstOrDefault(item => item.ItemType == Constants.PackageReferenceElementName && item.Include.ToLowerInvariant() == packageId.ToLowerInvariant());
            return existingPackageReference != null;
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
    }
}
