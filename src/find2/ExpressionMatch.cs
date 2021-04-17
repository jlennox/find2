using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;

#pragma warning disable CA2208 // Instantiate argument exceptions correctly

namespace find2
{
    internal static class ExpressionMatch
    {
        private static readonly PropertyInfo _namefield = typeof(WindowsFileEntry).GetProperty("Name", BindingFlags.Instance | BindingFlags.Public);
        private static readonly ParameterExpression _parameter = Expression.Parameter(typeof(WindowsFileEntry));

        public static Func<WindowsFileEntry, bool> Build(params string[] arguments)
        {
            if (arguments == null || arguments.Length == 0)
            {
                return _ => true;
            }

            if (arguments[0] == "(" && arguments[^1] == ")")
            {
                arguments = arguments.Skip(1).Take(arguments.Length - 2).ToArray();
            }

            Func<Expression, Expression> wrappingExpression = null;
            Func<Expression, Expression, BinaryExpression> binaryExpression = null;
            Expression expression = null;
            var expressionTrees = new Stack<Expression>();

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

            for (var i = 0; i < arguments.Length; ++i)
            {
                var arg = arguments[i];

                string GetArgument()
                {
                    if (i == arguments.Length - 1)
                    {
                        throw new ArgumentNullException(nameof(arg), $"Argument \"{arg}\" requires a value.");
                    }

                    return arguments[++i];
                }

                switch (arg)
                {
                    case "(":
                        expressionTrees.Push(expression);
                        expression = null;
                        break;
                    case ")":
                        if (expressionTrees.Count == 0)
                        {
                            throw new Exception("Unexpected \")\". Missing \"(\"");
                        }

                        var currentExpression = expression;
                        expression = expressionTrees.Pop();

                        if (currentExpression != null)
                        {
                            AddExpression(currentExpression);
                        }
                        break;
                    case "-name":
                        AddExpression(NameBlob(GetArgument(), false));
                        break;
                    case "-iname":
                        AddExpression(NameBlob(GetArgument(), true));
                        break;
                    case "-regex":
                        AddExpression(NameRegex(GetArgument(), false));
                        break;
                    case "-iregex":
                        AddExpression(NameRegex(GetArgument(), true));
                        break;
                    case "-true":
                        AddExpression(Expression.Constant(true));
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
                throw new Exception("Missing \")\"");
            }

            expression ??= Expression.Constant(true);

            return Expression.Lambda<Func<WindowsFileEntry, bool>>(expression, _parameter).Compile(false);
        }

        private static Expression NameBlob(string match, bool caseInsensitive)
        {
            if (match.Length == 0 || match == "*")
            {
                return Expression.IsTrue(Expression.Constant(true));
            }

            var startBlob = match[0] == '*';
            var endBlob = match[^1] == '*';
            var globPos = match.IndexOf('*', 1);
            var hasCenterBlobs = globPos != -1 && globPos != match.Length - 1;

            if (hasCenterBlobs)
            {
                var blobExp = "^" + Regex.Escape(match).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
                return NameRegex(blobExp, caseInsensitive);
            }

            if (startBlob && endBlob)
            {
                return NameContains(match.Substring(1, match.Length - 2), caseInsensitive);
            }

            if (startBlob)
            {
                return NameEndsWith(match.Substring(1, match.Length - 1), caseInsensitive);
            }

            if (endBlob)
            {
                return NameStartsWith(match.Substring(0, match.Length - 1), caseInsensitive);
            }

            return NameEquals(match, caseInsensitive);

        }

        private static MethodCallExpression NameStartsWith(string match, bool caseInsensitive)
        {
            return StringComparisonMethod("StartsWith", match, caseInsensitive);
        }

        private static MethodCallExpression NameEndsWith(string match, bool caseInsensitive)
        {
            return StringComparisonMethod("EndsWith", match, caseInsensitive);
        }

        private static MethodCallExpression NameContains(string match, bool caseInsensitive)
        {
            return StringComparisonMethod("Contains", match, caseInsensitive);
        }

        private static MethodCallExpression NameEquals(string match, bool caseInsensitive)
        {
            return StringComparisonMethod("Equals", match, caseInsensitive);
        }

        private static MethodCallExpression NameRegex(string match, bool caseInsensitive)
        {
            var regexOptions = RegexOptions.Compiled | (caseInsensitive ? RegexOptions.IgnoreCase : RegexOptions.None);
            var exp = new Regex(match, regexOptions);

            var isMatchMethod = typeof(Regex).GetMethod(
                "IsMatch",
                new[] { typeof(string) }, null);

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