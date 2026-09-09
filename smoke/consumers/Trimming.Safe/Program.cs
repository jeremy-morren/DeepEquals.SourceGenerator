using System;

namespace DeepEquals.Smoke.Trimming
{
    public static class Program
    {
        public static int Main()
        {
            bool equal = TrimmingContext.Safe.Equals(new Safe(1) { Shown = 2 }, new Safe(1) { Shown = 2 });
            Console.WriteLine(equal ? "OK" : "FAIL");
            return equal ? 0 : 1;
        }
    }
}
