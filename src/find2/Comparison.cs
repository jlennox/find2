using System;

namespace find2
{
    internal enum StringComparisonType
    {
        Exact,
        EndsWith,
        StartsWith,
        Contains,
        Glob,
        Regex
    }

    internal abstract class FileComparison<T>
    {
        public T Value { get; }
        public abstract bool Check(T input);
    }

    internal class StringComparison : FileComparison<string>
    {
        public StringComparisonType StringComparisonType { get; set; }

        public override bool Check(string input)
        {
            throw new NotImplementedException();
        }
    }

    // internal class LogicalOrComparison<T1, T2> : FileComparison<T>
}