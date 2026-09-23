namespace MushaAltSpaceIme.Core;

/// <summary>One physical or replayable keyboard event.</summary>
public readonly record struct KeyEvent(
    ushort VirtualKey,
    ushort ScanCode,
    bool IsUp,
    bool IsExtended);

/// <summary>The hook action for one physical keyboard event.</summary>
public sealed record InputDecision(bool Suppress, IReadOnlyList<KeyEvent> Replay, bool Toggle)
{
    public static InputDecision Pass { get; } = new(false, Array.Empty<KeyEvent>(), false);
}
