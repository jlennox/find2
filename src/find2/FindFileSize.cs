using System;
using System.Collections.Generic;

namespace find2
{
    internal enum FileSizeComparisonType
    {
        Equals, Less, Greater
    }

    internal readonly struct FindFileSize
    {
        private static readonly IReadOnlyDictionary<char, long> _fileSizeUnitLookup = new Dictionary<char, long>
        {
            { 'b', 512 },
            { 'c', 1 },
            { 'w', 2 },
            { 'k', 1024 },
            { 'M', 1024 * 1024 },
            { 'G', 1024 * 1024 * 1024 },
        };

        public long Size { get; }
        public long Unit { get; }
        public FileSizeComparisonType Type { get; }

        public FindFileSize(string input)
        {
            // TODO
            if (string.IsNullOrWhiteSpace(input)) throw new Exception("");

            var unit = 512L;
            Type = FileSizeComparisonType.Equals;

            switch (input[0])
            {
                case '+':
                    Type = FileSizeComparisonType.Greater;
                    input = input[1..];
                    break;
                case '-':
                    Type = FileSizeComparisonType.Less;
                    input = input[1..];
                    break;
            }

            if (char.IsLetter(input[^1]))
            {
                if (!_fileSizeUnitLookup.TryGetValue(input[^1], out unit))
                {
                    // TODO
                    throw new Exception("");
                }

                input = input[..^1];
            }

            if (!long.TryParse(input, out var value))
            {
                // TODO
                throw new Exception("");
            }

            Size = value * unit;
            Unit = unit;
        }
    }
}