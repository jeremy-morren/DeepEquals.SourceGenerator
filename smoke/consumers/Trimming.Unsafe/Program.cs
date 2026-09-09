using System;

namespace DeepEquals.Smoke.Trimming
{
    public static class Program
    {
        public static int Main()
        {
            // The line the trimming warnings must point at: the consumer's own use of a comparer that
            // reaches a generic holder's private field.
            bool equal = TrimmingContext.Risky.Equals(new Risky { Inner = new Box<int>(1) }, new Risky { Inner = new Box<int>(1) });
            Console.WriteLine(equal ? "OK" : "FAIL");
            return equal ? 0 : 1;
        }
    }
}
