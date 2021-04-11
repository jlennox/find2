using System.Collections.Generic;
using System.IO;

namespace find2
{
    internal class DotNetFileSearch : FileSearch<WindowsFileEntry>
    {
        public static bool IsSupported() => true;

        public override void Initialize()
        {
        }

        public override IEnumerator<IFileEntry> GetContents(string directory)
        {
            foreach (var entry in Directory.GetFiles(directory))
            {
                yield return new WindowsFileEntry(false, entry);
            }

            foreach (var entry in Directory.GetDirectories(directory))
            {
                yield return new WindowsFileEntry(true, entry);
            }
        }

        public override void Dispose()
        {
        }
    }
}