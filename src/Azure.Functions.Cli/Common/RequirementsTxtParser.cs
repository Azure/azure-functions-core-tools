using Azure.Functions.Cli.Common;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.IO;

namespace Azure.Functions.Cli.Common
{
    // Requirements.txt PEP https://www.python.org/dev/peps/pep-0440/
    public static class RequirementsTxtParser
    {
        public static async Task<List<PythonPackage>> ParseRequirementsTxtFile(string functionAppRoot)
        {
            // Check if requirements.txt exist
            string requirementsTxtPath = Path.Join(functionAppRoot, Constants.RequirementsTxt);
            if (!FileSystemHelpers.FileExists(requirementsTxtPath))
            {
                return new List<PythonPackage>();
            }

            // Parse requirements.txt line by line
            string fileContent = await FileSystemHelpers.ReadAllTextFromFileAsync(requirementsTxtPath);
            return ParseRequirementsTxtContent(fileContent);
        }

        public static List<PythonPackage> ParseRequirementsTxtContent(string fileContent)
        {
            string pattern = @"^(?<name>(\w|\-|_|\.)+\s*)((?<spec>(===|==|<=|>=|!=|~=|>|<)[(\d|\w\d)\.]*[^;@])?)((;(?<envmarker>[^@]+))?)((@(?<directref>[^$]+))?)$";
            Regex rx = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
            var packages = new ConcurrentBag<PythonPackage>();

            fileContent.Split('\r', '\n').Where(l => !string.IsNullOrWhiteSpace(l)).AsParallel().ForAll(line => {
                Match match = rx.Match(line);

                if (match.Success)
                {
                    GroupCollection groups = match.Groups;

                    Group packageName = groups["name"];
                    Group packageSpec = groups["spec"];
                    Group packageEnvMarker = groups["envmarker"];
                    Group packageDirectRef = groups["directref"];

                    packages.Add(new PythonPackage()
                    {
                        Name = packageName.Success ? packageName.Value.ToLower().Replace('_', '-').Replace('.', '-').Trim() : string.Empty,
                        Specification = packageSpec.Success ? packageSpec.Value.Trim() : string.Empty,
                        EnvironmentMarkers = packageEnvMarker.Success ? packageEnvMarker.Value.Trim() : string.Empty,
                        DirectReference = packageDirectRef.Success ? packageDirectRef.Value.Trim() : string.Empty
                    });
                }
            });

            return packages.ToList();
        }
    }
}
