using System;
using System.Collections.Generic;
using System.Text;
using find2.IO;
using static System.Runtime.InteropServices.JavaScript.JSType;

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
        // TODO: This is an undefined subset of what actual find implements.
        // They should be evaluated for what's actually useful (what can you find in the wild?).
        switch (_directive)
        {
            case 'a':
                // File's last access time in the format returned by
                // the C ctime(3) function.
                target.AppendAsciiDateTime(entry.LastAccessTime.ToLocalTime());
                break;
            case 'b':
                // The amount of disk space used for this file in
                // 512-byte blocks.  Since disk space is allocated in
                // multiples of the filesystem block size this is
                // usually greater than %s/512, but it can also be
                // smaller if the file is a sparse file.
            case 'c':
                // File's last status change time in the format
                // returned by the C ctime(3) function.
            case 'd':
                // File's depth in the directory tree; 0 means the
                // file is a starting-point.
            case 'f':
                // Print the basename; the file's name with any
                // leading directories removed (only the last
                // element).  For /, the result is `/'.  See the
                // EXAMPLES section for an example.
            case 'h':
                // Dirname; the Leading directories of the file's nam
                // (all but the last element).  If the file name
                // contains no slashes (since it is in the current
                // directory) the %h specifier expands to `.'.  For
                // files which are themselves directories and contain
                // a slash (including /), %h expands to the empty
                // string.  See the EXAMPLES section for an example.
            case 'H':
                // Starting-point under which file was found.
            case 'k':
                // The amount of disk space used for this file in 1 KB
                // blocks.  Since disk space is allocated in multiples
                // of the filesystem block size this is usually
                // greater than %s/1024, but it can also be smaller if
                // the file is a sparse file.
            case 'm':
                // File's permission bits (in octal).  This option
                // uses the `traditional' numbers which most Unix
                // implementations use, but if your particular
                // implementation uses an unusual ordering of octal
                // permissions bits, you will see a difference between
                // the actual value of the file's mode and the output
                // of %m.  Normally you will want to have a leading
                // zero on this number, and to do this, you should use
                // the # flag (as in, for example, `%#m').
            case 'M':
                // File's permissions (in symbolic form, as for ls).
                // This directive is supported in findutils 4.2.5 and
                // later.
                throw new ArgumentOutOfRangeException();
            case 'p':
                // File's name.
                target.Append(entry.FullPath);
                break;
            case 'P':
                // File's name with the name of the starting-point
                // under which it was found removed.
                target.Append(entry.Name);
                break;
            case 's':
                // File's size in bytes.
                target.Append(entry.Size);
                break;
            case 't':
                // File's last modification time in the format
                // returned by the C ctime(3) function.
                target.AppendAsciiDateTime(entry.LastWriteTime.ToLocalTime());
                break;
            case 'u':
                // File's user name, or numeric user ID if the user
                // has no name.
                var username = entry.OwnerUsername;
                if (string.IsNullOrEmpty(username)) goto case 'U';
                target.Append(username);
                break;
            case 'U':
                // File's numeric user ID.
                target.Append(entry.OwnerUserID);
                break;
            case 'y':
                // File's type (like in ls -l), U=unknown type
                // (shouldn't happen)
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
        StringBuilder? stringBuilder = null;

        for (var i = 0; i < format.Length; ++i)
        {
            var currentCharacter = format[i];
            if (currentCharacter == '\\')
            {
                if (format.Length - i < 2)
                {
                    throw new ArgumentException("Final character can not be a backslash, perhaps try \\", nameof(format));
                }

                var octalValue = 0;
                var hasOctal = false;
                for (var octalIndex = 0; octalIndex < 3 && i < format.Length - 1; ++octalIndex)
                {
                    var nextCharacter = format[i + 1];
                    if (nextCharacter < '0' || nextCharacter > '7') break;

                    octalValue = 8 * octalValue + nextCharacter - '0';
                    hasOctal = true;
                    ++i;
                }

                if (hasOctal)
                {
                    // \NNN The character whose ASCII code is NNN(octal).
                    // See `parse_octal_escape` in official source.
                    if (char.MaxValue < octalValue)
                    {
                        throw new ArgumentOutOfRangeException(nameof(format), octalValue, $"Unexpected escaped character value. Value is greater than {char.MaxValue}.");
                    }
                    commands.Add(new PrintFPrinterCommandLiteral(((char)octalValue).ToString()));
                    // throw new Exception("TODO: Write support for this.");
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
                    case '%':
                        commands.Add(new PrintFPrinterCommandLiteral("%"));
                        break;
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

            stringBuilder ??= new StringBuilder();
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
