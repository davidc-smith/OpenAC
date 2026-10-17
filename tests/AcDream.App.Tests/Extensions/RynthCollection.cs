namespace AcDream.App.Tests.Extensions;

/// <summary>
/// ImGui state and <c>OverlayInputGate</c> are process-wide, so the tests that
/// touch them never run alongside any other collection.
/// </summary>
[CollectionDefinition("Rynth", DisableParallelization = true)]
public sealed class RynthCollection
{
}
