using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Azure.Functions.Cli
{
    internal class ArtifactsConfigurationBuilder : IConfigureBuilder<IConfigurationBuilder>
    {
        public void Configure(IConfigurationBuilder builder)
        {
            builder.AddJsonFile("artifactsconfig.json", optional: true);        
        }
    }
}
