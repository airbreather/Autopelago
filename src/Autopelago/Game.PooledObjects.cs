using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;

using Microsoft.Extensions.ObjectPool;

namespace Autopelago;

public sealed partial class Game : IDisposable
{
    private readonly BitArray _hardLockedRegions = Pools.RegionBitArray.Get();
    private readonly BitArray _softLockedRegions = Pools.RegionBitArray.Get();

    private readonly List<LocationVector> _prevMovementLog = Pools<List<LocationVector>>.Get(MaxMovementsPerStep);
    private readonly List<LocationVector> _movementLog = Pools<List<LocationVector>>.Get(MaxMovementsPerStep);

    private readonly Queue<LocationKey> _pathToTarget = Pools<Queue<LocationKey>>.Get(GameDefinitions.Instance.AllLocations.Length);

    private readonly List<ItemKey> _receivedItemsOrder = Pools<List<ItemKey>>.Get(GameDefinitions.Instance.AllItems.Length);
    private readonly ArraySegment<int> _receivedItems = RentArray<int>(GameDefinitions.Instance.AllItems.Length);

    private readonly List<LocationKey> _checkedLocationsOrder = Pools<List<LocationKey>>.Get(GameDefinitions.Instance.AllLocations.Length);
    private readonly BitArray _checkedLocations = Pools.LocationBitArray.Get();
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
        _hardLockedRegions.SetAll(true);
        _hardLockedRegions[GameDefinitions.Instance.StartRegion.N] = false;
        _softLockedRegions.SetAll(true);
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
        PriorityPriorityLocations = new(_priorityPriorityLocations);
        PriorityLocations = new(_priorityLocations);
    }

    public void Dispose()
    {
        Pools<List<LocationKey>>.Return(_prevPath);

        Pools<List<LocationKey>>.Return(_priorityLocations);
        Pools<List<LocationKey>>.Return(_priorityPriorityLocations);

        ArrayPool<int>.Shared.Return(_regionUncheckedLocationsCount.Array!);
        Pools.LocationBitArray.Return(_checkedLocations);
        Pools<List<LocationKey>>.Return(_checkedLocationsOrder);

        ArrayPool<int>.Shared.Return(_receivedItems.Array!);
        Pools<List<ItemKey>>.Return(_receivedItemsOrder);

        Pools<Queue<LocationKey>>.Return(_pathToTarget);

        Pools<List<LocationVector>>.Return(_movementLog);
        Pools<List<LocationVector>>.Return(_prevMovementLog);

        Pools.RegionBitArray.Return(_softLockedRegions);
        Pools.RegionBitArray.Return(_hardLockedRegions);
    }

    private static ArraySegment<T> RentArray<T>(int length)
    {
        T[] array = ArrayPool<T>.Shared.Rent(length);
        ArraySegment<T> segment = new(array, 0, length);
        segment.AsSpan().Clear();
        return segment;
    }

    private static Borrowed<BitArray> BorrowLocationsBitArray()
    {
        Borrowed<BitArray> result = new(Pools.LocationBitArray.Get(), Pools.LocationBitArray);
        result.Value.SetAll(false);
        return result;
    }

    private static Borrowed<BitArray> BorrowRegionsBitArray()
    {
        Borrowed<BitArray> result = new(Pools.RegionBitArray.Get(), Pools.RegionBitArray);
        result.Value.SetAll(false);
        return result;
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

    public static Borrowed<T> GetWrapped()
    {
        return new(Pools.Easy<T>().Get(), Pools.Easy<T>());
    }

    public static T Get()
    {
        return Pools.Easy<T>().Get();
    }

    public static T Get(int capacity)
    {
        T result = Pools.Easy<T>().Get();
        try
        {
            s_ensureCapacity?.Invoke(result, capacity);
            return result;
        }
        catch
        {
            Pools.Easy<T>().Return(result);
            throw;
        }
    }

    public static void Return(T obj)
    {
        Pools.Easy<T>().Return(obj);
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

file static class Pools
{
    public static readonly ObjectPool<BitArray> RegionBitArray = new MyPool<BitArray>(() => new(GameDefinitions.Instance.AllRegions.Length));

    public static readonly ObjectPool<BitArray> LocationBitArray = new MyPool<BitArray>(() => new(GameDefinitions.Instance.AllLocations.Length));

    public static ObjectPool<T> Easy<T>()
        where T : class, new()
    {
        return BulkPools<T>.Pool;
    }

    private static class BulkPools<T>
        where T : class, new()
    {
        public static readonly ObjectPool<T> Pool = new MyPool<T>(() => new());
    }
}

file sealed class MyPool<T> : ObjectPool<T>
    where T : class
{
    private static readonly Action<T>? s_clear = GetClearMethod();

    private readonly ConcurrentBag<T> _bag = [];
    private readonly Func<T> _create;

    public MyPool(Func<T> create)
    {
        _create = create;
    }

    public override T Get()
    {
        return _bag.TryTake(out T? item) ? item : _create();
    }

    public override void Return(T obj)
    {
        if (_bag.Count < 100)
        {
            s_clear?.Invoke(obj);
            _bag.Add(obj);
        }
    }

    private static Action<T>? GetClearMethod()
    {
        MethodInfo? method = typeof(T).GetMethod("Clear", BindingFlags.Instance | BindingFlags.Public, []);
        if (method is null)
        {
            return null;
        }

        ParameterExpression thisParam = Expression.Parameter(typeof(T), "this");
        MethodCallExpression methodCall = Expression.Call(thisParam, method);
        return Expression.Lambda<Action<T>>(methodCall, thisParam).Compile();
    }
}
