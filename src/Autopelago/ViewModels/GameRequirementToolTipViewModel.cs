using System.Collections.Immutable;
using System.Reactive.Disposables;

using Autopelago.Converters;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Autopelago.ViewModels;

public sealed partial class GameRequirementToolTipViewModel : ViewModelBase, IDisposable
{
    private readonly CompositeDisposable _subscription = [];

    [ObservableAsProperty] private bool _lactoseIntolerant;

    [Reactive] private bool _satisfied;

    public GameRequirementToolTipViewModel(GameRequirement req, IObservable<bool> lactoseIntolerant)
    {
        Model = req;
        _lactoseIntolerantHelper = lactoseIntolerant
            .ToProperty(this, x => x.LactoseIntolerant);
        switch (req)
        {
            case RatCountRequirement { RatCount: 1 }:
                MyContent = "1 rat";
                break;

            case RatCountRequirement { RatCount: int ratCount }:
                MyContent = $"{ratCount} rats";
                break;

            case ReceivedItemRequirement { ItemKey: ItemKey itemKey }:
                var item = GameDefinitions.Instance[itemKey];
                MyContent = new ContentControl
                {
                    [!ContentControl.ContentProperty] = new MultiBinding
                    {
                        Bindings =
                        [
                            new Binding { Source = item },
                            lactoseIntolerant.ToBinding(),
                        ],
                        Converter = new ItemNameConverter(),
                    },
                };
                break;

            case AllChildrenGameRequirement { Children: ImmutableArray<GameRequirement> children }:
                HeaderContent = "All:";
                Children = ImmutableArray.CreateRange(children, child => new GameRequirementToolTipViewModel(child, lactoseIntolerant));
                foreach (GameRequirementToolTipViewModel child in Children)
                {
                    _subscription.Add(child
                        .WhenAnyValue(x => x.Satisfied)
                        .Subscribe(_ => Satisfied = Children.All(x => x.Satisfied)));
                }

                break;

            case AnyChildGameRequirement { Children: ImmutableArray<GameRequirement> children }:
                HeaderContent = "Any:";
                Children = ImmutableArray.CreateRange(children, child => new GameRequirementToolTipViewModel(child, lactoseIntolerant));
                foreach (GameRequirementToolTipViewModel child in Children)
                {
                    _subscription.Add(child
                        .WhenAnyValue(x => x.Satisfied)
                        .Subscribe(_ => Satisfied = Children.Any(x => x.Satisfied)));
                }

                break;

            case AnyTwoChildrenGameRequirement { Children: ImmutableArray<GameRequirement> children }:
                HeaderContent = "Any 2:";
                Children = ImmutableArray.CreateRange(children, child => new GameRequirementToolTipViewModel(child, lactoseIntolerant));
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

    public void Dispose()
    {
        _subscription.Dispose();
    }

    public GameRequirement Model { get; }

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
}
