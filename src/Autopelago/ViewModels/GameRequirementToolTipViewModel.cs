using System.Collections.Immutable;
using System.Reactive.Disposables;

using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Autopelago.ViewModels;

public sealed class GameRequirementToolTipViewModel : ViewModelBase, IDisposable
{
    private readonly CompositeDisposable _subscription = [];

    public GameRequirementToolTipViewModel(GameRequirement req)
    {
        Model = req;
        switch (req)
        {
            case RatCountRequirement { RatCount: 1 }:
                MyContent = "1 rat";
                break;

            case RatCountRequirement { RatCount: int ratCount }:
                MyContent = $"{ratCount} rats";
                break;

            case ReceivedItemRequirement { ItemKey: string itemKey }:
                MyContent = $"{GameDefinitions.Instance.ProgressionItems[itemKey].Name} (item)";
                break;

            case AllChildrenGameRequirement { Children: ImmutableArray<GameRequirement> children }:
                HeaderContent = "All:";
                Children = ImmutableArray.CreateRange(children, child => new GameRequirementToolTipViewModel(child));
                foreach (GameRequirementToolTipViewModel child in Children)
                {
                    _subscription.Add(child
                        .WhenAnyValue(x => x.Satisfied)
                        .Subscribe(_ => Satisfied = Children.All(x => x.Satisfied)));
                }

                break;

            case AnyChildGameRequirement { Children: ImmutableArray<GameRequirement> children }:
                HeaderContent = "Any:";
                Children = ImmutableArray.CreateRange(children, child => new GameRequirementToolTipViewModel(child));
                foreach (GameRequirementToolTipViewModel child in Children)
                {
                    _subscription.Add(child
                        .WhenAnyValue(x => x.Satisfied)
                        .Subscribe(_ => Satisfied = Children.Any(x => x.Satisfied)));
                }

                break;

            case AnyTwoChildrenGameRequirement { Children: ImmutableArray<GameRequirement> children }:
                HeaderContent = "Any 2:";
                Children = ImmutableArray.CreateRange(children, child => new GameRequirementToolTipViewModel(child));
                foreach (GameRequirementToolTipViewModel child in Children)
                {
                    _subscription.Add(child
                        .WhenAnyValue(x => x.Satisfied)
                        .Subscribe(_ => Satisfied = Children.Count(x => x.Satisfied) >= 2));
                }

                break;

            default:
                throw new InvalidOperationException("Update this whenever you add new requirement types");
        }
    }

    public GameRequirement Model { get; }

    [Reactive]
    public bool Satisfied { get; set; }

    public object? HeaderContent { get; }

    public object? MyContent { get; }

    public ImmutableArray<GameRequirementToolTipViewModel> Children { get; } = [];

    public IEnumerable<GameRequirementToolTipViewModel> DescendantsAndSelf()
    {
        yield return this;

        foreach (GameRequirementToolTipViewModel child in Children)
        {
            foreach (GameRequirementToolTipViewModel dsc in child.DescendantsAndSelf())
            {
                yield return dsc;
            }
        }
    }

    public void Dispose()
    {
        _subscription.Dispose();
    }
}