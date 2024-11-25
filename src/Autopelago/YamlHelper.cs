using System.Diagnostics.CodeAnalysis;

using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace Autopelago;

public static class YamlHelper
{
    private static readonly IDeserializer s_deserializer = new DeserializerBuilder().Build();

    public static bool TryGetValue<T>(this YamlMappingNode node, string key, [NotNullWhen(true)] out T? value)
    {
        if (!node.Children.TryGetValue(key, out YamlNode? valueNode))
        {
            value = default;
            return false;
        }

        value = valueNode.To<T>()!;
        return true;
    }

    public static T To<T>(this YamlNode node)
    {
        // based on https://stackoverflow.com/a/40727087/1083771
        using IEnumerator<ParsingEvent> enumerator = ConvertToEventStream(node).GetEnumerator();
        return s_deserializer.Deserialize<T>(new EventStreamParserAdapter(enumerator));
    }

    private static IEnumerable<ParsingEvent> ConvertToEventStream(YamlNode node)
    {
        return node switch
        {
            YamlScalarNode scalar => ConvertToEventStream(scalar),
            YamlSequenceNode sequence => ConvertToEventStream(sequence),
            YamlMappingNode mapping => ConvertToEventStream(mapping),
            _ => throw new NotSupportedException($"Unsupported node type: {node.GetType().Name}"),
        };
    }

    private static IEnumerable<ParsingEvent> ConvertToEventStream(YamlScalarNode scalar)
    {
        return [new Scalar(scalar.Anchor, scalar.Tag, scalar.Value!, scalar.Style, false, false)];
    }

    private static IEnumerable<ParsingEvent> ConvertToEventStream(YamlSequenceNode sequence)
    {
        yield return new SequenceStart(sequence.Anchor, sequence.Tag, false, sequence.Style);

        foreach (YamlNode node in sequence.Children)
        {
            foreach (ParsingEvent evt in ConvertToEventStream(node))
            {
                yield return evt;
            }
        }

        yield return new SequenceEnd();
    }

    private static IEnumerable<ParsingEvent> ConvertToEventStream(YamlMappingNode mapping)
    {
        yield return new MappingStart(mapping.Anchor, mapping.Tag, false, mapping.Style);

        foreach ((YamlNode key, YamlNode value) in mapping.Children)
        {
            foreach (ParsingEvent evt in ConvertToEventStream(key))
            {
                yield return evt;
            }
            foreach (ParsingEvent evt in ConvertToEventStream(value))
            {
                yield return evt;
            }
        }

        yield return new MappingEnd();
    }

    private sealed class EventStreamParserAdapter : IParser
    {
        private readonly IEnumerator<ParsingEvent> _enumerator;

        public EventStreamParserAdapter(IEnumerator<ParsingEvent> enumerator)
        {
            _enumerator = enumerator;
        }

        public ParsingEvent? Current { get; private set; }

        public bool MoveNext()
        {
            if (!_enumerator.MoveNext())
            {
                return false;
            }

            Current = _enumerator.Current;
            return true;
        }
    }
}
