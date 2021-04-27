using System.Collections.Generic;
using System.IO;

namespace find2
{
    internal sealed class DotNetFileSearch : FileSearch<WindowsFileEntry>
    {
        public static bool IsSupported() => true;

        public override void Initialize()
        {
        }

        public override IEnumerator<WindowsFileEntry> GetContents(string directory)
        {
            foreach (var entry in Directory.GetFiles(directory))
            {
                yield return new WindowsFileEntry(Path.Combine(directory, entry));
            }

            foreach (var entry in Directory.GetDirectories(directory))
            {
                yield return new WindowsFileEntry(Path.Combine(directory, entry));
            }
        }

        public override void Dispose()
        {
        }
    }
}