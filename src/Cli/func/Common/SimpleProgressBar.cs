using Colors.Net;
using Microsoft.WindowsAzure.Storage.Core.Util;
using System;

namespace Azure.Functions.Cli.Common
{
    // Simplified https://gist.github.com/DanielSWolf/0ab6a96899cc5377bf54
    class SimpleProgressBar : IDisposable
    {
        private bool isCompleted = false;
        private readonly int width;
        private int completed;

        public SimpleProgressBar(string title)
        {
            try
            {
                var bufferWidth = Console.BufferWidth;
                bufferWidth = bufferWidth > 100 ? 100 : bufferWidth;
                this.width = bufferWidth - (title.Length + 4);

                ColoredConsole.Write(title);

                ColoredConsole.Write(" [");

                for (var i = 0; i < width; i++)
                {
                    ColoredConsole.Write('-');
                }

                ColoredConsole.Write("]");

                for (var i = 0; i < width + 1; i++)
                {
                    ColoredConsole.Write('\b');
                }
            }
            catch
            {
                isCompleted = true;
                ColoredConsole.WriteLine();
            }
        }

        public void Report(int progressPercentage)
        {
            if (isCompleted || Console.IsOutputRedirected)
            {
                return;
            }

            var tick = this.width * progressPercentage / 100;
            var diff = tick - completed;
            completed = tick;
            for (var i = 0; i < diff; i++)
            {
                ColoredConsole.Write('#');
            }

            if (tick == width)
            {
                ColoredConsole.WriteLine(']');
                isCompleted = true;
            }
        }

        public void Dispose()
        {
            Report(100);
            isCompleted = true;
        }
    }

    class StorageProgressBar : SimpleProgressBar, IProgress<StorageProgress>
    {
        private readonly long size;

        public StorageProgressBar(string title, long size) : base(title)
        {
            this.size = size;
        }

        public void Report(StorageProgress value)
        {
            Report((int)(value.BytesTransferred * 100 / size));
        }
    }
}
