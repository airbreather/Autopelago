using System.Buffers;
using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;

using Microsoft.Extensions.ObjectPool;

namespace Autopelago;

public sealed partial class Game : IDisposable
{
    private static readonly DefaultObjectPool<BitArray> s_regionBitArrayPoolDefaultTrue = new(new BitArrayPoolPolicy(GameDefinitions.Instance.AllRegions.Length, true));

    private static readonly DefaultObjectPool<BitArray> s_locationBitArrayPoolDefaultFalse = new(new BitArrayPoolPolicy(GameDefinitions.Instance.AllLocations.Length, false));

    private readonly BitArray _hardLockedRegions = s_regionBitArrayPoolDefaultTrue.Get();
    private readonly BitArray _softLockedRegions = s_regionBitArrayPoolDefaultTrue.Get();

    private readonly List<LocationVector> _prevMovementLog = Pools<List<LocationVector>>.Get(MaxMovementsPerStep);
    private readonly List<LocationVector> _movementLog = Pools<List<LocationVector>>.Get(MaxMovementsPerStep);

    private readonly Queue<LocationKey> _pathToTarget = Pools<Queue<LocationKey>>.Get(GameDefinitions.Instance.AllLocations.Length);

    private readonly List<ItemKey> _receivedItemsOrder = Pools<List<ItemKey>>.Get(GameDefinitions.Instance.AllItems.Length);
    private readonly ArraySegment<int> _receivedItems = RentArray<int>(GameDefinitions.Instance.AllItems.Length);

    private readonly List<LocationKey> _checkedLocationsOrder = Pools<List<LocationKey>>.Get(GameDefinitions.Instance.AllLocations.Length);
    private readonly BitArray _checkedLocations = s_locationBitArrayPoolDefaultFalse.Get();
    private readonly ArraySegment<int> _regionUncheckedLocationsCount = RentArray<int>(GameDefinitions.Instance.AllRegions.Length);

    private readonly List<LocationKey> _priorityPriorityLocations = Pools<List<LocationKey>>.Get(GameDefinitions.Instance.AllLocations.Length);
    private readonly List<LocationKey> _priorityLocations = Pools<List<LocationKey>>.Get(GameDefinitions.Instance.AllLocations.Length);

    private readonly List<LocationKey> _prevPath = Pools<List<LocationKey>>.Get(GameDefinitions.Instance.AllLocations.Length);

    public Game(Prng.State prngState)
        : this(prngState, null)
    {
    }

    public Game(Prng.State prngState, GameInstrumentation? instrumentation)
    {
        _hardLockedRegions[GameDefinitions.Instance.StartRegion.N] = false;
        _softLockedRegions[GameDefinitions.Instance.StartRegion.N] = false;

        _receivedItems.AsSpan().Clear();
        Span<int> regionUncheckedLocationsCount = _regionUncheckedLocationsCount;
        foreach (ref readonly RegionDefinitionModel region in GameDefinitions.Instance.AllRegions.AsSpan())
        {
            regionUncheckedLocationsCount[region.Key.N] = region.Locations.Length;
        }

        _prngState = prngState;
        _instrumentation = instrumentation;
        _lock = instrumentation is null ? new() : null;
        _prevMovementLog.Add(new()
        {
            PreviousLocation = GameDefinitions.Instance.StartLocation,
            CurrentLocation = GameDefinitions.Instance.StartLocation,
        });
        PreviousStepMovementLog = _prevMovementLog.AsReadOnly();
        ReceivedItems = new(_receivedItemsOrder);
        LocationIsChecked = new(_checkedLocations);
        CheckedLocations = new(_checkedLocationsOrder);
    }

