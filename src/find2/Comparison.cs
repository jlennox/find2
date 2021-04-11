using System;
using System.Text.RegularExpressions;

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

    internal enum NumericCompartisonType
    {
        Exact,
        LessThan,
        GreaterThan
    }

    internal abstract class FileComparison<T>
    {
        public T Value { get; }
        public abstract bool Check(T input);

        protected FileComparison(T value)
        {
            Value = value;
        }
    }

    internal class StringComparison : FileComparison<string>
    {
        public StringComparisonType StringComparisonType { get; set; }

        private readonly Regex _expression;
        private readonly System.StringComparison _stringComparison;

        public StringComparison(
            string value,
            StringComparisonType stringComparisonType,
            bool caseInsensitive)
            : base(value)
        {
            StringComparisonType = stringComparisonType;

            var regexOptions = RegexOptions.Compiled | (caseInsensitive ? RegexOptions.IgnoreCase : RegexOptions.None);

            switch (StringComparisonType)
            {
                case StringComparisonType.Glob:
                    _expression = new Regex(
                        "^" + Regex.Escape(value).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
                        regexOptions);
                    break;
                case StringComparisonType.Regex:
                    _expression = new Regex(value, regexOptions);
                    break;
            }

            _stringComparison = caseInsensitive
                ? System.StringComparison.CurrentCultureIgnoreCase
                : System.StringComparison.CurrentCulture;
        }

        public override bool Check(string input)
        {
            switch (StringComparisonType)
            {
                case StringComparisonType.Exact:
                    return input.Equals(Value, _stringComparison);
                case StringComparisonType.EndsWith:
                    return input.EndsWith(Value, _stringComparison);
                case StringComparisonType.StartsWith:
                    return input.StartsWith(Value, _stringComparison);
                case StringComparisonType.Contains:
                    return input.Contains(Value, _stringComparison);
                case StringComparisonType.Glob:
                case StringComparisonType.Regex:
                    return _expression.IsMatch(input);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(StringComparisonType), StringComparisonType,
                        "Unknown StringComparisonType");
            }
        }
    }

    // internal class LogicalOrComparison<T1, T2> : FileComparison<T>
}