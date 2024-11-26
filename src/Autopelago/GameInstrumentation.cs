using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace Autopelago;

public enum GameEventId : byte
{
    StartStep,
    StopStep,
    ProcessNegativeFood,
    ProcessPositiveFood,
    ProcessDistraction,
    StartSubstep,
    StopSubstep,
    KeepTargetLocation,
    SwitchTargetLocation,
    ProcessNegativeEnergy,
    ProcessPositiveEnergy,
    MoveOnce,
    DoneMoving,
    ProcessNegativeLuck,
    ProcessPositiveLuck,
    ProcessPositiveStyle,
    TryLocation,
    ClearPriority,
    ClearPriorityPriority,
    DeductNextMovement,
    ProcessPositiveStartled,
    ReceiveItem,
    AddConfidence,
    SubtractConfidence,
    AddPriorityPriorityLocation,
    FizzlePriorityPriorityLocation,
}

public sealed class GameInstrumentation : IDisposable
{
    private readonly CompositeDisposable _disposables = [];

    private readonly Subject<GameEventId> _gameEvents = new();

    public GameInstrumentation()
    {
        _disposables.Add(_gameEvents);
        GameEvents = _gameEvents.AsObservable();
    }

    public IObservable<GameEventId> GameEvents { get; }

    public void Trace(GameEventId gameEventId)
    {
        _gameEvents.OnNext(gameEventId);
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
