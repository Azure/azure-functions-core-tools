using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Azure.Functions.Cli.Common
{
    [DataContract]
    public class ProxyDefinition : IComparable<ProxyDefinition>
    {
        private static readonly char[] UriPathSeparator = new[] { '/' };

        private string[] cachedPathSegments;

        public bool IsBaseRoute { get; set; }

        [DataMember(Name = "matchCondition", EmitDefaultValue = false)]
        public MatchCondition Condition { get; set; }

        [DataMember(Name = "backendUri", EmitDefaultValue = false)]
        public object BackendUri { get; set; }

        [DataMember(Name = "debug", EmitDefaultValue = false)]
        public string Debug { get; set; }

        [DataMember(Name = "requestOverrides", EmitDefaultValue = false)]
        public Dictionary<string, object> RequestOverrides { get; set; }

        [DataMember(Name = "responseOverrides", EmitDefaultValue = false)]
        public Dictionary<string, object> ResponseOverrides { get; set; }

        [DataMember(Name = "disabled", EmitDefaultValue = false)]
        public bool Disabled { get; set; }

        internal string[] PathSegments
        {
            get
            {
                if (this.cachedPathSegments == null)
                {
                    // remove both leading and trailing / separators
                    this.cachedPathSegments = this.Condition.Route != null ?
                        this.Condition.Route.Trim('/').Split('/') :
                        new string[0];
                }

                return this.cachedPathSegments;
            }
        }

        /// <summary>
        /// Compares two <see cref="RouteDefinition"/> objects based on the number of segments.
        /// </summary>
        public int CompareTo(ProxyDefinition other)
        {
            // Sort the routes in reverse-order based on the number of path segments. This enables
            // us to do longest prefix-matching when evaluating request paths to routes.
            int result = other.PathSegments.Length.CompareTo(this.PathSegments.Length);
            if (result == 0)
            {
                bool leftIsUnspecified = this.Condition.HttpMethods == null || this.Condition.HttpMethods.Length == 0;
                bool rightIsUnspecified = other.Condition.HttpMethods == null || other.Condition.HttpMethods.Length == 0;

                // Specific method filters execute before wildcard method filters. If either
                // both are wildcard or both are non-wildcard, then treat them as equal.
                if (leftIsUnspecified && !rightIsUnspecified)
                {
                    return 1;
                }
                else if (!leftIsUnspecified && rightIsUnspecified)
                {
                    return -1;
                }
            }

            return result;
        }

        /// <summary>
        /// Gets a friendly string description of the route definition.
        /// </summary>
        public override string ToString()
        {
            string methodList = this.Condition.HttpMethods != null ? string.Join("|", this.Condition.HttpMethods) : "*";
            return string.Concat(methodList, " ", this.Condition.Route ?? "(undefined)");
        }
    }
}