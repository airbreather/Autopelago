using System.Buffers;
using System.Collections;

using Microsoft.Extensions.ObjectPool;

namespace Autopelago;

public sealed partial class Game : IDisposable
{
    private readonly BorrowedBitArray b_hardLockedRegions = BorrowedBitArray.ForRegions();
    private readonly BitArray _hardLockedRegions;

    private readonly BorrowedBitArray b_softLockedRegions = BorrowedBitArray.ForRegions();
    private readonly BitArray _softLockedRegions;


    private readonly Borrowed<List<LocationVector>> b_prevMovementLog = new();
    private readonly List<LocationVector> _prevMovementLog;

    private readonly Borrowed<List<LocationVector>> b_movementLog = new();
    private readonly List<LocationVector> _movementLog;


    private readonly Borrowed<Queue<LocationKey>> b_pathToTarget = new();
    private readonly Queue<LocationKey> _pathToTarget;


    private readonly Borrowed<List<ItemKey>> b_receivedItemsOrder = new();
    private readonly List<ItemKey> _receivedItemsOrder;

    private readonly BorrowedArray<int> b_receivedItems = new(GameDefinitions.Instance.AllItems.Length);
    private readonly ArraySegment<int> _receivedItems;


    private readonly Borrowed<List<LocationKey>> b_checkedLocationsOrder = new();
    private readonly List<LocationKey> _checkedLocationsOrder;

    private readonly BorrowedBitArray b_checkedLocations = BorrowedBitArray.ForLocations();
    private readonly BitArray _checkedLocations;

    private readonly BorrowedArray<int> b_regionUncheckedLocationsCount = new(GameDefinitions.Instance.AllRegions.Length);
    private readonly ArraySegment<int> _regionUncheckedLocationsCount;


    private readonly Borrowed<List<LocationKey>> b_priorityPriorityLocations = new();
    private readonly List<LocationKey> _priorityPriorityLocations;

    private readonly Borrowed<List<LocationKey>> b_priorityLocations = new();
    private readonly List<LocationKey> _priorityLocations;


    private readonly Borrowed<List<LocationKey>> b_prevPath = new();
    private readonly List<LocationKey> _prevPath;

    public Game(Prng.State prngState)
        : this(prngState, null)
    {
    }

    public Game(Prng.State prngState, GameInstrumentation? instrumentation)
    {
        _hardLockedRegions = b_hardLockedRegions.Value; _hardLockedRegions.SetAll(true);
        _softLockedRegions = b_softLockedRegions.Value; _softLockedRegions.SetAll(true);
        _prevMovementLog = b_prevMovementLog.Value; _prevMovementLog.Clear(); _prevMovementLog.EnsureCapacity(GameDefinitions.Instance.AllLocations.Length);
        _movementLog = b_movementLog.Value; _movementLog.Clear(); _movementLog.EnsureCapacity(GameDefinitions.Instance.AllLocations.Length);
        _pathToTarget = b_pathToTarget.Value; _pathToTarget.Clear(); _pathToTarget.EnsureCapacity(GameDefinitions.Instance.AllLocations.Length);
        _receivedItemsOrder = b_receivedItemsOrder.Value; _receivedItemsOrder.Clear(); _receivedItemsOrder.EnsureCapacity(GameDefinitions.Instance.AllLocations.Length); // yes locations
        _receivedItems = b_receivedItems.Value; _receivedItems.AsSpan().Clear();
        _checkedLocationsOrder = b_checkedLocationsOrder.Value; _checkedLocationsOrder.Clear(); _checkedLocationsOrder.EnsureCapacity(GameDefinitions.Instance.AllLocations.Length);
        _checkedLocations = b_checkedLocations.Value; _checkedLocations.SetAll(false);
        _regionUncheckedLocationsCount = b_regionUncheckedLocationsCount.Value; _regionUncheckedLocationsCount.AsSpan().Clear();
        _priorityPriorityLocations = b_priorityPriorityLocations.Value; _priorityPriorityLocations.Clear(); _priorityPriorityLocations.EnsureCapacity(GameDefinitions.Instance.AllLocations.Length);
        _priorityLocations = b_priorityLocations.Value; _priorityLocations.Clear(); _priorityLocations.EnsureCapacity(GameDefinitions.Instance.AllLocations.Length);
        _prevPath = b_prevPath.Value; _prevPath.Clear(); _prevPath.EnsureCapacity(GameDefinitions.Instance.AllLocations.Length);

        _hardLockedRegions[GameDefinitions.Instance.StartRegion.N] = false;
        _softLockedRegions[GameDefinitions.Instance.StartRegion.N] = false;

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
        b_hardLockedRegions.Dispose();
        b_softLockedRegions.Dispose();
        b_prevMovementLog.Dispose();
        b_movementLog.Dispose();
        b_pathToTarget.Dispose();
        b_receivedItemsOrder.Dispose();
        b_receivedItems.Dispose();
        b_checkedLocationsOrder.Dispose();
        b_checkedLocations.Dispose();
        b_regionUncheckedLocationsCount.Dispose();
        b_priorityPriorityLocations.Dispose();
        b_priorityLocations.Dispose();
        b_prevPath.Dispose();
    }
}

public readonly struct Borrowed<T> : IDisposable
    where T : class, new()
{
    private static readonly ObjectPool<T> s_pool = ObjectPool.Create<T>();

    public Borrowed()
    {
        Value = s_pool.Get();
    }

    public void Dispose()
    {
        s_pool.Return(Value);
    }

    public T Value { get; }
}

public readonly struct BorrowedBitArray : IDisposable
{
    private static readonly ObjectPool<BitArray> s_regionBitArray = new DefaultObjectPool<BitArray>(new AnonymousPolicy<BitArray>(() => new(GameDefinitions.Instance.AllRegions.Length)));

    private static readonly ObjectPool<BitArray> s_locationBitArray = new DefaultObjectPool<BitArray>(new AnonymousPolicy<BitArray>(() => new(GameDefinitions.Instance.AllLocations.Length)));

    private readonly ObjectPool<BitArray> _pool;

    private BorrowedBitArray(ObjectPool<BitArray> pool)
    {
        _pool = pool;
        Value = pool.Get();
    }

    public static BorrowedBitArray ForRegions()
    {
        return new(s_regionBitArray);
    }

    public static BorrowedBitArray ForLocations()
    {
        return new(s_locationBitArray);
    }

    public void Dispose()
    {
        _pool.Return(Value);
    }

    public BitArray Value { get; }
}

public readonly struct BorrowedArray<T> : IDisposable
{
    public BorrowedArray(int length)
    {
        Value = new(ArrayPool<T>.Shared.Rent(length), 0, length);
    }

    public void Dispose()
    {
        ArrayPool<T>.Shared.Return(Value.Array!);
    }

    public ArraySegment<T> Value { get; }
}

file sealed class AnonymousPolicy<T> : PooledObjectPolicy<T>
    where T : class
{
    private readonly Func<T> _factory;

    public AnonymousPolicy(Func<T> factory)
    {
        _factory = factory;
    }

    public override T Create()
    {
        return _factory();
    }

    public override bool Return(T obj)
    {
        return true;
    }
}
