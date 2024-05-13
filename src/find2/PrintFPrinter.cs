using System;
using System.Collections.Generic;
using System.Globalization;
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

internal sealed class PrintFPrinterCommandDirective(char directive, char command) : PrintFPrinterCommand
{
    public override void Format(IFileEntry entry, StringBuilder target)
    {
        switch (directive)
        {
            case 'A':
                FormatAccessTime(entry.LastAccessTime.ToLocalTime(), command, target);
                break;
            case 'B':
                FormatAccessTime(entry.CreationTime.ToLocalTime(), command, target);
                break;
            case 'C':
                FormatAccessTime(entry.ChangeTime.ToLocalTime(), command, target);
                break;
            case 'T':
                FormatAccessTime(entry.LastWriteTime.ToLocalTime(), command, target);
                break;
            default:
                throw new ArgumentOutOfRangeException("TODO");
        }
    }

    private static void FormatAccessTime(DateTime dateTime, char directive, StringBuilder target)
    {
        // In general this code should avoid dateTime.ToString when it can, to prevent an intermediate
        // string allocation, but all in all, this code is not frequently used and does not need to
        // be optimized, so favor legible code over fast code.
        switch (directive)
        {
            // H      hour (00..23)
            // k      hour ( 0..23)
            case 'H':
            case 'k':
                target.AppendTwoDigitsLeftSpaced(dateTime.Hour, directive == 'H' ? '0' : ' ');
                break;
            // I      hour (01..12)
            // l      hour ( 1..12)
            case 'I':
            case 'l':
                var padding = directive == 'I' ? '0' : ' ';
                target.AppendTwoDigitsLeftSpaced(dateTime.Hour > 12 ? dateTime.Hour % 12 : dateTime.Hour, padding);
                break;
            // M      minute (00..59)
            case 'M': target.AppendTwoDigitsLeftSpaced(dateTime.Minute, '0'); break;
            // p      locale's AM or PM
            case 'p': target.Append(dateTime.ToString("tt")); break;
            // r      time, 12-hour (hh:mm:ss [AP]M)
            case 'r': target.Append(dateTime.ToString("hh:mm:ss tt")); break;
            // S      Second (00.00 .. 61.00).  There is a fractional part.
            case 'S': target.Append(dateTime.ToString("ss.fffffff000")); break;
            // T      time, 24-hour (hh:mm:ss.xxxxxxxxxx)
            case 'T': target.Append(dateTime.ToString("hh:mm:ss.fffffff000")); break;
            // +      Date and time, separated by `+', for example
            //        `2004-04-28+22:22:05.0'.  This is a GNU
            //        extension.  The time is given in the current
            //        timezone (which may be affected by setting
            //        the TZ environment variable).  The seconds
            //        field includes a fractional part.
            case '+': target.Append(dateTime.ToString("yyyy-MM-dd+hh:mm:ss.fffffff000")); break;
            // X      locale's time representation (H:M:S).  The
            //        seconds field includes a fractional part.
            // TODO: This is wrong.
            case 'X': goto case 'T';
            // Z      time zone (e.g., EDT), or nothing if no time
            //        zone is determinable
            case 'Z': target.Append(dateTime.Kind == DateTimeKind.Local ? TimeZoneInfo.Local : dateTime.Kind); break;
            // a      locale's abbreviated weekday name (Sun..Sat)
            case 'a': target.Append(dateTime.ToString("ddd")); break;
            // A      locale's full weekday name, variable length
            //        (Sunday..Saturday)
            case 'A': target.Append(dateTime.ToString("dddd")); break;
            // b      locale's abbreviated month name (Jan..Dec)
            case 'b': target.Append(dateTime.ToString("MMM")); break;
            // B locale's full month name, variable length (January..December)
            case 'B': target.Append(dateTime.ToString("MMMM")); break;
            case 'c': target.AppendAsciiDateTimeNoFractions(dateTime); break;
            // d      day of month (01..31)
            case 'd': target.AppendTwoDigitsLeftSpaced(dateTime.Day, '0'); break;
            // D      date (mm/dd/yy)
            case 'D': target.Append(dateTime.ToString("MM\\\\dd\\\\yy")); break;
            // F      date (yyyy-mm-dd)
            case 'F': target.Append(dateTime.ToString("yyyy-MM-dd")); break;
            // h      same as b
            case 'h': goto case 'b';
            // j      day of year (001..366)
            case 'j': target.Append(dateTime.DayOfYear.ToString("000")); break;
            // m      month (01..12)
            case 'm': target.AppendTwoDigitsLeftSpaced(dateTime.Month, '0'); break;
            // U      week number of year with Sunday as first day of week (00..53)
            case 'U':
                var weekOfYear = CultureInfo.InvariantCulture.Calendar
                    .GetWeekOfYear(dateTime, CalendarWeekRule.FirstFullWeek, DayOfWeek.Sunday);
                target.AppendTwoDigitsLeftSpaced(weekOfYear, '0');
                break;
            // w      day of week (0..6)
            case 'w': target.Append((int)dateTime.DayOfWeek); break;
            // W      week number of year with Monday as first day of week (00..53)
            case 'W':
                var weekOfYearMonday = CultureInfo.InvariantCulture.Calendar
                    .GetWeekOfYear(dateTime, CalendarWeekRule.FirstFullWeek, DayOfWeek.Monday);
                target.AppendTwoDigitsLeftSpaced(weekOfYearMonday, '0');
                break;
            // x      locale's date representation (mm/dd/yy)
            // TODO: Uh, is it just 'd' or is it current locale based?
            case 'x': goto case 'D';
            // y      last two digits of year (00..99)
            case 'y': target.Append(dateTime.ToString("yy")); break;
            // Y      year (1970...)
            case 'Y': target.Append(dateTime.Year); break;
            default:
                throw new ArgumentOutOfRangeException("TODO");
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
                    throw new ArgumentException("Final character can not be an unescaped backslash.", nameof(format));
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
                        throw new ArgumentOutOfRangeException(
                            nameof(format), octalValue,
                            $"Unexpected escaped character value. Value is greater than {char.MaxValue}.");
                    }
                    commands.Add(new PrintFPrinterCommandLiteral(((char)octalValue).ToString()));
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
                    case 'A':
                    case 'B':
                    case 'C':
                    case 'T':
                        if (i == format.Length - 1) throw new ArgumentOutOfRangeException("TODO");
                        commands.Add(new PrintFPrinterCommandDirective(directiveChar, format[++i]));
                        break;
                    default:
                        // This breaks from the existing implementation of find, which will simply print this
                        // character out. That's... such a bad idea, that I'm breaking from the implementation there.
                        throw new ArgumentOutOfRangeException(
                            nameof(format), directiveChar,
                            "Unknown directive character in printf format string.");
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
