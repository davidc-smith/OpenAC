using System.Numerics;
using System.Xml.Linq;
using AcDream.UI.Abstractions.Input;

namespace AcDream.App.UI.Markup;

/// <summary>
/// Binds a colour attribute the way <c>MarkupDocument.AddElement</c> does,
/// including <c>theme:</c> tokens, against the element's binding object.
/// </summary>
internal delegate void MarkupColorBinder(
    string? expression, Action<Vector4> setValue, Action<Func<Vector4>> setSource);

/// <summary>
/// What an element builder outside <see cref="MarkupDocument"/> gets from the
/// document build: the same resolvers and font every built-in element uses,
/// the window's remembered control state (null when nothing is remembered),
/// and <see cref="AddChild"/>, which builds one child element into a
/// container exactly as the document builds any element. <see cref="DescribeKey"/>
/// is the host's key names for hotkeys (null shows the canonical string).
/// </summary>
internal sealed record MarkupBuildContext(
    Func<uint, (uint, int, int)> Resolve,
    UiDatFont? DatFont,
    IMarkupIconResolver? Icons,
    MarkupColorBinder BindColor,
    IMarkupControlState? ControlState = null,
    Action<UiElement, XElement>? AddChild = null,
    Func<KeyChord, string>? DescribeKey = null);
