using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using MatchExpression = System.Func<find2.WindowsFileEntry, bool>;

#nullable enable
#pragma warning disable CA2208 // Instantiate argument exceptions correctly

namespace find2
{
    internal enum FollowSymbolicLinks
    {
        Never = 0, Always, WhenArgument
    }

    [Flags]
    internal enum DebugOptions
    {
        None,
        Exec = 1 << 0,
        Help = 1 << 1,
        Optimization = 1 << 2,
        Rates = 1 << 3,
        Search = 1 << 4,
        Stat = 1 << 5,
        Tree = 1 << 6,
    }

    internal class FindArguments
    {
        public DebugOptions DebugOptions = DebugOptions.None;
        public int OptimizationLevel;
        public string Root = ".";
        public FollowSymbolicLinks FollowSymbolicLinks = FollowSymbolicLinks.Never;

        // If null, all results should match.
        public MatchExpression? Match;
        public int? MaxDepth;
    }

    internal static class ExpressionMatch
    {
        private static readonly ParameterExpression _parameter = Expression.Parameter(typeof(WindowsFileEntry));
        private static readonly PropertyInfo _namefield = typeof(WindowsFileEntry)
            .GetProperty("Name", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new ArgumentException($"Unable to locate \"Name\" property on {nameof(WindowsFileEntry)}.");

        private static readonly IReadOnlyDictionary<string, DebugOptions> _debugOptionLookup = new Dictionary<string, DebugOptions>
        {
            { "exec", DebugOptions.Exec },
            { "help", DebugOptions.Help },
            { "opt", DebugOptions.Optimization },
            { "rates", DebugOptions.Rates },
            { "search", DebugOptions.Search },
            { "stat", DebugOptions.Stat },
            { "tree", DebugOptions.Tree },
        };

        public static FindArguments Build(params string[]? arguments)
        {
            var findArguments = new FindArguments();
            var i = 0;

            string GetArgument(string arg)
            {
                if (i == arguments.Length - 1)
                {
                    throw new ArgumentNullException(nameof(arg), $"Argument \"{arg}\" requires a value.");
                }

                return arguments[++i];
            }

            if (arguments == null || arguments.Length == 0)
            {
                return findArguments;
            }

            for (var completed = false; i < arguments.Length && !completed; ++i)
            {
                var arg = arguments[i];
                switch (arg)
                {
                    case "-P":
                        findArguments.FollowSymbolicLinks = FollowSymbolicLinks.Never;
                        break;
                    case "-L":
                        findArguments.FollowSymbolicLinks = FollowSymbolicLinks.Always;
                        break;
                    case "-H":
                        findArguments.FollowSymbolicLinks = FollowSymbolicLinks.WhenArgument;
                        break;
                    case "-D":
                        var debugOptions = GetArgument(arg).Split(',');
                        foreach (var debugOption in debugOptions)
                        {
                            if (_debugOptionLookup.TryGetValue(debugOption, out var option))
                            {
                                throw new Exception($"Unsupported -D flag, {debugOption}");
                            }

                            findArguments.DebugOptions |= option;
                        }
                        break;
                    case "-0level": findArguments.OptimizationLevel = 0; break;
                    case "-1level": findArguments.OptimizationLevel = 1; break;
                    case "-2level": findArguments.OptimizationLevel = 2; break;
                    case "-3level": findArguments.OptimizationLevel = 3; break;
                    default:
                        --i;
                        completed = true;
                        break;
                }
            }

            if (arguments.Length < i && !arguments[i].StartsWith('-'))
            {
                findArguments.Root = arguments[i];
                ++i;
            }

            if (arguments.Length <= i)
            {
                return findArguments;
            }

            // Simplify to avoid needing to handle this exceptional case.
            if (arguments[i] == "(" && arguments[^1] == ")")
            {
                arguments = arguments.Skip(1).Take(arguments.Length - 2).ToArray();
            }

            Func<Expression, Expression>? wrappingExpression = null;
            Func<Expression, Expression, BinaryExpression>? binaryExpression = null;
            Expression? expression = null;
            // TODO: Need to stack out wrappingExpression too.
            var expressionTrees = new Stack<Expression?>();

            void AddExpression(Expression newExpression)
            {
                if (wrappingExpression != null)
                {
                    newExpression = wrappingExpression(newExpression);
                    wrappingExpression = null;
                }

                if (expression == null)
                {
                    expression = newExpression;
                    return;
                }

                var binaryOp = binaryExpression ?? Expression.AndAlso;
                binaryExpression = null;

                expression = binaryOp(expression, newExpression);
            }

            for (; i < arguments.Length; ++i)
            {
                var arg = arguments[i];

                switch (arg)
                {
                    case "(":
                        expressionTrees.Push(expression);
                        expression = null;
                        break;
                    case ")":
                        if (expressionTrees.Count == 0)
                        {
                            throw new Exception("Unexpected \")\". Missing \"(\".");
                        }

                        var currentExpression = expression;
                        expression = expressionTrees.Pop();

                        if (currentExpression != null)
                        {
                            AddExpression(currentExpression);
                        }
                        break;
                    case "-name":
                        AddExpression(NameBlob(GetArgument(arg), false, out _));
                        break;
                    case "-iname":
                        AddExpression(NameBlob(GetArgument(arg), true, out _));
                        break;
                    case "-regex":
                        AddExpression(NameRegex(GetArgument(arg), false));
                        break;
                    case "-iregex":
                        AddExpression(NameRegex(GetArgument(arg), true));
                        break;
                    case "-true":
                        AddExpression(Expression.Constant(true));
                        break;
                    case "-false":
                        AddExpression(Expression.Constant(false));
                        break;
                    case "-not":
                    case "!":
                        wrappingExpression = Expression.IsFalse;
                        break;
                    case "-or":
                    case "-o":
                        binaryExpression = Expression.OrElse;
                        break;
                    case "-and":
                    case "-a":
                        binaryExpression = Expression.AndAlso;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(arg), arg, $"Unknown argument \"{arg}\".");
                }
            }

            if (expressionTrees.Count != 0)
            {
                throw new Exception("Missing \")\".");
            }

            expression ??= Expression.Constant(true);

            findArguments.Match = Expression.Lambda<MatchExpression>(expression, _parameter).Compile(false);

            return findArguments;
        }

