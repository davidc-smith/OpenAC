using System.Numerics;

namespace AcDream.App.UI.Markup;

/// <summary>
/// Binds a colour attribute the way <c>MarkupDocument.AddElement</c> does,
/// including <c>theme:</c> tokens, against the element's binding object.
/// </summary>
internal delegate void MarkupColorBinder(
    string? expression, Action<Vector4> setValue, Action<Func<Vector4>> setSource);

/// <summary>
/// What an element builder outside <see cref="MarkupDocument"/> gets from the
/// document build: the same resolvers and font every built-in element uses.
/// </summary>
internal sealed record MarkupBuildContext(
    Func<uint, (uint, int, int)> Resolve,
    UiDatFont? DatFont,
    IMarkupIconResolver? Icons,
    MarkupColorBinder BindColor);
