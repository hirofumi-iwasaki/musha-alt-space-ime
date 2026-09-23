namespace MushaAltSpaceIme.Core;

/// <summary>
/// Decides how physical keyboard events participate in the left Alt + Space
/// gesture. It deliberately knows nothing about hooks, layouts, or SendInput.
/// </summary>
public sealed class AltKeyStateMachine
{
    public const ushort VK_SPACE = 0x20;
    public const ushort VK_LMENU = 0xA4;
    public const ushort VK_RMENU = 0xA5;
    public const ushort VK_CONTROL = 0x11;
    public const ushort VK_SHIFT = 0x10;
    public const ushort VK_LCONTROL = 0xA2;
    public const ushort VK_RCONTROL = 0xA3;
    public const ushort VK_LSHIFT = 0xA0;
    public const ushort VK_RSHIFT = 0xA1;
    public const ushort VK_LWIN = 0x5B;
    public const ushort VK_RWIN = 0x5C;

    private readonly HashSet<ushort> _physicalDown = [];
    private bool _altWasSuppressed;
    private bool _normalAltChordUnderway;
    private bool _logicalAltDown;
    private bool _spaceWasSuppressed;
    private bool _ignoreLeftAltUntilUp;
    private KeyEvent _leftAltDown;

    public bool IsEnabled { get; private set; } = true;

    /// <summary>Processes one physical event. Injected events must be filtered by the caller.</summary>
    public InputDecision Process(KeyEvent input, bool canToggle)
    {
        var wasDown = _physicalDown.Contains(input.VirtualKey);
        if (input.IsUp)
            _physicalDown.Remove(input.VirtualKey);
        else
            _physicalDown.Add(input.VirtualKey);

        if (!IsEnabled)
            return DrainWhileDisabled(input);

        if (input.VirtualKey == VK_LMENU)
            return ProcessLeftAlt(input, wasDown);

        if (input.VirtualKey == VK_SPACE && _spaceWasSuppressed)
        {
            if (input.IsUp)
                _spaceWasSuppressed = false;
            return SuppressOnly();
        }

        if (_altWasSuppressed)
        {
            if (_normalAltChordUnderway)
                return InputDecision.Pass;

            if (input.VirtualKey == VK_SPACE && !input.IsUp && !wasDown && canToggle && CanToggleNow())
            {
                _spaceWasSuppressed = true;
                return new InputDecision(true, Array.Empty<KeyEvent>(), true);
            }

            // The initial physical Alt was withheld. The first other event must
            // be replayed together with it to preserve the shortcut's order.
            return StartNormalAltChord(input);
        }

        return InputDecision.Pass;
    }

    /// <summary>
    /// Enables or pauses recognition. Returned events must be sent atomically
    /// before the hook changes its enabled behavior.
    /// </summary>
    public IReadOnlyList<KeyEvent> SetEnabled(bool enabled)
    {
        if (IsEnabled == enabled)
            return Array.Empty<KeyEvent>();

        IsEnabled = enabled;
        if (!enabled)
            return ReleaseLogicalAlt();

        // A key held across resume cannot start a new gesture. It must be
        // released and pressed again after the state boundary.
        _ignoreLeftAltUntilUp = _physicalDown.Contains(VK_LMENU);
        return Array.Empty<KeyEvent>();
    }

    /// <summary>
    /// Discards an old input boundary (lock, sleep, hook restart) and returns
    /// the one synthetic release required to avoid a stuck logical Alt.
    /// </summary>
    public IReadOnlyList<KeyEvent> Reset(IEnumerable<ushort> currentlyDown)
    {
        ArgumentNullException.ThrowIfNull(currentlyDown);

        var cleanup = ReleaseLogicalAlt();
        var drainWithheldAlt = _altWasSuppressed;
        var drainWithheldSpace = _spaceWasSuppressed;
        _physicalDown.Clear();
        foreach (var key in currentlyDown)
            _physicalDown.Add(key);

        // A boundary must not forget a key-down we hid: its repeat and up still
        // belong to the hidden gesture. Keys already released have nothing left
        // to drain, and cannot be revived by a later event.
        _altWasSuppressed = drainWithheldAlt && _physicalDown.Contains(VK_LMENU);
        _normalAltChordUnderway = false;
        _spaceWasSuppressed = drainWithheldSpace && _physicalDown.Contains(VK_SPACE);
        _ignoreLeftAltUntilUp = _physicalDown.Contains(VK_LMENU);
        return cleanup;
    }

