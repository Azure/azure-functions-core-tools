using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;

namespace Azure.Functions.Cli.Extensions
{
    public static class HttpExtension
    {
        /// <summary>
        /// Clones HttpRequestMessage
        /// </summary>
        /// <param name="request">The HttpRequestMessage to be cloned</param>
        /// <returns></returns>
        public static HttpRequestMessage Clone(this HttpRequestMessage request)
        {
            if (request == null)
            {
                return null;
            }

            var clone = new HttpRequestMessage(request.Method, request.RequestUri)
            {
                Content = request.Content.Clone(),
                Version = request.Version
            };
            foreach (KeyValuePair<string, object> prop in request.Properties)
            {
                clone.Properties.Add(prop);
            }
            foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return clone;
        }

        /// <summary>
        /// Clones HttpContent object
        /// </summary>
        /// <param name="content">HttpContent to be cloned</param>
        /// <returns>The cloned HttpContent object</returns>
        public static HttpContent Clone(this HttpContent content)
        {
            if (content == null)
            {
                return null;
            }

            var ms = new MemoryStream();
            content.CopyToAsync(ms).Wait();
            ms.Position = 0;

            var clone = new StreamContent(ms);
            foreach (KeyValuePair<string, IEnumerable<string>> header in content.Headers)
            {
                clone.Headers.Add(header.Key, header.Value);
            }
            return clone;
        }
    }
}