        internal static Expression NameBlob(string match, bool caseInsensitive, out string? matchType)
        {
            if (match.Length == 0 || match == "*")
            {
                matchType = null;
                return Expression.IsTrue(Expression.Constant(true));
            }

            var startBlob = match[0] == '*';
            var endBlob = match[^1] == '*';
            var globPos = match.IndexOf('*', 1);
            var hasCenterBlobs = globPos != -1 && globPos != match.Length - 1;

            // "foo*bar"
            if (hasCenterBlobs)
            {
                var blobExp = "^" + Regex.Escape(match).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
                matchType = nameof(NameRegex);
                return NameRegex(blobExp, caseInsensitive);
            }

            // "*foobar*"
            if (startBlob && endBlob)
            {
                matchType = nameof(NameContains);
                return NameContains(match[1..^1], caseInsensitive);
            }

            // "*foo"
            if (startBlob)
            {
                matchType = nameof(NameEndsWith);
                return NameEndsWith(match[1..], caseInsensitive);
            }

            // "foo*"
            if (endBlob)
            {
                matchType = nameof(NameStartsWith);
                return NameStartsWith(match[..^1], caseInsensitive);
            }

            // "foo"
            matchType = nameof(NameEquals);
            return NameEquals(match, caseInsensitive);

        }

        internal static MethodCallExpression NameStartsWith(string match, bool caseInsensitive)
        {
            return StringComparisonMethod("StartsWith", match, caseInsensitive);
        }

        internal static MethodCallExpression NameEndsWith(string match, bool caseInsensitive)
        {
            return StringComparisonMethod("EndsWith", match, caseInsensitive);
        }

        internal static MethodCallExpression NameContains(string match, bool caseInsensitive)
        {
            return StringComparisonMethod("Contains", match, caseInsensitive);
        }

        internal static MethodCallExpression NameEquals(string match, bool caseInsensitive)
        {
            return StringComparisonMethod("Equals", match, caseInsensitive);
        }

        internal static MethodCallExpression NameRegex(string match, bool caseInsensitive)
        {
            var regexOptions = RegexOptions.Compiled | (caseInsensitive ? RegexOptions.IgnoreCase : RegexOptions.None);
            var exp = new Regex(match, regexOptions);

            var isMatchMethod = typeof(Regex).GetMethod(
                "IsMatch",
                new[] { typeof(string) }, null);

            if (isMatchMethod == null)
            {
                throw new ArgumentOutOfRangeException(nameof(isMatchMethod), "IsMatch", "Unable to locate 'IsMatch' method.");
            }

            return Expression.Call(
                Expression.Constant(exp),
                isMatchMethod,
                Expression.MakeMemberAccess(_parameter, _namefield));
        }

        private static MethodCallExpression StringComparisonMethod(string method, string match, bool caseInsensitive)
        {
            var methodInfo = typeof(string).GetMethod(
                method,
                new[] { typeof(string), typeof(StringComparison) }, null);

            if (methodInfo == null)
            {
                throw new ArgumentOutOfRangeException(nameof(method), method, "Unable to locate method.");
            }

            var comparison = caseInsensitive
                ? StringComparison.CurrentCultureIgnoreCase
                : StringComparison.CurrentCulture;

            return Expression.Call(
                Expression.MakeMemberAccess(_parameter, _namefield),
                methodInfo,
                Expression.Constant(match), Expression.Constant(comparison));
        }
    }
}