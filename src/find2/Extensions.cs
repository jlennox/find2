using System;
using System.Runtime.CompilerServices;

namespace find2
{
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
    }
}
