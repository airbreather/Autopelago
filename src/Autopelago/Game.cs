using System.Collections.Immutable;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

using ArchipelagoClientDotNet;

namespace Autopelago;

public sealed record StepStartedEventArgs
{
    public required Game.State StateBeforeAdvance { get; init; }
}

public sealed record StepFinishedEventArgs
{
    public required Game.State StateBeforeAdvance { get; init; }

    public required Game.State StateAfterAdvance { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(Game.State.Proxy))]
internal sealed partial class GameStateProxySerializerContext : JsonSerializerContext
{
}

public static class Game
{
    public sealed record State
    {
        private State()
        {
        }

        private State(State copyFrom)
        {
            Epoch = copyFrom.Epoch + 1;
            CurrentLocation = copyFrom.CurrentLocation;
            TargetLocation = copyFrom.TargetLocation;
            ReceivedItems = copyFrom.ReceivedItems;
            CheckedLocations = copyFrom.CheckedLocations;
            PrngState = copyFrom.PrngState;
            LocationCheckAttemptsThisStep = copyFrom.LocationCheckAttemptsThisStep;
        }

        public ulong Epoch { get; private init; }

        public required LocationDefinitionModel CurrentLocation { get; init; }

        public required LocationDefinitionModel TargetLocation { get; init; }

        public required ImmutableList<ItemDefinitionModel> ReceivedItems { get; init; }

        public required ImmutableList<LocationDefinitionModel> CheckedLocations { get; init; }

        public required int LocationCheckAttemptsThisStep { get; init; }

        public required Prng.State PrngState { get; init; }

        public bool IsCompleted => CurrentLocation == GameDefinitions.Instance.GoalLocation;

        public int DiceModifier => (RatCount / 3) - (LocationCheckAttemptsThisStep * 5);

        public int RatCount => ReceivedItems.Sum(i => i.RatCount).GetValueOrDefault();

        public static State Start(Random? random = null)
        {
            return Start(unchecked((ulong)(random ?? Random.Shared).NextInt64()));
        }

        public static State Start(ulong seed)
        {
            return Start(Prng.State.Start(seed));
        }

        public static State Start(Prng.State prngState)
        {
            return new()
            {
                CurrentLocation = GameDefinitions.Instance.StartLocation,
                TargetLocation = GameDefinitions.Instance.StartLocation,
                ReceivedItems = [],
                CheckedLocations = [],
                LocationCheckAttemptsThisStep = 0,
                PrngState = prngState,
            };
        }

        public static int NextD20(ref State state)
        {
            Prng.State s = state.PrngState;
            int result = Prng.NextD20(ref s);
            state = state with { PrngState = s };
            return result;
        }

        public Proxy ToProxy()
        {
            return new()
            {
                Epoch = Epoch,
                CurrentLocation = CurrentLocation.Name,
                TargetLocation = TargetLocation.Name,
                ReceivedItems = [.. ReceivedItems.Select(i => i.Name)],
                CheckedLocations = [.. CheckedLocations.Select(l => l.Name)],
                LocationCheckAttemptsThisStep = LocationCheckAttemptsThisStep,
                PrngState = PrngState,
            };
        }

        public bool Equals(State? other)
        {
            return
                other is not null &&
                Epoch == other.Epoch &&
                PrngState == other.PrngState &&
                CurrentLocation == other.CurrentLocation &&
                TargetLocation == other.TargetLocation &&
                LocationCheckAttemptsThisStep == other.LocationCheckAttemptsThisStep &&
                ReceivedItems.SequenceEqual(other.ReceivedItems) &&
                CheckedLocations.SequenceEqual(other.CheckedLocations);
        }

        public override int GetHashCode() => Epoch.GetHashCode();

        public sealed record Proxy
        {
            public static readonly JsonSerializerOptions SerializerOptions = new()
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                TypeInfoResolver = GameStateProxySerializerContext.Default,
            };

            public ulong Epoch { get; init; }

            public required string CurrentLocation { get; init; }

            public required string TargetLocation { get; init; }

            public required ImmutableArray<string> ReceivedItems { get; init; }

            public required ImmutableArray<string> CheckedLocations { get; init; }

            public required int LocationCheckAttemptsThisStep { get; init; }

            public Prng.State PrngState { get; init; }

            public State ToState()
            {
                return new()
                {
                    Epoch = Epoch,
                    CurrentLocation = GameDefinitions.Instance.LocationsByName[CurrentLocation],
                    TargetLocation = GameDefinitions.Instance.LocationsByName[TargetLocation],
                    ReceivedItems = [.. ReceivedItems.Select(name => GameDefinitions.Instance.ItemsByName[name])],
                    CheckedLocations = [.. CheckedLocations.Select(name => GameDefinitions.Instance.LocationsByName[name])],
                    LocationCheckAttemptsThisStep = LocationCheckAttemptsThisStep,
                    PrngState = PrngState,
                };
            }
        }
    }

    public static IObservable<State> Run(State state, AutopelagoClient client, IScheduler timeScheduler)
    {
        Player player = new();

        IObservable<State> playerTransitions =
            Observable
                .Interval(TimeSpan.FromSeconds(1), timeScheduler)
                .Select(_ => state = player.Advance(state));

        IObservable<State> receivedItemTransitions =
            client.ReceivedItemsEvents
                .ObserveOn(timeScheduler)
                .Select(args =>
                {
                    for (int i = args.Index; i < state.ReceivedItems.Count; i++)
                    {
                        if (args.Items[i - args.Index] != state.ReceivedItems[i])
                        {
                            throw new NotImplementedException("Need to resync.");
                        }
                    }

                    ImmutableArray<ItemDefinitionModel> newItems = args.Items[(state.ReceivedItems.Count - args.Index)..];
                    if (newItems.Length > 0)
                    {
                        state = state with { ReceivedItems = state.ReceivedItems.AddRange(newItems) };
                    }

                    return state;
                });

        State stateAtLastUpdate = state;
        return Observable
            .Merge(playerTransitions, receivedItemTransitions)
            .TakeUntil(_ => stateAtLastUpdate.IsCompleted)
            .Do(_ => // ignore the incremental transition
            {
                State stateToUpdate = state;
                if (stateAtLastUpdate.Epoch == stateToUpdate.Epoch)
                {
                    return;
                }

                if (stateAtLastUpdate.CheckedLocations.Count < stateToUpdate.CheckedLocations.Count)
                {
                    client.SendLocationChecksAsync(stateToUpdate.CheckedLocations.Except(stateAtLastUpdate.CheckedLocations), CancellationToken.None).WaitMoreSafely();
                }

                stateAtLastUpdate = stateToUpdate;
            });
    }
}
