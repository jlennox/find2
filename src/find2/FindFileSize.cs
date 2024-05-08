using System;

namespace find2;

internal enum FileSizeComparisonType
{
    Equals, Less, Greater
}

internal readonly struct FindFileSize
{
    public long Size { get; }
    public long Unit { get; }
    public FileSizeComparisonType Type { get; }

    public FindFileSize(ReadOnlySpan<char> input)
    {
        // TODO: Sync all exception messages with actual find?
        if (input.IsWhiteSpace())
        {
            throw new ArgumentNullException(nameof(input), "File size missing.");
        }

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

        var unitChar = input[^1];
        if (char.IsLetter(unitChar))
        {
            unit = GetUnitMeasurement(unitChar, input);
            input = input[..^1];
        }

        if (!long.TryParse(input, out var value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(input), input.ToString(),
                $"Expected numerical file size, got '{input}'");
        }

        Size = value * unit;
        Unit = unit;
    }

    private static int GetUnitMeasurement(char unitChar, ReadOnlySpan<char> input) => unitChar switch
    {
        'b' => 512,
        'c' => 1,
        'w' => 2,
        'k' => 1024,
        'M' => 1024 * 1024,
        'G' => 1024 * 1024 * 1024,
        _ => throw new ArgumentOutOfRangeException(
            nameof(input), input.ToString(),
            $"Expected valid unit of measurement, got '{unitChar}'"),
    };
}