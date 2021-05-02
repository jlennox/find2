using System.Collections.Generic;
using System.IO;

namespace find2
{
    internal sealed class DotNetFileSearch : FileSearch
    {
        public static bool IsSupported() => true;

        public override void Initialize()
        {
        }

        public override IEnumerator<IFileEntry> GetContents(string directory)
        {
            var fileEntry = new DotnetFileEntry();
            foreach (var entry in Directory.GetFiles(directory))
            {
                fileEntry.Set(Path.Combine(directory, entry), false);
                yield return fileEntry;
            }

            foreach (var entry in Directory.GetDirectories(directory))
            {
                fileEntry.Set(Path.Combine(directory, entry), true);
                yield return fileEntry;
            }
        }

        public override void Dispose()
        {
        }
    }
}