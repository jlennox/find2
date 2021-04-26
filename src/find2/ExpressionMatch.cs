﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using MatchExpression = System.Func<find2.WindowsFileEntry, bool>;

#pragma warning disable CA2208 // Instantiate argument exceptions correctly

namespace find2
{
    internal enum FollowSymbolicLinkBehavior
    {
        Never, Always, WhenArgument
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

    internal enum DirectoryEngine
    {
        Default, Windows, Dotnet
    }

    internal class FindArguments
    {
        public DebugOptions DebugOptions = DebugOptions.None;
        public int OptimizationLevel;
        public int? ThreadCount;
        public TimeSpan? Timeout;
        public string Root = ".";
        public FollowSymbolicLinkBehavior FollowSymbolicLinkBehavior = FollowSymbolicLinkBehavior.Never;
        public DirectoryEngine DirectoryEngine;

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

        private static readonly IReadOnlyDictionary<string, DirectoryEngine> _engineLookup = new Dictionary<string, DirectoryEngine>
        {
            { "default", DirectoryEngine.Default },
            { "windows", DirectoryEngine.Windows },
            { "dotnet", DirectoryEngine.Dotnet },
        };

        private static readonly IReadOnlySet<string> _matchOptions = new HashSet<string> {
            "(", ")", "-name", "-iname", "-regex", "-iregex", "-true", "-false", "-not", "!", "-or", "-o", "-and", "-a"
        };

        public static FindArguments Build(params string[]? arguments)
        {
            var findArguments = new FindArguments();
            var i = 0;

            string GetArgument(string arg)
            {
                if (i == arguments!.Length - 1)
                {
                    throw new ArgumentNullException(arg, $"Argument \"{arg}\" requires a value.");
                }

                return arguments[++i];
            }

            int GetArgumentInt(string arg)
            {
                var value = GetArgument(arg);

                if (!int.TryParse(value, out var intValue))
                {
                    throw new ArgumentOutOfRangeException(arg, value, "Expected integral value.");
                }

                return intValue;
            }

            int GetPositiveInt(string arg)
            {
                var value = GetArgumentInt(arg);
                if (value <= 0)
                {
                    throw new ArgumentOutOfRangeException(arg, value, "Expected positive, non-zero, integral value.");
                }

                return value;
            }

            if (arguments == null || arguments.Length == 0)
            {
                return findArguments;
            }

            for (var completed = false; i < arguments.Length && !completed; ++i)
            {
                var arg = arguments[i];
                if (string.IsNullOrWhiteSpace(arg)) continue;

                switch (arg)
                {
                    case "-P":
                        findArguments.FollowSymbolicLinkBehavior = FollowSymbolicLinkBehavior.Never;
                        break;
                    case "-L":
                        findArguments.FollowSymbolicLinkBehavior = FollowSymbolicLinkBehavior.Always;
                        break;
                    case "-H":
                        findArguments.FollowSymbolicLinkBehavior = FollowSymbolicLinkBehavior.WhenArgument;
                        break;
                    case "-D":
                        var debugOptions = GetArgument(arg).Split(',');
                        foreach (var debugOption in debugOptions)
                        {
                            if (_debugOptionLookup.TryGetValue(debugOption, out var option))
                            {
                                throw new Exception($"Unsupported {arg} flag, {debugOption}");
                            }

                            findArguments.DebugOptions |= option;
                        }
                        break;
                    case "--engine":
                        var engineOption = GetArgument(arg);
                        if (_engineLookup.TryGetValue(engineOption, out var engine))
                        {
                            throw new Exception($"Unsupported {arg} flag, {engineOption}");
                        }

                        findArguments.DirectoryEngine = engine;
                        break;
                    case "--threads":
                        findArguments.ThreadCount = GetPositiveInt(arg);
                        break;
                    case "--timeout":
                        findArguments.Timeout = TimeSpan.FromMilliseconds(GetPositiveInt(arg));
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

            if (arguments.Length > i && !_matchOptions.Contains(arguments[i]))
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
                if (string.IsNullOrWhiteSpace(arg)) continue;

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

            switch (startBlob, endBlob)
            {
                case (true, true): // "*foobar*"
                    matchType = nameof(NameContains);
                    return NameContains(match[1..^1], caseInsensitive);
                case (true, false): // "*foo"
                    matchType = nameof(NameEndsWith);
                    return NameEndsWith(match[1..], caseInsensitive);
                case (false, true): // "foo*"
                    matchType = nameof(NameStartsWith);
                    return NameStartsWith(match[..^1], caseInsensitive);
                case (false, false): // "foo"
                    matchType = nameof(NameEquals);
                    return NameEquals(match, caseInsensitive);
            }
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