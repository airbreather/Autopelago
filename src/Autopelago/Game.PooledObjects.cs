using System.Buffers;
using System.Collections;

using Microsoft.Extensions.ObjectPool;

namespace Autopelago;

public sealed partial class Game : IDisposable
{
    private readonly BorrowedBitArray _hardLockedRegionsBorrow = BorrowedBitArray.ForRegions();
    private readonly BitArray _hardLockedRegions;

    private readonly BorrowedBitArray _softLockedRegionsBorrow = BorrowedBitArray.ForRegions();
    private readonly BitArray _softLockedRegions;


    private readonly Borrowed<List<LocationVector>> _prevMovementLogBorrow = new();
    private readonly List<LocationVector> _prevMovementLog;

    private readonly Borrowed<List<LocationVector>> _movementLogBorrow = new();
    private readonly List<LocationVector> _movementLog;


    private readonly Borrowed<Queue<LocationKey>> _pathToTargetBorrow = new();
    private readonly Queue<LocationKey> _pathToTarget;


    private readonly Borrowed<List<ItemKey>> _receivedItemsOrderBorrow = new();
    private readonly List<ItemKey> _receivedItemsOrder;

    private readonly BorrowedArray<int> _receivedItemsBorrow = new(GameDefinitions.Instance.AllItems.Length);
    private readonly ArraySegment<int> _receivedItems;


    private readonly Borrowed<List<LocationKey>> _checkedLocationsOrderBorrow = new();
    private readonly List<LocationKey> _checkedLocationsOrder;

    private readonly BorrowedBitArray _checkedLocationsBorrow = BorrowedBitArray.ForLocations();
    private readonly BitArray _checkedLocations;

    private readonly BorrowedArray<int> _regionUncheckedLocationsCountBorrow = new(GameDefinitions.Instance.AllRegions.Length);
    private readonly ArraySegment<int> _regionUncheckedLocationsCount;


    private readonly Borrowed<List<LocationKey>> _priorityPriorityLocationsBorrow = new();
    private readonly List<LocationKey> _priorityPriorityLocations;

    private readonly Borrowed<List<LocationKey>> _priorityLocationsBorrow = new();
    private readonly List<LocationKey> _priorityLocations;


    private readonly Borrowed<List<LocationKey>> _prevPathBorrow = new();
    private readonly List<LocationKey> _prevPath;

    public Game(Prng.State prngState)
        : this(prngState, null)
    {
    }

    public Game(Prng.State prngState, GameInstrumentation? instrumentation)
    {
        _hardLockedRegions = _hardLockedRegionsBorrow.Value; _hardLockedRegions.SetAll(true);
        _softLockedRegions = _softLockedRegionsBorrow.Value; _softLockedRegions.SetAll(true);
        _prevMovementLog = _prevMovementLogBorrow.Value; _prevMovementLog.Clear(); _prevMovementLog.EnsureCapacity(MaxMovementsPerStep);
        _movementLog = _movementLogBorrow.Value; _movementLog.Clear(); _movementLog.EnsureCapacity(MaxMovementsPerStep);
        _pathToTarget = _pathToTargetBorrow.Value; _pathToTarget.Clear();
        _receivedItemsOrder = _receivedItemsOrderBorrow.Value; _receivedItemsOrder.Clear(); _receivedItemsOrder.EnsureCapacity(GameDefinitions.Instance.AllLocations.Length);
        _receivedItems = _receivedItemsBorrow.Value; _receivedItems.AsSpan().Clear();
        _checkedLocationsOrder = _checkedLocationsOrderBorrow.Value; _checkedLocationsOrder.Clear(); _checkedLocationsOrder.EnsureCapacity(GameDefinitions.Instance.AllLocations.Length);
        _checkedLocations = _checkedLocationsBorrow.Value; _checkedLocations.SetAll(false);
        _regionUncheckedLocationsCount = _regionUncheckedLocationsCountBorrow.Value; _regionUncheckedLocationsCount.AsSpan().Clear();
        _priorityPriorityLocations = _priorityPriorityLocationsBorrow.Value; _priorityPriorityLocations.Clear();
        _priorityLocations = _priorityLocationsBorrow.Value; _priorityLocations.Clear();
        _prevPath = _prevPathBorrow.Value; _prevPath.Clear();

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
        _hardLockedRegionsBorrow.Dispose();
        _softLockedRegionsBorrow.Dispose();
        _prevMovementLogBorrow.Dispose();
        _movementLogBorrow.Dispose();
        _pathToTargetBorrow.Dispose();
        _receivedItemsOrderBorrow.Dispose();
        _receivedItemsBorrow.Dispose();
        _checkedLocationsOrderBorrow.Dispose();
        _checkedLocationsBorrow.Dispose();
        _regionUncheckedLocationsCountBorrow.Dispose();
        _priorityPriorityLocationsBorrow.Dispose();
        _priorityLocationsBorrow.Dispose();
        _prevPathBorrow.Dispose();
    }
}

