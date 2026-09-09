namespace DeepEquals.SourceGeneration.Framework;

/// <summary>Hash-only callback into a generated core. Implemented by a generated struct so the call devirtualizes.</summary>
public interface IDeepEqualsHashOps<T>
{
    int GetHashCode(T x);
}

/// <summary>Equality and hash callbacks into generated cores whose element closure needs comparison state.</summary>
public interface IDeepEqualsElementOps<T> : IDeepEqualsHashOps<T>
{
    bool Equals(T x, T y, ref DeepEqualsState state);
}

/// <summary>Equality and hash callbacks into generated cores whose element closure is wholly acyclic.</summary>
public interface IDeepEqualsStatelessElementOps<T> : IDeepEqualsHashOps<T>
{
    bool Equals(T x, T y);
}
