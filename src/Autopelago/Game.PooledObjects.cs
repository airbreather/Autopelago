using System.Buffers;
using System.Collections;
using System.Diagnostics;

using Microsoft.Extensions.ObjectPool;

namespace Autopelago;

public sealed partial class Game : IDisposable
{
    private readonly BorrowedBitArray b_hardLockedRegions = BorrowedBitArray.ForRegions(); private BitArray _hardLockedRegions => b_hardLockedRegions.Value;
    private readonly BorrowedBitArray b_softLockedRegions = BorrowedBitArray.ForRegions(); private BitArray _softLockedRegions => b_softLockedRegions.Value;

    private readonly Borrowed<List<LocationVector>> b_prevMovementLog = new(); private List<LocationVector> _prevMovementLog => b_prevMovementLog.Value;
    private readonly Borrowed<List<LocationVector>> b_movementLog = new(); private List<LocationVector> _movementLog => b_movementLog.Value;

    private readonly Borrowed<Queue<LocationKey>> b_pathToTarget = new(); private Queue<LocationKey> _pathToTarget => b_pathToTarget.Value;

    private readonly Borrowed<List<ItemKey>> b_receivedItemsOrder = new(); private List<ItemKey> _receivedItemsOrder => b_receivedItemsOrder.Value;
    private readonly BorrowedArray<int> b_receivedItems = new(GameDefinitions.Instance.AllItems.Length); private Span<int> _receivedItems => b_receivedItems.Value;

    private readonly Borrowed<List<LocationKey>> b_checkedLocationsOrder = new(); private List<LocationKey> _checkedLocationsOrder => b_checkedLocationsOrder.Value;
    private readonly BorrowedBitArray b_checkedLocations = BorrowedBitArray.ForLocations(); private BitArray _checkedLocations => b_checkedLocations.Value;
    private readonly BorrowedArray<int> b_regionUncheckedLocationsCount = new(GameDefinitions.Instance.AllRegions.Length); private Span<int> _regionUncheckedLocationsCount => b_regionUncheckedLocationsCount.Value;

    private readonly Borrowed<List<LocationKey>> b_priorityPriorityLocations = new(); private List<LocationKey> _priorityPriorityLocations => b_priorityPriorityLocations.Value;
    private readonly Borrowed<List<LocationKey>> b_priorityLocations = new(); private List<LocationKey> _priorityLocations => b_priorityLocations.Value;

    private readonly Borrowed<List<LocationKey>> b_prevPath = new(); private List<LocationKey> _prevPath => b_prevPath.Value;

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

        _receivedItems.Clear();
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

public sealed class Borrowed<T> : IDisposable
    where T : class, new()
{
    private static readonly ObjectPool<T> s_pool = ObjectPool.Create<T>();

    private T? _obj = s_pool.Get();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _obj, null) is { } obj)
        {
            s_pool.Return(obj);
        }
    }

    public T Value
    {
        get
        {
            ObjectDisposedException.ThrowIf(_obj is null, this);
            return _obj;
        }
    }
}

public sealed class BorrowedBitArray : IDisposable
{
    private static readonly ObjectPool<BitArray> s_regionBitArray = new DefaultObjectPool<BitArray>(new AnonymousPolicy<BitArray>(() => new(GameDefinitions.Instance.AllRegions.Length)));

    private static readonly ObjectPool<BitArray> s_locationBitArray = new DefaultObjectPool<BitArray>(new AnonymousPolicy<BitArray>(() => new(GameDefinitions.Instance.AllLocations.Length)));

    private readonly string _stackTrace = Environment.StackTrace;

    private readonly ObjectPool<BitArray> _pool;

    private BitArray? _obj;

    private BorrowedBitArray(ObjectPool<BitArray> pool)
    {
        _pool = pool;
        _obj = pool.Get();
        _obj.SetAll(false);
    }

    ~BorrowedBitArray()
    {
        Console.WriteLine($"AAAAAAAAAAAAA!!! BIT ARRAY!!! {_stackTrace}");
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
        if (Interlocked.Exchange(ref _obj, null) is { } obj)
        {
            _pool.Return(obj);
            GC.SuppressFinalize(this);
        }
    }

    public BitArray Value
    {
        get
        {
            ObjectDisposedException.ThrowIf(_obj is null, this);
            return _obj;
        }
    }
}

public sealed class BorrowedArray<T> : IDisposable
{
    private T[]? _value;

    private readonly int _length;

    public BorrowedArray(int length)
    {
        _value = ArrayPool<T>.Shared.Rent(length);
        _length = length;
        Array.Clear(_value, 0, _length);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _value, null) is { } array)
        {
            ArrayPool<T>.Shared.Return(array);
        }
    }

    public Span<T> Value
    {
        get
        {
            ObjectDisposedException.ThrowIf(_value is null, this);
            return _value.AsSpan(0, _length);
        }
    }
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
