using Azure.Functions.Cli.Common;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Azure.Functions.Cli
{
    internal class ArtifactsConfigurationBuilder : IConfigureBuilder<IConfigurationBuilder>
    {
        public void Configure(IConfigurationBuilder builder)
        {
            builder.AddJsonFile(Constants.ArtifactsConfigFileName, optional: true);        
        }
    }
}
