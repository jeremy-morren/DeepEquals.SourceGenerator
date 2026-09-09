using DeepEquals.SourceGeneration;

namespace DeepEquals.Smoke.Trimming
{
    /// <summary>A plain type: its comparer reaches the private field without reflection on every tier.</summary>
    public sealed class Safe
    {
        private int _hidden;

        public Safe(int hidden) { _hidden = hidden; }

        public int Shown { get; set; }
    }

    /// <summary>A generic holder with a private field: the shape that needs a delegate below net10.0.</summary>
    public sealed class Box<T>
    {
        private T _hidden;

        public Box(T hidden) { _hidden = hidden; }
    }

    /// <summary>Reaches <see cref="Box{T}"/>, so its comparer is the one that can be trim-unsafe.</summary>
    public sealed class Risky
    {
        public Box<int> Inner { get; set; }
    }

    [GenerateDeepEquals(typeof(Safe))]
    [GenerateDeepEquals(typeof(Risky))]
    public partial class TrimmingContext : DeepEqualsContextBase { }
}
