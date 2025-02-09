using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Autopelago;

public interface IWeighted
{
    int Weight { get; }
}

public sealed class WeightedRandomItems<T> where T : IWeighted
{
    private readonly ImmutableArray<double> _normalizedWeights;

    public WeightedRandomItems(ImmutableArray<T> items)
    {
        if (items.IsDefaultOrEmpty)
        {
            throw new ArgumentException("Must have at least one item.", nameof(items));
        }

        Items = items;

        double sum = 0;
        double[] weights = new double[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            weights[i] = items[i].Weight;
            sum += weights[i];
        }

        double prev = 0;
        for (int i = 0; i < items.Length; i++)
        {
            double num = prev + weights[i];
            prev = num;
            weights[i] = num / sum;
        }

        _normalizedWeights = ImmutableCollectionsMarshal.AsImmutableArray(weights);
    }

    [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
    public ImmutableArray<T> Items { get; }

    public T QueryByRoll(double q)
    {
        if (q is not (>= 0 and < 1))
        {
            throw new ArgumentOutOfRangeException(nameof(q), q, "Must be between [0, 1)");
        }

        int i = _normalizedWeights.BinarySearch(q);
        if (i < 0)
        {
            i = ~i;
        }

        return Items[i];
    }
}
