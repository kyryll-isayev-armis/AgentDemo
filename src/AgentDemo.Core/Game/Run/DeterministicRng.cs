using System.Text;

namespace AgentDemo.Web.Game.Run;

/// <summary>SplitMix64 generator. Named streams are derived from the seed only, so they never depend on call order.</summary>
public sealed class DeterministicRng
{
    const ulong Gamma = 0x9E3779B97F4A7C15UL;
    ulong _state;

    public DeterministicRng(ulong seed)
    {
        Seed = seed;
        _state = seed;
    }

    public ulong Seed { get; }

    public ulong NextUInt64()
    {
        unchecked
        {
            _state += Gamma;
            return Mix(_state);
        }
    }

    public int NextInt(int maxExclusive)
    {
        if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        return (int)(NextUInt64() % (ulong)maxExclusive);
    }

    public bool Chance(int percent) => NextInt(100) < percent;

    public T Pick<T>(IReadOnlyList<T> items)
    {
        if (items.Count == 0) throw new ArgumentException("Cannot pick from an empty list.", nameof(items));
        return items[NextInt(items.Count)];
    }

    public DeterministicRng Fork(string name) => new(Mix(unchecked(Seed ^ Fnv1a(name))));

    public static ulong Mix(ulong z)
    {
        unchecked
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    static ulong Fnv1a(string text)
    {
        unchecked
        {
            var h = 14695981039346656037UL;
            foreach (var b in Encoding.UTF8.GetBytes(text))
            {
                h ^= b;
                h *= 1099511628211UL;
            }
            return h;
        }
    }
}
