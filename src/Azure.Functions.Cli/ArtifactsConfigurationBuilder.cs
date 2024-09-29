using Azure.Functions.Cli.Common;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Azure.Functions.Cli
{
    internal class ArtifactsConfigurationBuilder : IConfigureBuilder<IConfigurationBuilder>
    {
        public void Configure(IConfigurationBuilder builder)
        {
            var artifactsConfigFilePath = Path.Combine(Constants.ArtifactsConfigFileName);
            Console.WriteLine($"In ArtifactsConfigurationBuilder : {artifactsConfigFilePath}");
            builder.AddJsonFile(artifactsConfigFilePath, optional: true);        
        }
    }
}