    /// <summary>
    /// Promotes a withheld left Alt so a pointer gesture can use the ordinary
    /// Alt-modified behavior. The caller sends this replay before its pointer
    /// input, then feeds later physical keyboard events back to <see cref="Process"/>.
    /// </summary>
    public IReadOnlyList<KeyEvent> PromoteAltForPointer()
    {
        if (!IsEnabled || !_altWasSuppressed || _normalAltChordUnderway)
            return Array.Empty<KeyEvent>();

        _logicalAltDown = true;
        _normalAltChordUnderway = true;
        return [_leftAltDown];
    }

    private InputDecision ProcessLeftAlt(KeyEvent input, bool wasDown)
    {
        if (input.IsUp)
        {
            _ignoreLeftAltUntilUp = false;
            if (!_altWasSuppressed)
                return InputDecision.Pass;

            _altWasSuppressed = false;
            _normalAltChordUnderway = false;
            return new InputDecision(true, ReleaseLogicalAlt(), false);
        }

        if (wasDown)
            return _altWasSuppressed ? SuppressOnly() : InputDecision.Pass;

        if (_ignoreLeftAltUntilUp || HasAnyPhysicalKeyOtherThanLeftAlt())
            return InputDecision.Pass;

        _leftAltDown = input;
        _altWasSuppressed = true;
        _normalAltChordUnderway = false;
        return SuppressOnly();
    }

    private InputDecision StartNormalAltChord(KeyEvent input)
    {
        _logicalAltDown = true;
        _altWasSuppressed = true;
        _normalAltChordUnderway = true;
        return new InputDecision(true, [_leftAltDown, input], false);
    }

    private InputDecision DrainWhileDisabled(KeyEvent input)
    {
        // A key-down that was hidden before pausing must have its matching up
        // hidden too. All keys whose down was delivered remain ordinary input.
        if (input.VirtualKey == VK_SPACE && _spaceWasSuppressed)
        {
            if (input.IsUp)
                _spaceWasSuppressed = false;
            return SuppressOnly();
        }

        if (input.VirtualKey == VK_LMENU && _altWasSuppressed)
        {
            if (input.IsUp)
            {
                _altWasSuppressed = false;
                _normalAltChordUnderway = false;
                _ignoreLeftAltUntilUp = false;
            }
            return SuppressOnly();
        }

        return InputDecision.Pass;
    }

    private bool CanToggleNow() => !_ignoreLeftAltUntilUp && !HasDisqualifyingModifier();

    private bool HasAnyPhysicalKeyOtherThanLeftAlt() =>
        _physicalDown.Any(key => key != VK_LMENU);

    private bool HasDisqualifyingModifier() =>
        _physicalDown.Contains(VK_RMENU) ||
        _physicalDown.Contains(VK_CONTROL) ||
        _physicalDown.Contains(VK_LCONTROL) ||
        _physicalDown.Contains(VK_RCONTROL) ||
        _physicalDown.Contains(VK_SHIFT) ||
        _physicalDown.Contains(VK_LSHIFT) ||
        _physicalDown.Contains(VK_RSHIFT) ||
        _physicalDown.Contains(VK_LWIN) ||
        _physicalDown.Contains(VK_RWIN);

    private IReadOnlyList<KeyEvent> ReleaseLogicalAlt()
    {
        if (!_logicalAltDown)
            return Array.Empty<KeyEvent>();

        _logicalAltDown = false;
        return [_leftAltDown with { IsUp = true }];
    }

    private static InputDecision SuppressOnly() => new(true, Array.Empty<KeyEvent>(), false);
}
