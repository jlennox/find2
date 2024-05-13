using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace find2;

internal static class Extensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TryDispose<T>(this T? disposable) where T : IDisposable
    {
        if (disposable == null) return;

        try
        {
            disposable.Dispose();
        }
        catch { }
    }

    public static void AppendAsciiDateTime(this StringBuilder sb, DateTime dateTime)
    {
        sb.Append(dateTime.ToString("ddd MMM "));
        sb.AppendTwoDigitsLeftSpaced(dateTime.Day);
        sb.Append(dateTime.ToString(" HH:mm:ss.fffffff000 yyyy"));
    }

    public static void AppendAsciiDateTimeNoFractions(this StringBuilder sb, DateTime dateTime)
    {
        sb.Append(dateTime.ToString("ddd MMM "));
        sb.AppendTwoDigitsLeftSpaced(dateTime.Day);
        sb.Append(dateTime.ToString(" HH:mm:ss yyyy"));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AppendTwoDigitsLeftSpaced(this StringBuilder sb, int digits, char padding = ' ')
    {
        if (digits < 10) sb.Append(padding);
        sb.Append(digits);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AppendTwoDigitsLeftSpaced(this StringBuilder sb, string digits, char padding = ' ')
    {
        if (digits.Length < 2) sb.Append(padding);
        sb.Append(digits);
    }
}