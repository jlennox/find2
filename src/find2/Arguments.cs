using System;
using System.IO;

namespace find2
{
    internal sealed class Arguments
    {
        public int Index { get; set; }

        private readonly string[]? _arguments;

        private string? _arg;

        public Arguments(string[]? arguments)
        {
            _arguments = arguments;
        }

        public string? Get()
        {
            _arg = Index >= _arguments!.Length ? null : _arguments[Index++];
            return _arg;
        }

        public string GetValue()
        {
            if (Index >= _arguments!.Length)
            {
                throw new ArgumentNullException(_arg, $"Argument \"{_arg}\" requires a value.");
            }

            return _arguments[Index++];
        }

        public string GetFileValue()
        {
            var file = GetValue();
            // TODO: Exception
            if (!File.Exists(file)) throw new FileNotFoundException("");
            // TODO: If the file is a symbolic link and -H/-L are specified, follow the link.
            return file;
        }

        public int GetIntValue()
        {
            var value = GetValue();
            if (int.TryParse(value, out var intValue)) return intValue;
            throw new ArgumentOutOfRangeException(_arg, value, "Expected integral value.");
        }

        public int GetPositiveIntValue()
        {
            var value = GetIntValue();
            if (value >= 0) return value;
            throw new ArgumentOutOfRangeException(_arg, value, "Expected positive integral value.");
        }

        public int GetPositiveNonZeroIntValue()
        {
            var value = GetPositiveIntValue();
            if (value > 0) return value;
            throw new ArgumentOutOfRangeException(_arg, value, "Expected positive, non-zero, integral value.");
        }
    }
}