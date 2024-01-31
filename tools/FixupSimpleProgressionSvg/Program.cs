using System.Text.RegularExpressions;
using System.Xml.Linq;

await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

XNamespace svg = "http://www.w3.org/2000/svg";
XNamespace xlink = "http://www.w3.org/1999/xlink";

FileStreamOptions openFileAsync = new()
{
    Mode = FileMode.Open,
    Access = FileAccess.Read,
    Share = FileShare.ReadWrite | FileShare.Delete,
    Options = FileOptions.SequentialScan | FileOptions.Asynchronous,
};

FileStreamOptions saveFileAsync = new()
{
    Mode = FileMode.Create,
    Access = FileAccess.Write,
    Share = FileShare.ReadWrite | FileShare.Delete,
    Options = FileOptions.Asynchronous,
};

XDocument doc;
await using (FileStream fs = new(args[1], openFileAsync))
{
    doc = await XDocument.LoadAsync(fs, LoadOptions.None, default);
}

doc.DocumentType?.Remove();
doc.Root!.Attribute("width")!.Value = "100%";
doc.Root.Attribute("height")?.Remove();

doc.Root.Attribute("viewBox")!.Value = Regex.Replace(
    doc.Root.Attribute("viewBox")!.Value,
    @"(?<minX>-?\d+(?:\.\d+)?) (?<minY>-?\d+(?:\.\d+)?) (?<width>-?\d+(?:\.\d+)?) (?<height>-?\d+(?:\.\d+)?)",
    m =>
    {
        decimal width = decimal.Parse(m.Groups["width"].ValueSpan);
        decimal height = decimal.Parse(m.Groups["height"].ValueSpan);
        return $"{m.Groups["minX"].Value} {m.Groups["minY"].Value} {width + 30} {height + 30}";
    },
    RegexOptions.CultureInvariant | RegexOptions.Compiled);

if (doc.Root?.Element(svg.GetName("g")) is XElement layoutRoot)
{
    if (layoutRoot.Attribute("transform") is not XAttribute { Value: "scale(1 1) rotate(0) translate(4 160)" } transformAttribute)
    {
        throw new InvalidDataException("Hardcoded parameters changed:" + layoutRoot.Attribute("transform"));
    }

    transformAttribute.Remove();
    layoutRoot.Element(svg.GetName("polygon"))?.Remove();
    layoutRoot.Attribute("class")?.Remove();
}

foreach (XElement path in doc.Descendants(svg.GetName("path")).Cast<XElement>())
{
    path.Add(new XAttribute("class", "arrow-line"));

    XElement parent = path.Parent!;
    string title = parent.Element(svg.GetName("title"))!.Value;
    parent.Attribute("class")!.Value = "traversed-path";
    parent.Attribute("class")!.Value += $" before-{Regex.Replace(title, @"^.+\>(?<dst>.*)$", m => m.Groups["dst"].Value.Replace('_', '-'), RegexOptions.Compiled)}";
    parent.Attribute("class")!.Value += $" after-{Regex.Replace(title, @"^(?<src>.+)\-\>.*$", m => m.Groups["src"].Value.Replace('_', '-'), RegexOptions.Compiled)}";
    switch (title)
    {
        case "start->basketball":
            parent.Attribute("class")!.Value += " current-region";
            break;
    }

    if (path.Attribute("d") is not XAttribute d)
    {
        throw new InvalidDataException("Hardcoded parameters changed.");
    }

    d.Value = Regex.Replace(d.Value, @"(?<x>-?\d+(?:\.\d+)?),(?<y>-?\d+(?:\.\d+)?)", m => $"{decimal.Parse(m.Groups["x"].ValueSpan) + 19},{decimal.Parse(m.Groups["y"].ValueSpan) + 175}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
}

foreach (XElement polygon in doc.Descendants(svg.GetName("polygon")).Cast<XElement>())
{
    polygon.Add(new XAttribute("class", "arrow-head"));
    if (polygon.Attribute("points") is not XAttribute points)
    {
        throw new InvalidDataException("Hardcoded parameters changed.");
    }

    points.Value = Regex.Replace(points.Value, @"(?<x>-?\d+(?:\.\d+)?),(?<y>-?\d+(?:\.\d+)?)", m => $"{decimal.Parse(m.Groups["x"].ValueSpan) + 19},{decimal.Parse(m.Groups["y"].ValueSpan) + 175}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
}

foreach (XElement img in doc.Descendants(svg.GetName("image")).Cast<XElement>())
{
    XElement parent = img.Parent!;
    string title = parent.Element(svg.GetName("title"))!.Value;
    parent.Attribute("class")!.Value = $"checked-location not-checked not-open checked-{title.Replace('_', '-')}";

    img.Attribute("x")!.Value = $"{decimal.Parse(img.Attribute("x")!.Value) + 19}";
    img.Attribute("y")!.Value = $"{decimal.Parse(img.Attribute("y")!.Value) + 175}";
    img.Attribute("height")!.Value = "54px";
    img.Attribute("preserveAspectRatio")!.Value = "xMinYMin";

    if (img.Attribute(xlink.GetName("href")) is not XAttribute href)
    {
        throw new InvalidDataException("Hardcoded parameters changed.");
    }

    href.Value = Path.ChangeExtension(href.Value, "webp");
    switch (args[0])
    {
        case "1":
            href.Value = $$$"""
            {{ url_for('static', filename='static/icons/autopelago/{{{href.Value}}}') }}
            """;
            break;

        case "2":
            href.Value = $"../assets/images/{href.Value}";
            break;
    }
}

doc.DescendantNodes().OfType<XComment>().Remove();
doc.Descendants().SelectMany(e => e.Attributes().Where(a => a.Name == "id")).Remove();
doc.Descendants(svg.GetName("title")).Remove();

await using (FileStream fs = new(args[2], saveFileAsync))
{
    await doc.SaveAsync(fs, SaveOptions.None, default);
}
