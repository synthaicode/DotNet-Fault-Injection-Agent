using System;
using System.Runtime.CompilerServices;

internal static class Program
{
    private static void Main()
    {
        Console.WriteLine("64-bit process: " + Environment.Is64BitProcess);
        Console.WriteLine("GetValue: " + GetValue());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int GetValue()
    {
        return 42;
    }
}