    public void Dispose()
    {
        Pools<List<LocationKey>>.Return(_prevPath);

        Pools<List<LocationKey>>.Return(_priorityLocations);
        Pools<List<LocationKey>>.Return(_priorityPriorityLocations);

        ArrayPool<int>.Shared.Return(_regionUncheckedLocationsCount.Array!);
        s_locationBitArrayPoolDefaultFalse.Return(_checkedLocations);
        Pools<List<LocationKey>>.Return(_checkedLocationsOrder);

        ArrayPool<int>.Shared.Return(_receivedItems.Array!);
        Pools<List<ItemKey>>.Return(_receivedItemsOrder);

        Pools<Queue<LocationKey>>.Return(_pathToTarget);

        Pools<List<LocationVector>>.Return(_movementLog);
        Pools<List<LocationVector>>.Return(_prevMovementLog);

        s_regionBitArrayPoolDefaultTrue.Return(_softLockedRegions);
        s_regionBitArrayPoolDefaultTrue.Return(_hardLockedRegions);
    }

    private static ArraySegment<T> RentArray<T>(int length)
    {
        T[] array = ArrayPool<T>.Shared.Rent(length);
        ArraySegment<T> segment = new(array, 0, length);
        return segment;
    }

    private static Borrowed<BitArray> BorrowLocationsBitArrayDefaultFalse()
    {
        return new(s_locationBitArrayPoolDefaultFalse.Get(), s_locationBitArrayPoolDefaultFalse);
    }

    private static Borrowed<BitArray> BorrowRegionsBitArrayDefaultTrue()
    {
        return new(s_regionBitArrayPoolDefaultTrue.Get(), s_regionBitArrayPoolDefaultTrue);
    }

    private static Borrowed<T> Borrow<T>()
        where T : class, new()
    {
        return Pools<T>.GetWrapped();
    }
}

[StructLayout(LayoutKind.Auto)]
public readonly struct Borrowed<T> : IDisposable
    where T : class
{
    private readonly ObjectPool<T> _pool;

    public Borrowed(T obj, ObjectPool<T> pool)
    {
        Value = obj;
        _pool = pool;
    }

    public T Value { get; }

    public void Dispose()
    {
        _pool.Return(Value);
    }
}

file static class Pools<T> where T : class, new()
{
    private static readonly Action<T, int>? s_ensureCapacity = GetEnsureCapacityMethod();

    private static readonly DefaultObjectPool<T> s_pool = new(new DefaultPooledObjectPolicy<T>());

    public static Borrowed<T> GetWrapped()
    {
        return new(Get(), s_pool);
    }

    public static T Get()
    {
        return s_pool.Get();
    }

    public static T Get(int capacity)
    {
        T result = s_pool.Get();
        try
        {
            s_ensureCapacity?.Invoke(result, capacity);
            return result;
        }
        catch
        {
            s_pool.Return(result);
            throw;
        }
    }

    public static void Return(T obj)
    {
        s_pool.Return(obj);
    }

    private static Action<T, int>? GetEnsureCapacityMethod()
    {
        MethodInfo? method = typeof(T).GetMethod("EnsureCapacity", BindingFlags.Instance | BindingFlags.Public, [typeof(int)]);
        if (method is null)
        {
            return null;
        }

        ParameterExpression thisParam = Expression.Parameter(typeof(T), "this");
        ParameterExpression capacityParam = Expression.Parameter(typeof(int), "capacity");
        MethodCallExpression methodCall = Expression.Call(thisParam, method, capacityParam);
        return Expression.Lambda<Action<T, int>>(methodCall, thisParam, capacityParam).Compile();
    }
}

file sealed class BitArrayPoolPolicy : PooledObjectPolicy<BitArray>
{
    private readonly int _length;

    private readonly bool _defaultValue;

    public BitArrayPoolPolicy(int length, bool defaultValue)
    {
        _length = length;
        _defaultValue = defaultValue;
    }

    public override BitArray Create()
    {
        return new(_length, _defaultValue);
    }

    public override bool Return(BitArray obj)
    {
        obj.SetAll(_defaultValue);
        return true;
    }
}
