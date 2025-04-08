namespace Azure.Functions.Cli.Arm.Models
{
    internal class ArmWrapper<T>
    {
        public string id { get; set; }

        public string name { get; set; }

        public string type { get; set; }

        public string location { get; set; }

        public string kind { get; set; }

        public T properties { get; set; }
    }
}