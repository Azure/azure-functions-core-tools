using System;
using Colors.Net;
using Colors.Net.StringColorExtensions;

namespace Build
{
    public class Orchestrator
    {
        public static Orchestrator StartWith(string targetToRun, Action target, string name = null, bool skip = false)
            => new Orchestrator(targetToRun).ThenDo(target, name, skip);

        private Node head = null;
        private Node tail = null;
        private string targetToRun;

        private Orchestrator(string targetToRun)
        {
            this.targetToRun = targetToRun;
        }

        public Orchestrator ThenDo(Action target, string name = null, bool skip = false)
        {
            var node = new Node
            {
                Target = target,
                Name = name ?? target.Method.Name,
                Skip = skip
            };

            if (head == null)
            {
                head = node;
                tail = head;
            }
            else
            {
                tail.Next = node;
                node.Previous = tail;
                tail = node;
            }

            return this;
        }

        public void Run()
        {
            var tmp = tail;
            var end = head;
            while (end != null &&
                   !string.IsNullOrWhiteSpace(targetToRun) &&
                   !end.Name.Equals(targetToRun))
            {
                end = end.Next;
            }

            if (end == null)
            {
                ColoredConsole.Error.WriteLine("No target was found to start".Red());
                return;
            }

            PrintTargetsToRun(tmp, end);

            while (tmp != null)
            {
                if (!tmp.Skip)
                {
                    ColoredConsole.WriteLine($"Running Target {tmp.Name}".Cyan());
                    tmp.Target();
                }
                if (tmp == end)
                {
                    break;
                }

                tmp = tmp.Previous;
            }
        }

        class Node
        {
            public Action Target;
            public string Name;
            public Node Next;
            public Node Previous;
            public bool Skip;
        }
    }
}