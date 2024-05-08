using System;
using System.Collections.Generic;
using System.Text;
using find2.IO;

namespace find2;

internal abstract class PrintFPrinterCommand
{
    public abstract void Format(IFileEntry entry, StringBuilder target);
}

internal sealed class PrintFPrinterCommandLiteral(string literal) : PrintFPrinterCommand
{
    private readonly string _literal = literal;

    public override void Format(IFileEntry _, StringBuilder target)
    {
        target.Append(_literal);
    }
}

internal sealed class PrintFPrinterCommandOutputFlush() : PrintFPrinterCommand
{
    public override void Format(IFileEntry _, StringBuilder __)
    {
        // TODO:
    }
}

internal sealed class PrintFPrinterCommandArgumentlessDirective(char directive) : PrintFPrinterCommand
{
    private readonly char _directive = directive;

    public override void Format(IFileEntry entry, StringBuilder target)
    {
        switch (_directive)
        {
            case 'a':
                target.AppendAsciiDateTime(entry.LastAccessTime.ToLocalTime());
                break;
            case 'b':
            case 'c':
            case 'd':
            case 'f':
            case 'h':
            case 'H':
            case 'k':
            case 'm':
            case 'M':
                throw new ArgumentOutOfRangeException();
            case 'p':
                target.Append(entry.FullPath);
                break;
            case 'P':
                target.Append(entry.Name);
                break;
            case 's':
                target.Append(entry.Size);
                break;
            case 't':
                target.AppendAsciiDateTime(entry.LastWriteTime.ToLocalTime());
                break;
            case 'u':
                target.Append(entry.OwnerUsername);
                break;
            case 'U':
            case 'y':
                throw new ArgumentOutOfRangeException();
        }
    }
}

internal sealed class PrintFPrinter(string format)
{
    private readonly IReadOnlyList<PrintFPrinterCommand> _commands = Parse(format);

    [ThreadStatic]
    private static StringBuilder? _stringBuilder;

    public string Format(IFileEntry entry)
    {
        var sb = _stringBuilder;
        sb ??= _stringBuilder = new StringBuilder();

        foreach (var command in _commands)
        {
            command.Format(entry, sb);
        }

        var result = sb.ToString();
        sb.Clear();
        return result;
    }

    // https://man7.org/linux/man-pages/man1/find.1.html#EXPRESSION
    private static IReadOnlyList<PrintFPrinterCommand> Parse(string format)
    {
        var commands = new List<PrintFPrinterCommand>();
        var stringBuilder = new StringBuilder();

        for (var i = 0; i < format.Length; ++i)
        {
            var currentCharacter = format[i];
            if (currentCharacter == '\\')
            {
                if (format.Length - i < 2)
                {
                    throw new ArgumentException("Final character can not be a backslash, perhaps try \\", nameof(format));
                }

                StringBuilder? numeric = null;
                for (; i < format.Length;)
                {
                    if (!char.IsNumber(format[i + 1])) break;

                    numeric ??= new StringBuilder();
                    numeric.Append(format[++i]);
                }

                if (numeric != null)
                {
                    // \NNN The character whose ASCII code is NNN(octal).
                    throw new Exception("TODO: Write support for this.");
                    continue;
                }

                var escapedCharacter = format[++i];
                switch (escapedCharacter)
                {
                    case 'a': commands.Add(new PrintFPrinterCommandLiteral("\a")); break;
                    case 'b': commands.Add(new PrintFPrinterCommandLiteral("\b")); break;
                    case 'c': commands.Add(new PrintFPrinterCommandOutputFlush()); break;
                    case 'f': commands.Add(new PrintFPrinterCommandLiteral("\f")); break;
                    case 'n': commands.Add(new PrintFPrinterCommandLiteral("\n")); break;
                    case 'r': commands.Add(new PrintFPrinterCommandLiteral("\r")); break;
                    case 't': commands.Add(new PrintFPrinterCommandLiteral("\t")); break;
                    case 'v': commands.Add(new PrintFPrinterCommandLiteral("\v")); break;
                    case '0': commands.Add(new PrintFPrinterCommandLiteral("\0")); break;
                    case '\\': commands.Add(new PrintFPrinterCommandLiteral("\\")); break;
                    default: commands.Add(new PrintFPrinterCommandLiteral(escapedCharacter.ToString())); break;
                }

                continue;
            }

            if (currentCharacter == '%')
            {
                if (format.Length - i < 2)
                {
                    throw new ArgumentException("Final character can not be %, perhaps try %%", nameof(format));
                }

                var directiveChar = format[++i];
                switch (directiveChar)
                {
                    case '%': commands.Add(new PrintFPrinterCommandLiteral("%")); break;
                    case 'a':
                    case 'b':
                    case 'c':
                    case 'd':
                    case 'f':
                    case 'h':
                    case 'H':
                    case 'k':
                    case 'm':
                    case 'M':
                    case 'p':
                    case 'P':
                    case 's':
                    case 't':
                    case 'u':
                    case 'U':
                    case 'y':
                        commands.Add(new PrintFPrinterCommandArgumentlessDirective(directiveChar));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(format), directiveChar, "Unknown directive character in printf format string.");
                }

                continue;
            }

            for (; i < format.Length;)
            {
                var c = format[i];
                if (c is '%' or '\\')
                {
                    --i; // bad hack to rewind us for the for(;;)'s ++i.
                    break;
                }

                stringBuilder.Append(c);
                ++i;
            }

            commands.Add(new PrintFPrinterCommandLiteral(stringBuilder.ToString()));
            stringBuilder.Clear();
        }

        return commands;
    }
}