public readonly struct Borrowed<T> : IDisposable
    where T : class, new()
{
    private static readonly ObjectPool<T> s_pool = new AggressivelyTunedObjectPool<T>(() => new());

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
    private static readonly ObjectPool<BitArray> s_regionBitArray = new AggressivelyTunedObjectPool<BitArray>(() => new(GameDefinitions.Instance.AllRegions.Length));

    private static readonly ObjectPool<BitArray> s_locationBitArray = new AggressivelyTunedObjectPool<BitArray>(() => new(GameDefinitions.Instance.AllLocations.Length));

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

// There is absolutely no practical reason for me to have gone as far as I did with this one. With
// that out of the way, the primary use case for caring about performance (even if for impractical
// reasons) is the million-sized data collection routine ("big-science" in the launch settings). In
// that run, tens of thousands of rat-only multiworlds are run in parallel. Each Game does a good
// enough job minimizing its own dynamic allocations via its own members that get reused. Without
// the ability to "reset" an instance of a Game class, though (which I don't want to do), letting it
// initialize those members from an object pool is the next-best thing. All of that is fine, though
// even there, it's overkill given the availability of hardware relative to the size of the problems
// and the frequency with which I solve them.
//
// Where it gets over-the-top ridiculous is where and how I deviated from DefaultObjectPool<T>. You
// see, most developers (including myself, when I'm expected to make more sane decisions at my day
// job) would plug in the default implementation like I did, see that it drastically cuts down on
// allocations and significantly improves the speed, and leave it there. The runtime of big-science
// was already down to something like 2 minutes on my primary development machine, where it used to
// be something more like 2.5 (after other recent performance optimizations had already brought it
// down from I think 5+), so that's pretty respectable on its own.
//
// But I saw that a huge amount of time was spent in the ConcurrentQueue<T> and knew that I could do
// better with my local knowledge of the usage patterns. In particular, I can make the following
// assumptions in this project that a general-purpose ConcurrentQueue<T> should never rely on:
// 1. The pool will be accessed by only a smallish number of threads that grow very slowly relative
//    to how long the application will ever need to run.
// 2. For a given thread, the usage pattern will *always* look like: a) allocate MANY long-lived
//    objects from the pool all in one go, b) do some work that involves those objects and some more
//    very short-lived ones that it politely returns right away, c) release ALL of those long-lived
//    objects in one go, d) either go back to a) or never touch this pool again.
// 3. There are never enough objects in-flight that are heavy enough to justify the overhead needed
//    to make it possible for a request on one thread to steal from another thread.
//
// Taking all those assumptions together, I *briefly* tried ConcurrentBag<T>, which I think brought
// it down to something more like 1.5 minutes, but because each Game would run for an unpredictable
// amount of time, the unnecessary stealing was dominating. A non-tracking ThreadLocal<Stack<T>> was
// starting to look just about right. This brought it all the way down to 0.3 minutes.
//
// ThreadLocal<T> is actually just a way to make [ThreadStatic] more usable in situations where you
// don't have enough (or reliable enough) static knowledge about the nature of your program. It adds
// overhead on top of that. In fact, I did experiment with using [ThreadStatic] directly, and it WAS
// a significant relative improvement. However, I cannot justify going that far. The fraction of the
// time spent in ThreadLocal<T>.get_Value is perfectly acceptable, and although I CAN make it work,
// the downside risk is too great. Minimally, I would have to create a whole separate pool for the
// second BitArray, which hints at the issue: it is quite useful to be able to create new instances
// of this pool and have them function fully independently of one another. With [ThreadStatic], I
// must either accept that there is effectively only one pool per type of T, or basically wind up
// reimplementing a worse version of ThreadLocal<T>. The impact at the time of writing is minimal,
// because "only one pool per type of T" is basically how it functions already at the next level of
// abstraction, but I would prefer to keep this fact there, not have it influence this.
file sealed class AggressivelyTunedObjectPool<T> : ObjectPool<T>
    where T : class
{
    private readonly ThreadLocal<Stack<T>> _stack = new();

    private readonly Func<T> _factory;

    public AggressivelyTunedObjectPool(Func<T> factory)
    {
        _factory = factory;
    }

    public override T Get()
    {
        return _stack.Value?.TryPop(out T? value) == true ? value : _factory();
    }

    public override void Return(T obj)
    {
        Stack<T> stack = _stack.Value ??= new(1);
        int newCapacity = Math.Max(stack.Count + 1, stack.Capacity);
        stack.Push(obj);
        stack.TrimExcess(newCapacity);
    }
}
