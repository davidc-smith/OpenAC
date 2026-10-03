using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Xml;

namespace AcDream.Core.Plugins;

public enum SvgPaintKind { Fill, Stroke }

public enum SvgLineCap { Butt, Round, Square }

public enum SvgLineJoin { Miter, Round, Bevel }

public sealed record SvgStrokeStyle(double Width, SvgLineCap Cap, SvgLineJoin Join, double MiterLimit);

/// <summary>One thing to paint, in document order: an element's fill or its stroke, in the
/// element's own user units, with the transform from those units to the icon's viewBox.</summary>
public sealed record SvgPaintLayer(
    SvgPaintKind Kind,
    double Opacity,
    IReadOnlyList<SvgSubpath> Subpaths,
    SvgStrokeStyle? Stroke,
    SvgMatrix Transform);

/// <summary>A parsed icon: its viewBox and what to paint. Coverage only; it has no colour.</summary>
public sealed record SvgIconDocument(
    double MinX, double MinY, double Width, double Height, IReadOnlyList<SvgPaintLayer> Layers);

/// <summary>
/// Reads a plugin's SVG icon: a small, safe subset of SVG 1.1 (see the plugin UI
/// markup docs). A file is accepted only if everything in it that could change
/// what is drawn is understood, so an icon is never drawn wrong. Never throws:
/// every rejection is a reason string, and the caller falls back to the next
/// icon.
/// </summary>
public static class PluginSvgIcon
{
    public const string FileName = "icon.svg";
    public const int MaximumBytes = 16 * 1024;
    public const int MaximumElements = 256;
    public const int MaximumDepth = 8;
    public const int MaximumPathCommands = 4096;

    private const string SvgNamespace = "http://www.w3.org/2000/svg";

    private static readonly HashSet<string> Drawn =
        ["path", "circle", "ellipse", "rect", "line", "polyline", "polygon"];

    private static readonly HashSet<string> Skipped = ["title", "desc", "metadata"];

    /// <summary>Attributes that never change what is drawn.</summary>
    private static readonly HashSet<string> Ignored =
    [
        "id", "class", "version", "vector-effect", "color", "shape-rendering", "overflow",
        "enable-background", "color-interpolation", "color-interpolation-filters", "baseProfile",
    ];

    /// <summary>Loads and parses the file at <paramref name="path"/>.</summary>
    public static bool TryLoad(
        string path, [NotNullWhen(true)] out SvgIconDocument? document, [NotNullWhen(false)] out string? reason)
    {
        document = null;
        try
        {
            using FileStream stream = File.OpenRead(path);
            var buffer = new byte[MaximumBytes + 1];
            int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            if (read > MaximumBytes)
            {
                reason = $"is larger than {MaximumBytes / 1024} KiB";
                return false;
            }
            return TryParse(buffer.AsSpan(0, read).ToArray(), out document, out reason);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException)
        {
            reason = "could not be read: " + ex.Message;
            return false;
        }
    }

