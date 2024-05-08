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
        if (dateTime.Day < 10) sb.Append(' ');
        sb.Append(dateTime.Day);
        sb.Append(dateTime.ToString(" HH:mm:ss.fffffff000 yyyy"));
    }
}