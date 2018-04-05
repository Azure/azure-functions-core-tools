using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Colors.Net;
using Newtonsoft.Json;
using Azure.Functions.Cli.Interfaces;
using Newtonsoft.Json.Linq;

namespace Azure.Functions.Cli.Common
{
    internal class TemplatesManager : ITemplatesManager
    {
        public Task<IEnumerable<Template>> Templates
        {
            get
            {
                return GetTemplates();
            }
        }

        public IEnumerable<Template> PythonTemplates
        {
            get
            {
                return new[]
                {
                    new Template
                    {
                        Files = new Dictionary<string, string>
                        {
                            { "main.py", @"import logging

import azure.functions as func

def main(req: func.HttpRequest) -> func.HttpResponse:
    logging.info('Python HTTP trigger function processed a request.')

    name = req.params.get('name')
    if not name:
        try:
            req_body = req.get_json()
        except ValueError:
            pass
        else:
            name = req_body.get('name')

    if name:
        return func.HttpResponse(f""Hello {name}!"")
    else:
        return func.HttpResponse(
             ""Please pass a name on the query string or in the request body"",
             status_code = 400
)"}
                        },
                        Id = "python-func",
                        Function = JsonConvert.DeserializeObject<JObject>(@"{
  'scriptFile': 'main.py',
  'bindings': [
    {
      'authLevel': 'anonymous',
      'type': 'httpTrigger',
      'direction': 'in',
      'name': 'req'
    },
    {
      'type': 'http',
      'direction': 'out',
      'name': '$return'
    }
  ]
}"),
                        Metadata = new TemplateMetadata
                        {
                            Language = "Python",
                            DefaultFunctionName = "HttpTriggerPython",
                            Name = "HttpTrigger"
                        }
                    }
                };
            }
        }

        private static async Task<IEnumerable<Template>> GetTemplates()
        {
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/");
                var templatesResponse = await client.GetAsync("https://functionscdn.azureedge.net/public/templates.json");
                return await templatesResponse.Content.ReadAsAsync<IEnumerable<Template>>();
            }
        }

        public async Task Deploy(string Name, Template template)
        {
            var path = Path.Combine(Environment.CurrentDirectory, Name);
            if (FileSystemHelpers.DirectoryExists(path))
            {
                var response = "n";
                do
                {
                    ColoredConsole.Write($"A directory with the name {Name} already exists. Overwrite [y/n]? [n] ");
                    response = Console.ReadLine();
                } while (response != "n" && response != "y");
                if (response == "n")
                {
                    return;
                }
            }

            if (FileSystemHelpers.DirectoryExists(path))
            {
                FileSystemHelpers.DeleteDirectorySafe(path, ignoreErrors: false);
            }

            FileSystemHelpers.EnsureDirectory(path);

            foreach (var file in template.Files)
            {
                var filePath = Path.Combine(path, file.Key);
                ColoredConsole.WriteLine($"Writing {filePath}");
                await FileSystemHelpers.WriteAllTextToFileAsync(filePath, file.Value);
            }
            var functionJsonPath = Path.Combine(path, "function.json");
            ColoredConsole.WriteLine($"Writing {functionJsonPath}");
            await FileSystemHelpers.WriteAllTextToFileAsync(functionJsonPath, JsonConvert.SerializeObject(template.Function, Formatting.Indented));
        }
    }
}