    /// <summary>Parses an icon from its bytes.</summary>
    public static bool TryParse(
        byte[] bytes, [NotNullWhen(true)] out SvgIconDocument? document, [NotNullWhen(false)] out string? reason)
    {
        document = null;
        if (bytes.Length > MaximumBytes)
        {
            reason = $"is larger than {MaximumBytes / 1024} KiB";
            return false;
        }

        try
        {
            var parser = new Parser();
            document = parser.Parse(bytes);
            reason = parser.Reason;
            return document is not null;
        }
        catch (XmlException ex)
        {
            reason = ex.Message.Contains("DTD", StringComparison.Ordinal)
                ? "uses a DTD, which plugin icons do not support"
                : "is not well-formed XML: " + ex.Message;
            return false;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException
                                       or OverflowException or IndexOutOfRangeException)
        {
            document = null;
            reason = "could not be parsed: " + ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Resolves a descriptor's <c>IconFile</c> against the plugin folder. The path must be
    /// relative, end in <c>.svg</c>, and stay inside the folder after links are followed.
    /// </summary>
    public static bool TryResolvePath(
        string pluginDirectory, string iconFile,
        [NotNullWhen(true)] out string? fullPath, [NotNullWhen(false)] out string? reason)
    {
        fullPath = null;
        string relative = iconFile.Replace('\\', '/');
        if (relative.Length == 0 || relative.StartsWith('/') || Path.IsPathRooted(relative)
            || (relative.Length >= 2 && relative[1] == ':'))
        {
            reason = "points outside the plugin folder";
            return false;
        }
        if (!relative.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            reason = "is not an .svg file";
            return false;
        }

        try
        {
            string root = Path.GetFullPath(pluginDirectory);
            string candidate = Path.GetFullPath(Path.Combine(root, relative));
            if (!IsInside(root, candidate))
            {
                reason = "points outside the plugin folder";
                return false;
            }
            if (!File.Exists(candidate))
            {
                reason = "does not exist";
                return false;
            }

            string resolvedRoot = ResolveLinks(root);
            string resolved = ResolveLinks(candidate);
            if (!IsInside(resolvedRoot, resolved))
            {
                reason = "points outside the plugin folder";
                return false;
            }
            fullPath = resolved;
            reason = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException)
        {
            reason = "could not be resolved: " + ex.Message;
            return false;
        }
    }

    private static bool IsInside(string root, string path)
    {
        string prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return path.StartsWith(prefix, comparison);
    }

    /// <summary>
    /// Follows every link along <paramref name="path"/>, directories included, one component at a
    /// time. A link's target is itself walked component by component, so a link that points through
    /// another link cannot hide where the real file is.
    /// </summary>
    private static string ResolveLinks(string path)
    {
        const int MaximumLinkHops = 40;
        string full = Path.GetFullPath(path);
        string current = Path.GetPathRoot(full) ?? "";
        var pending = new LinkedList<string>(SplitPath(full[current.Length..]));
        int hops = 0;
        while (pending.First is { } node)
        {
            pending.RemoveFirst();
            string part = node.Value;
            if (part == ".") continue;
            if (part == "..")
            {
                current = Path.GetDirectoryName(current) ?? current;
                continue;
            }

            string next = Path.Combine(current, part);
            FileSystemInfo info = Directory.Exists(next) ? new DirectoryInfo(next) : new FileInfo(next);
            string? target = info.LinkTarget;
            if (target is null)
            {
                current = next;
                continue;
            }
            if (++hops > MaximumLinkHops) throw new IOException("too many levels of links");
            if (Path.IsPathRooted(target))
            {
                current = Path.GetPathRoot(target) ?? current;
                target = target[current.Length..];
            }
            string[] targetParts = SplitPath(target);
            for (int i = targetParts.Length - 1; i >= 0; i--) pending.AddFirst(targetParts[i]);
        }
        return current;
    }

    private static string[] SplitPath(string path) =>
        path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

    private readonly record struct Style(
        bool Fill, bool Stroke, double FillOpacity, double StrokeOpacity, double GroupOpacity,
        double StrokeWidth, SvgLineCap Cap, SvgLineJoin Join, double MiterLimit);

    private sealed class Parser
    {
        private int _elements;
        private int _commands;
        private readonly List<SvgPaintLayer> _layers = [];

        public string? Reason { get; private set; }

        private SvgIconDocument? Fail(string reason)
        {
            Reason = reason;
            return null;
        }

        public SvgIconDocument? Parse(byte[] bytes)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                // Note: 0 means "no limit" in .NET. DtdProcessing.Prohibit is the real guard:
                // with DTDs refused there are no entities to expand.
                MaxCharactersFromEntities = 0,
                MaxCharactersInDocument = MaximumBytes,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true,
            };
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = XmlReader.Create(stream, settings);
            if (reader.MoveToContent() != XmlNodeType.Element || reader.LocalName != "svg" || !IsSvgNamespace(reader))
                return Fail("is not an SVG document");

            var style = new Style(true, false, 1, 1, 1, 1, SvgLineCap.Butt, SvgLineJoin.Miter, 4);
            Dictionary<string, string> attributes = ReadAttributes(reader);
            if (Reason is not null) return null;
            if (!TryViewBox(attributes, out double minX, out double minY, out double width, out double height))
                return Fail(Reason ?? "has no usable viewBox, width or height");
            attributes.Remove("viewBox");
            attributes.Remove("width");
            attributes.Remove("height");
            attributes.Remove("preserveAspectRatio");
            attributes.Remove("x");
            attributes.Remove("y");
            if (!TryStyle(attributes, ref style, out SvgMatrix transform, out double opacity)) return null;
            if (!NoneLeft(attributes, "svg")) return null;
            style = style with { GroupOpacity = style.GroupOpacity * opacity };

            _elements = 1;
            if (!reader.IsEmptyElement && !ReadChildren(reader, style, transform, depth: 1)) return null;
            if (_layers.Count == 0) return Fail("draws nothing");
            return new SvgIconDocument(minX, minY, width, height, _layers);
        }

        private static bool IsSvgNamespace(XmlReader reader) =>
            reader.NamespaceURI.Length == 0 || reader.NamespaceURI == SvgNamespace;

        private bool ReadChildren(XmlReader reader, Style inherited, SvgMatrix ctm, int depth)
        {
            int parentDepth = reader.Depth;
            reader.Read();
            while (!reader.EOF && reader.Depth > parentDepth)
            {
                if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA)
                {
                    reader.Read();
                    continue;
                }
                if (reader.NodeType != XmlNodeType.Element)
                {
                    reader.Read();
                    continue;
                }
                if (!ReadElement(reader, inherited, ctm, depth)) return false;
            }
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == parentDepth) reader.Read();
            return true;
        }

        private bool ReadElement(XmlReader reader, Style inherited, SvgMatrix ctm, int depth)
        {
            string name = reader.LocalName;
            if (!IsSvgNamespace(reader))
            {
                Reason = $"uses <{reader.Name}>, which plugin icons do not support";
                return false;
            }
            if (++_elements > MaximumElements)
            {
                Reason = $"has more than {MaximumElements} elements";
                return false;
            }
            if (depth >= MaximumDepth)
            {
                Reason = $"nests elements more than {MaximumDepth} deep";
                return false;
            }

            if (Skipped.Contains(name)) return SkipCounting(reader);
            if (name == "defs")
            {
                if (!reader.IsEmptyElement)
                {
                    using XmlReader inner = reader.ReadSubtree();
                    inner.Read();
                    while (inner.Read())
                    {
                        if (inner.NodeType == XmlNodeType.Element)
                        {
                            Reason = "uses <defs> with content, which plugin icons do not support";
                            return false;
                        }
                    }
                }
                reader.Skip();
                return true;
            }
            if (name != "g" && !Drawn.Contains(name))
            {
                Reason = $"uses <{name}>, which plugin icons do not support";
                return false;
            }

            bool empty = reader.IsEmptyElement;
            Dictionary<string, string> attributes = ReadAttributes(reader);
            if (Reason is not null) return false;
            Style style = inherited;
            if (!TryStyle(attributes, ref style, out SvgMatrix own, out double opacity)) return false;
            SvgMatrix transform = ctm.Then(own);
            if (!IsUsable(transform))
            {
                Reason = "has a transform that is not finite or collapses the shape";
                return false;
            }
            style = style with { GroupOpacity = style.GroupOpacity * opacity };

            if (name == "g")
            {
                if (!NoneLeft(attributes, "g")) return false;
                if (empty)
                {
                    reader.Read();
                    return true;
                }
                return ReadChildren(reader, style, transform, depth + 1);
            }

            var subpaths = new List<SvgSubpath>();
            if (!TryGeometry(name, attributes, subpaths)) return false;
            if (!NoneLeft(attributes, name)) return false;
            if (!AllFinite(subpaths))
            {
                Reason = $"has a coordinate that is not finite on <{name}>";
                return false;
            }
            if (subpaths.Count > 0)
            {
                double fillOpacity = style.FillOpacity * style.GroupOpacity;
                double strokeOpacity = style.StrokeOpacity * style.GroupOpacity;
                if (style.Fill && fillOpacity > 0)
                    _layers.Add(new SvgPaintLayer(SvgPaintKind.Fill, fillOpacity, subpaths, null, transform));
                if (style.Stroke && strokeOpacity > 0 && style.StrokeWidth > 0)
                {
                    _layers.Add(new SvgPaintLayer(SvgPaintKind.Stroke, strokeOpacity, subpaths,
                        new SvgStrokeStyle(style.StrokeWidth, style.Cap, style.Join, style.MiterLimit), transform));
                }
            }

            if (empty)
            {
                reader.Read();
                return true;
            }
            // A drawn element may hold only title/desc text; anything else is refused.
            int parentDepth = reader.Depth;
            reader.Read();
            while (!reader.EOF && reader.Depth > parentDepth)
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    if (!Skipped.Contains(reader.LocalName))
                    {
                        Reason = $"puts <{reader.LocalName}> inside <{name}>, which plugin icons do not support";
                        return false;
                    }
                    if (!SkipCounting(reader)) return false;
                    continue;
                }
                reader.Read();
            }
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == parentDepth) reader.Read();
            return true;
        }

        /// <summary>Skips an element and everything in it, but counts its descendants toward the element limit.</summary>
        private bool SkipCounting(XmlReader reader)
        {
            if (!reader.IsEmptyElement)
            {
                using XmlReader inner = reader.ReadSubtree();
                inner.Read();
                while (inner.Read())
                {
                    if (inner.NodeType == XmlNodeType.Element && ++_elements > MaximumElements)
                    {
                        Reason = $"has more than {MaximumElements} elements";
                        return false;
                    }
                }
            }
            reader.Skip();
            return true;
        }

        private bool NoneLeft(Dictionary<string, string> attributes, string element)
        {
            if (attributes.Count == 0) return true;
            Reason = $"uses the attribute '{attributes.Keys.First()}' on <{element}>, which plugin icons do not support";
            return false;
        }

        private Dictionary<string, string> ReadAttributes(XmlReader reader)
        {
            var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
            if (reader.MoveToFirstAttribute())
            {
                do
                {
                    string name = reader.Name;
                    if (name == "xmlns" || name.StartsWith("xmlns:", StringComparison.Ordinal)
                        || name.StartsWith("xml:", StringComparison.Ordinal)
                        || name.StartsWith("aria-", StringComparison.Ordinal)
                        || name.StartsWith("data-", StringComparison.Ordinal)
                        || name.StartsWith("sodipodi:", StringComparison.Ordinal)
                        || name.StartsWith("inkscape:", StringComparison.Ordinal)
                        || Ignored.Contains(name))
                        continue;
                    if (name.Contains(':'))
                    {
                        Reason = $"uses the attribute '{name}', which plugin icons do not support";
                        break;
                    }
                    attributes[name] = reader.Value;
                }
                while (reader.MoveToNextAttribute());
                reader.MoveToElement();
            }
            return attributes;
        }

        private bool TryViewBox(
            Dictionary<string, string> attributes, out double minX, out double minY, out double width, out double height)
        {
            minX = minY = width = height = 0;
            if (attributes.TryGetValue("viewBox", out string? viewBox))
            {
                if (!SvgOutline.TryParseNumbers(viewBox, out List<double> n) || n.Count != 4 || !(n[2] > 0) || !(n[3] > 0))
                {
                    Reason = "has a malformed viewBox";
                    return false;
                }
                (minX, minY, width, height) = (n[0], n[1], n[2], n[3]);
                return true;
            }
            if (attributes.TryGetValue("width", out string? w) && attributes.TryGetValue("height", out string? h)
                && TryLength(w, out width) && TryLength(h, out height) && width > 0 && height > 0)
                return true;
            return false;
        }

        private bool TryStyle(Dictionary<string, string> attributes, ref Style style, out SvgMatrix transform, out double opacity)
        {
            transform = SvgMatrix.Identity;
            opacity = 1;
            var properties = new List<(string Name, string Value)>();
            foreach (string name in PresentationNames)
            {
                if (attributes.Remove(name, out string? value)) properties.Add((name, value));
            }
            if (attributes.Remove("style", out string? css))
            {
                foreach (string declaration in css.Split(';'))
                {
                    if (string.IsNullOrWhiteSpace(declaration)) continue;
                    int colon = declaration.IndexOf(':');
                    if (colon <= 0)
                    {
                        Reason = "has a malformed style attribute";
                        return false;
                    }
                    string name = declaration[..colon].Trim();
                    string value = declaration[(colon + 1)..].Trim();
                    if (Ignored.Contains(name)) continue;
                    if (!PresentationNames.Contains(name) || name == "transform")
                    {
                        Reason = $"uses the style property '{name}', which plugin icons do not support";
                        return false;
                    }
                    properties.Add((name, value));
                }
            }

            foreach ((string name, string raw) in properties)
            {
                string value = raw.Trim();
                string keyword = value.ToLowerInvariant();
                if (keyword == "inherit") continue;
                switch (name)
                {
                    case "fill":
                    case "stroke":
                        if (!TryPaint(value, out bool ink)) return false;
                        style = name == "fill" ? style with { Fill = ink } : style with { Stroke = ink };
                        break;
                    case "fill-opacity":
                    case "stroke-opacity":
                    case "opacity":
                        if (!TryOpacity(value, out double o)) return Bad(name);
                        if (name == "fill-opacity") style = style with { FillOpacity = o };
                        else if (name == "stroke-opacity") style = style with { StrokeOpacity = o };
                        else opacity = o;
                        break;
                    case "stroke-width":
                        if (!TryLength(value, out double width) || width < 0) return Bad(name);
                        style = style with { StrokeWidth = width };
                        break;
                    case "stroke-linecap":
                        SvgLineCap? cap = keyword switch
                        {
                            "butt" => SvgLineCap.Butt, "round" => SvgLineCap.Round, "square" => SvgLineCap.Square, _ => null,
                        };
                        if (cap is null) return Bad(name);
                        style = style with { Cap = cap.Value };
                        break;
                    case "stroke-linejoin":
                        SvgLineJoin? join = keyword switch
                        {
                            "miter" => SvgLineJoin.Miter, "round" => SvgLineJoin.Round, "bevel" => SvgLineJoin.Bevel, _ => null,
                        };
                        if (join is null) return Bad(name);
                        style = style with { Join = join.Value };
                        break;
                    case "stroke-miterlimit":
                        if (!TryNumber(value, out double limit) || limit < 1) return Bad(name);
                        style = style with { MiterLimit = limit };
                        break;
                    case "fill-rule":
                    case "clip-rule":
                        if (keyword != "nonzero")
                        {
                            Reason = $"uses {name}=\"{value}\"; plugin icons support only nonzero";
                            return false;
                        }
                        break;
                    case "stroke-dasharray":
                        if (keyword != "none")
                        {
                            Reason = "uses stroke-dasharray, which plugin icons do not support";
                            return false;
                        }
                        break;
                    case "stroke-dashoffset":
                        break;
                    case "display":
                        if (keyword is not ("inline" or "block"))
                        {
                            Reason = $"uses display=\"{value}\", which plugin icons do not support";
                            return false;
                        }
                        break;
                    case "visibility":
                        if (keyword != "visible")
                        {
                            Reason = $"uses visibility=\"{value}\", which plugin icons do not support";
                            return false;
                        }
                        break;
                    case "transform":
                        if (!TryTransform(value, out transform))
                        {
                            Reason ??= "has a malformed transform";
                            return false;
                        }
                        break;
                }
            }
            return true;
        }

        private bool Bad(string name)
        {
            Reason = $"has a malformed {name}";
            return false;
        }

        private static readonly HashSet<string> PresentationNames =
        [
            "fill", "stroke", "fill-opacity", "stroke-opacity", "opacity", "stroke-width",
            "stroke-linecap", "stroke-linejoin", "stroke-miterlimit", "fill-rule", "clip-rule",
            "stroke-dasharray", "stroke-dashoffset", "display", "visibility", "transform",
        ];

        private bool TryPaint(string value, out bool ink)
        {
            ink = false;
            if (value.Equals("none", StringComparison.OrdinalIgnoreCase) || value.Equals("transparent", StringComparison.OrdinalIgnoreCase)) return true;
            if (value.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
            {
                Reason = "uses a paint server (url(...)), which plugin icons do not support";
                return false;
            }
            if (value.Equals("currentColor", StringComparison.OrdinalIgnoreCase) || NamedColours.Contains(value) || IsHexColour(value) || IsFunctionColour(value))
            {
                ink = true;
                return true;
            }
            Reason = $"has an unknown paint '{value}'";
            return false;
        }

        private static bool IsHexColour(string value) =>
            value.Length is 4 or 5 or 7 or 9 && value[0] == '#' && value.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;

        private static bool IsFunctionColour(string value)
        {
            foreach (string function in (ReadOnlySpan<string>)["rgb(", "rgba(", "hsl(", "hsla("])
            {
                if (value.StartsWith(function, StringComparison.OrdinalIgnoreCase) && value.EndsWith(')')
                    && value.IndexOf(')') == value.Length - 1)
                    return true;
            }
            return false;
        }

        private static readonly HashSet<string> NamedColours = new(StringComparer.OrdinalIgnoreCase)
        {
            "aliceblue", "antiquewhite", "aqua", "aquamarine", "azure", "beige", "bisque", "black", "blanchedalmond",
            "blue", "blueviolet", "brown", "burlywood", "cadetblue", "chartreuse", "chocolate", "coral",
            "cornflowerblue", "cornsilk", "crimson", "cyan", "darkblue", "darkcyan", "darkgoldenrod", "darkgray",
            "darkgreen", "darkgrey", "darkkhaki", "darkmagenta", "darkolivegreen", "darkorange", "darkorchid",
            "darkred", "darksalmon", "darkseagreen", "darkslateblue", "darkslategray", "darkslategrey",
            "darkturquoise", "darkviolet", "deeppink", "deepskyblue", "dimgray", "dimgrey", "dodgerblue",
            "firebrick", "floralwhite", "forestgreen", "fuchsia", "gainsboro", "ghostwhite", "gold", "goldenrod",
            "gray", "green", "greenyellow", "grey", "honeydew", "hotpink", "indianred", "indigo", "ivory", "khaki",
            "lavender", "lavenderblush", "lawngreen", "lemonchiffon", "lightblue", "lightcoral", "lightcyan",
            "lightgoldenrodyellow", "lightgray", "lightgreen", "lightgrey", "lightpink", "lightsalmon",
            "lightseagreen", "lightskyblue", "lightslategray", "lightslategrey", "lightsteelblue", "lightyellow",
            "lime", "limegreen", "linen", "magenta", "maroon", "mediumaquamarine", "mediumblue", "mediumorchid",
            "mediumpurple", "mediumseagreen", "mediumslateblue", "mediumspringgreen", "mediumturquoise",
            "mediumvioletred", "midnightblue", "mintcream", "mistyrose", "moccasin", "navajowhite", "navy",
            "oldlace", "olive", "olivedrab", "orange", "orangered", "orchid", "palegoldenrod", "palegreen",
            "paleturquoise", "palevioletred", "papayawhip", "peachpuff", "peru", "pink", "plum", "powderblue",
            "purple", "rebeccapurple", "red", "rosybrown", "royalblue", "saddlebrown", "salmon", "sandybrown",
            "seagreen", "seashell", "sienna", "silver", "skyblue", "slateblue", "slategray", "slategrey", "snow",
            "springgreen", "steelblue", "tan", "teal", "thistle", "tomato", "turquoise", "violet", "wheat",
            "white", "whitesmoke", "yellow", "yellowgreen",
        };

        private static bool TryOpacity(string value, out double opacity)
        {
            bool percent = value.EndsWith('%');
            bool ok = TryNumber(percent ? value[..^1] : value, out opacity);
            if (percent) opacity /= 100;
            opacity = Math.Clamp(opacity, 0, 1);
            return ok;
        }

        private static bool TryNumber(string value, out double number) =>
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number);

        private static bool TryLength(string value, out double number)
        {
            string trimmed = value.Trim();
            if (trimmed.EndsWith("px", StringComparison.Ordinal)) trimmed = trimmed[..^2];
            return TryNumber(trimmed, out number);
        }

        private bool TryTransform(string text, out SvgMatrix matrix)
        {
            matrix = SvgMatrix.Identity;
            int i = 0;
            while (true)
            {
                while (i < text.Length && (char.IsWhiteSpace(text[i]) || text[i] == ',')) i++;
                if (i >= text.Length) break;
                int nameStart = i;
                while (i < text.Length && char.IsAsciiLetter(text[i])) i++;
                string name = text[nameStart..i];
                while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                if (i >= text.Length || text[i] != '(') return false;
                int close = text.IndexOf(')', i);
                if (close < 0) return false;
                if (!SvgOutline.TryParseNumbers(text[(i + 1)..close], out List<double> n)) return false;
                i = close + 1;
                SvgMatrix? step = (name, n.Count) switch
                {
                    ("matrix", 6) => new SvgMatrix(n[0], n[1], n[2], n[3], n[4], n[5]),
                    ("translate", 1) => new SvgMatrix(1, 0, 0, 1, n[0], 0),
                    ("translate", 2) => new SvgMatrix(1, 0, 0, 1, n[0], n[1]),
                    ("scale", 1) => new SvgMatrix(n[0], 0, 0, n[0], 0, 0),
                    ("scale", 2) => new SvgMatrix(n[0], 0, 0, n[1], 0, 0),
                    ("rotate", 1) => Rotate(n[0], 0, 0),
                    ("rotate", 3) => Rotate(n[0], n[1], n[2]),
                    ("skewX", 1) => new SvgMatrix(1, 0, Math.Tan(n[0] * Math.PI / 180), 1, 0, 0),
                    ("skewY", 1) => new SvgMatrix(1, Math.Tan(n[0] * Math.PI / 180), 0, 1, 0, 0),
                    _ => null,
                };
                if (step is null) return false;
                matrix = matrix.Then(step.Value);
            }
            if (!IsUsable(matrix))
            {
                Reason = "has a transform that is not finite or collapses the shape";
                return false;
            }
            return true;
        }

        private static bool IsUsable(SvgMatrix m) =>
            double.IsFinite(m.A) && double.IsFinite(m.B) && double.IsFinite(m.C) && double.IsFinite(m.D)
            && double.IsFinite(m.E) && double.IsFinite(m.F)
            && double.IsFinite(m.Determinant) && Math.Abs(m.Determinant) >= 1e-12;

        private static bool AllFinite(List<SvgSubpath> subpaths)
        {
            static bool Ok(SvgPoint p) => double.IsFinite(p.X) && double.IsFinite(p.Y);
            foreach (SvgSubpath subpath in subpaths)
            {
                if (!Ok(subpath.Start)) return false;
                foreach (SvgSegment segment in subpath.Segments)
                {
                    if (!Ok(segment.End)) return false;
                    if (segment.Kind != SvgSegmentKind.Line && !Ok(segment.C1)) return false;
                    if (segment.Kind == SvgSegmentKind.Cubic && !Ok(segment.C2)) return false;
                }
            }
            return true;
        }

        private static SvgMatrix Rotate(double degrees, double cx, double cy)
        {
            double a = degrees * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
            return new SvgMatrix(1, 0, 0, 1, cx, cy)
                .Then(new SvgMatrix(cos, sin, -sin, cos, 0, 0))
                .Then(new SvgMatrix(1, 0, 0, 1, -cx, -cy));
        }

        private bool TryGeometry(string name, Dictionary<string, string> a, List<SvgSubpath> into)
        {
            double Number(string key, double fallback = 0)
            {
                if (!a.Remove(key, out string? text)) return fallback;
                if (TryLength(text, out double value)) return value;
                Reason ??= $"has a malformed {key} on <{name}>";
                return double.NaN;
            }

            switch (name)
            {
                case "path":
                    if (!a.Remove("d", out string? d)) return true;
                    if (!SvgOutline.TryParsePath(d, into, ref _commands, MaximumPathCommands, out string? why))
                    {
                        Reason = why;
                        return false;
                    }
                    return true;
                case "circle":
                {
                    double cx = Number("cx"), cy = Number("cy"), r = Number("r");
                    if (Reason is not null) return false;
                    if (r < 0) return Bad("r");
                    if (r > 0) into.Add(SvgOutline.Ellipse(cx, cy, r, r));
                    return true;
                }
                case "ellipse":
                {
                    double cx = Number("cx"), cy = Number("cy"), rx = Number("rx"), ry = Number("ry");
                    if (Reason is not null) return false;
                    if (rx < 0 || ry < 0) return Bad("rx");
                    if (rx > 0 && ry > 0) into.Add(SvgOutline.Ellipse(cx, cy, rx, ry));
                    return true;
                }
                case "rect":
                {
                    bool hasRx = a.ContainsKey("rx"), hasRy = a.ContainsKey("ry");
                    double x = Number("x"), y = Number("y"), w = Number("width"), h = Number("height");
                    double rx = Number("rx"), ry = Number("ry");
                    if (Reason is not null) return false;
                    if (w < 0 || h < 0 || rx < 0 || ry < 0) return Bad("rect size");
                    if (hasRx && !hasRy) ry = rx;
                    if (hasRy && !hasRx) rx = ry;
                    rx = Math.Min(rx, w / 2);
                    ry = Math.Min(ry, h / 2);
                    if (w > 0 && h > 0) into.Add(SvgOutline.Rect(x, y, w, h, rx, ry));
                    return true;
                }
                case "line":
                {
                    double x1 = Number("x1"), y1 = Number("y1"), x2 = Number("x2"), y2 = Number("y2");
                    if (Reason is not null) return false;
                    into.Add(SvgOutline.Poly([new(x1, y1), new(x2, y2)], closed: false));
                    return true;
                }
                default: // polyline, polygon
                {
                    if (!a.Remove("points", out string? text)) return true;
                    if (!SvgOutline.TryParsePoints(text, out List<SvgPoint> points)) return Bad("points");
                    into.Add(SvgOutline.Poly(points, closed: name == "polygon"));
                    return true;
                }
            }
        }
    }
}
