using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using BinaryCheckExpression = System.Func<System.Linq.Expressions.Expression, System.Linq.Expressions.Expression, System.Linq.Expressions.BinaryExpression>;
using MatchExpression = System.Func<find2.IFileEntry, bool>;

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

    internal sealed class FindArguments
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
        public string ExpressionDescription;
        public int? MinDepth;
        public int? MaxDepth;

        public FileSearch GetSearch()
        {
            switch (DirectoryEngine)
            {
                case DirectoryEngine.Default:
                    return WindowsFileSearch.IsSupported()
                        ? new WindowsFileSearch()
                        : new DotNetFileSearch();
                case DirectoryEngine.Dotnet:
                    return new DotNetFileSearch();
                case DirectoryEngine.Windows:
                    return new WindowsFileSearch();
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    internal static class ExpressionMatch
    {
        // TODO: If it removes a call.virt, make these IFileEntry dynamically reference the used implementation type.
        private static readonly ParameterExpression _parameter = Expression.Parameter(typeof(IFileEntry), "file");
        private static readonly MemberExpression _nameField = GetPropertyAccess<IFileEntry>(nameof(IFileEntry.Name));
        private static readonly MemberExpression _isDirectoryField = GetPropertyAccess<IFileEntry>(nameof(IFileEntry.IsDirectory));

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
            "(", ")", "-name", "-iname", "-regex", "-iregex", "-true", "-false", "-not", "!", "-or", "-o", "-and", "-a",
            "-type", "-size", "-maxdepth", "-mindepth", "-mmin", "-newer", "-amin", "-anewer"
        };

        private static PropertyInfo GetProperty<T>(string name)
        {
            return typeof(T).GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
                ?? throw new ArgumentException($"Unable to locate \"{name}\" property on {nameof(T)}.");
        }

        private static MemberExpression GetPropertyAccess<T>(string name)
        {
            return Expression.MakeMemberAccess(_parameter, GetProperty<T>(name));
        }

        public static FindArguments Build(params string[]? args)
        {
            var findArguments = new FindArguments();
            string? arg;

            if (args == null || args.Length == 0)
            {
                return findArguments;
            }

            var arguments = new Arguments(args);
            var completed = false;

            while (!completed && (arg = arguments.Get()) != null)
            {
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
                        var debugOptions = arguments.GetValue().Split(',');
                        foreach (var debugOption in debugOptions)
                        {
                            if (!_debugOptionLookup.TryGetValue(debugOption, out var option))
                            {
                                throw new Exception($"Unsupported {arg} flag, {debugOption}");
                            }

                            findArguments.DebugOptions |= option;
                        }
                        break;
                    case "--engine":
                        var engineOption = arguments.GetValue();
                        if (!_engineLookup.TryGetValue(engineOption, out var engine))
                        {
                            throw new Exception($"Unsupported {arg} flag, {engineOption}");
                        }

                        findArguments.DirectoryEngine = engine;
                        break;
                    case "--threads":
                        findArguments.ThreadCount = arguments.GetPositiveNonZeroIntValue();
                        break;
                    case "--timeout":
                        findArguments.Timeout = TimeSpan.FromMilliseconds(arguments.GetPositiveNonZeroIntValue());
                        break;
                    case "-0level": findArguments.OptimizationLevel = 0; break;
                    case "-1level": findArguments.OptimizationLevel = 1; break;
                    case "-2level": findArguments.OptimizationLevel = 2; break;
                    case "-3level": findArguments.OptimizationLevel = 3; break;
                    default:
                        arguments.Index--;
                        completed = true;
                        break;
                }
            }

            // How we know if it's a path for argument is kind of ambiguous...
            if (args.Length > arguments.Index && !_matchOptions.Contains(args[arguments.Index]))
            {
                findArguments.Root = args[arguments.Index];
                ++arguments.Index;
            }

            if (args.Length <= arguments.Index)
            {
                return findArguments;
            }

            // Simplify to avoid needing to handle this exceptional case.
            if (args[arguments.Index] == "(" && args[^1] == ")")
            {
                arguments = new Arguments(args.Skip(1).Take(args.Length - 2).ToArray());
            }

            Func<Expression, Expression>? wrappingExpression = null;
            BinaryCheckExpression? binaryExpression = null;
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

            while ((arg = arguments.Get()) != null)
            {
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
                        AddExpression(NameBlob(arguments.GetValue(), false, out _));
                        break;
                    case "-iname":
                        AddExpression(NameBlob(arguments.GetValue(), true, out _));
                        break;
                    case "-regex":
                        AddExpression(NameRegex(arguments.GetValue(), false));
                        break;
                    case "-iregex":
                        AddExpression(NameRegex(arguments.GetValue(), true));
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
                    case "-type":
                        var typeArgs = arguments.GetValue();
                        foreach (var typeArg in typeArgs.Split(','))
                        {
                            switch (typeArg)
                            {
                                case "d":
                                    AddExpression(IsDirectory(true));
                                    break;
                                case "f":
                                    AddExpression(IsDirectory(false));
                                    break;
                                case "b": // block (buffered) special
                                case "c": // character (unbuffered) special
                                case "p": // named pipe (FIFO)
                                case "l": // symbolic link; this is never true if the -L option or the -follow option is in effect
                                case "s": // socket
                                case "D": // door (Solaris)
                                    throw new ArgumentOutOfRangeException(arg, typeArg, "A valid argument type but not (yet?) supported.");
                                default:
                                    throw new ArgumentOutOfRangeException(arg, typeArg, "Invalid type argument.");
                            }
                        }

                        break;
                    case "-size":
                        AddExpression(MatchSize(new FindFileSize(arguments.GetValue())));
                        break;
                    case "-maxdepth":
                        // TODO: Technically -maxdepth and -mindepth need to come before all other parameters, and when
                        // inside the usual flow, should exception.
                        findArguments.MaxDepth = arguments.GetPositiveIntValue();
                        break;
                    case "-mindepth":
                        findArguments.MinDepth = arguments.GetPositiveIntValue();
                        break;
                    case "-mmin":
                        AddExpression(LastWriteTime(
                            DateTime.UtcNow + TimeSpan.FromMinutes(arguments.GetIntValue()),
                            Expression.GreaterThanOrEqual));
                        break;
                    case "-newer":
                        AddExpression(LastWriteTime(
                            File.GetLastAccessTimeUtc(arguments.GetFileValue()),
                            Expression.GreaterThanOrEqual));
                        break;
                    case "-amin":
                        AddExpression(LastAccessTime(
                            DateTime.UtcNow + TimeSpan.FromMinutes(arguments.GetIntValue()),
                            Expression.GreaterThanOrEqual));
                        break;
                    case "-anewer":
                        AddExpression(LastAccessTime(
                            File.GetLastAccessTimeUtc(arguments.GetFileValue()),
                            Expression.GreaterThanOrEqual));
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
            findArguments.ExpressionDescription = expression == null ? "*" : expression.ToString();

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
            return StringComparisonMethod(nameof(string.StartsWith), match, caseInsensitive);
        }

        internal static MethodCallExpression NameEndsWith(string match, bool caseInsensitive)
        {
            return StringComparisonMethod(nameof(string.EndsWith), match, caseInsensitive);
        }

        internal static MethodCallExpression NameContains(string match, bool caseInsensitive)
        {
            return StringComparisonMethod(nameof(string.Contains), match, caseInsensitive);
        }

        internal static MethodCallExpression NameEquals(string match, bool caseInsensitive)
        {
            return StringComparisonMethod(nameof(string.Equals), match, caseInsensitive);
        }

        internal static Expression IsDirectory(bool shouldBe)
        {
            return shouldBe ? Expression.IsTrue(_isDirectoryField) : Expression.IsFalse(_isDirectoryField);
        }

        internal static Expression MatchSize(FindFileSize size)
        {
            // TODO: The `IsDirectory` checks here are incorrect. Need to figure out why GNU `find` sometimes returns
            // directory results.
            var sizeField = GetPropertyAccess<IFileEntry>(nameof(IFileEntry.Size));
            var sizeConstant = Expression.Constant(size.Size);

            BinaryCheckExpression equalityCheck = size.Type switch {
                FileSizeComparisonType.Less => Expression.LessThan,
                FileSizeComparisonType.Greater => Expression.GreaterThan,
                FileSizeComparisonType.Equals => Expression.Equal,
                _ => throw new ArgumentOutOfRangeException()
            };

            // Fast path byte sized checks because they do not need the rounding math to happen.
            if (size.Unit == 1)
            {
                return Expression.And(equalityCheck(sizeField, sizeConstant), IsDirectory(false));
            }

            // GNU `find` "rounds up." that is, if they're searching for "1 megabyte" and the file is "1 byte," it's
            // rounded up to be 1 megabytes.
            var rounded = Expression.Condition(
                Expression.Equal(sizeField, Expression.Constant(0L)),
                Expression.Constant(0L),
                Expression.Add(
                    Expression.Modulo(Expression.Constant(size.Unit), sizeField),
                    sizeField));

            return Expression.And(equalityCheck(rounded, sizeConstant), IsDirectory(false));
        }

        internal static Expression LastWriteTime(DateTime datetime, BinaryCheckExpression check)
        {
            var field = GetPropertyAccess<IFileEntry>(nameof(IFileEntry.LastWriteTime));
            return check(field, Expression.Constant(datetime));
        }

        internal static Expression LastAccessTime(DateTime datetime, BinaryCheckExpression check)
        {
            var field = GetPropertyAccess<IFileEntry>(nameof(IFileEntry.LastAccessTime));
            return check(field, Expression.Constant(datetime));
        }

        internal static MethodCallExpression NameRegex(string match, bool caseInsensitive)
        {
            var regexOptions = RegexOptions.Compiled | (caseInsensitive ? RegexOptions.IgnoreCase : RegexOptions.None);
            var exp = new Regex(match, regexOptions);

            var isMatchMethod = typeof(Regex).GetMethod(
                nameof(Regex.IsMatch),
                new[] { typeof(string) }, null);

            if (isMatchMethod == null)
            {
                throw new ArgumentOutOfRangeException(nameof(isMatchMethod), "IsMatch", "Unable to locate 'IsMatch' method.");
            }

            return Expression.Call(Expression.Constant(exp), isMatchMethod, _nameField);
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
                _nameField, methodInfo,
                Expression.Constant(match), Expression.Constant(comparison));
        }
    }
}