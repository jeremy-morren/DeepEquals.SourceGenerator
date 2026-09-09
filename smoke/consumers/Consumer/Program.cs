using System;

namespace DeepEquals.Smoke
{
    /// <summary>
    /// Present on the netstandard legs too, where it is never called; those exist to prove the
    /// generated code compiles against that reference surface.
    /// </summary>
    public static class Program
    {
        public static int Main()
        {
            string result = Checks.Run();
            Console.WriteLine(result);
            return result.StartsWith("OK", StringComparison.Ordinal) ? 0 : 1;
        }
    }
}
