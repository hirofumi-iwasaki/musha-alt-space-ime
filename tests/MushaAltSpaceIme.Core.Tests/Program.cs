using MushaAltSpaceIme.Core;
using System.Runtime.InteropServices;
using MushaAltSpaceIme;

var tests = new (string Name, Action Run)[]
{
    ("left Alt alone is silent", LeftAltAloneIsSilent),
    ("Alt plus another key replays in order", AltOtherChordReplaysInOrder),
    ("Alt Space toggles once and swallows repeats", AltSpaceTogglesOnce),
    ("Alt Space then Tab restores Alt", AltSpaceThenTabRestoresAlt),
    ("unavailable layout falls through as Alt Space", UnavailableLayoutFallsThrough),
    ("Space remains swallowed when Alt releases first", AltReleasedBeforeSpace),
    ("pause drains hidden key releases", PauseDrainsHiddenReleases),
    ("pause releases only a synthetic Alt", PauseReleasesOnlySyntheticAlt),
    ("held keys cannot trigger after resume", HeldKeysCannotTriggerAfterResume),
    ("reset drains Alt and Space still held", ResetDrainsAltAndSpaceStillHeld),
    ("reset drains Space after Alt releases first", ResetDrainsSpaceAfterAltRelease),
    ("pointer promotion replays the withheld Alt once", PointerPromotionReplaysAltOnce),
    ("preheld modifier and Space do not toggle", PreheldKeysDoNotToggle),
    ("native interop declarations match the Windows ABI", NativeLayoutMatchesWindowsAbi),
    ("partial sender recovery avoids duplicate Alt release", PartialSenderRecoveryAvoidsDuplicateAltRelease),
    ("zero-accepted sender batch needs no release", ZeroAcceptedSenderBatchNeedsNoRelease),
    ("failed recovery retries only Alt release", FailedRecoveryRetriesOnlyAltRelease),
    ("partial toggle recovery never repeats grave", PartialToggleRecoveryNeverRepeatsGrave),
    ("failed two-key recovery retries grave and Alt once", FailedTwoKeyRecoveryRetriesGraveAndAltOnce),
    ("state machine sequence invariants", StateMachineSequenceInvariants),
};

var failures = new List<string>();
foreach (var (name, run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures.Add($"FAIL {name}: {ex.Message}"); }
}

if (failures.Count > 0)
{
    foreach (var failure in failures) Console.Error.WriteLine(failure);
    return 1;
}

return 0;

static KeyEvent Down(ushort key) => new(key, key, false, false);
static KeyEvent Up(ushort key) => new(key, key, true, false);
static KeyEvent AltDown() => new(AltKeyStateMachine.VK_LMENU, 0x38, false, false);
static KeyEvent AltUp() => new(AltKeyStateMachine.VK_LMENU, 0x38, true, false);
static InputDecision Send(AltKeyStateMachine machine, KeyEvent input, bool canToggle = true) => machine.Process(input, canToggle);
static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static void Events(IReadOnlyList<KeyEvent> actual, params KeyEvent[] expected) => Check(actual.SequenceEqual(expected), "unexpected replay sequence");

static void LeftAltAloneIsSilent()
{
    var m = new AltKeyStateMachine();
    Check(Send(m, Down(AltKeyStateMachine.VK_LMENU)).Suppress, "Alt down must be withheld");
    Check(Send(m, Up(AltKeyStateMachine.VK_LMENU)).Suppress, "Alt up must be withheld");
}

static void AltOtherChordReplaysInOrder()
{
    var m = new AltKeyStateMachine();
    Send(m, Down(AltKeyStateMachine.VK_LMENU));
    var tab = Send(m, Down(0x09));
    Check(tab.Suppress, "first chord key must replace original");
    Events(tab.Replay, Down(AltKeyStateMachine.VK_LMENU), Down(0x09));
    Check(!Send(m, Up(0x09)).Suppress, "later chord events pass normally");
    var altUp = Send(m, Up(AltKeyStateMachine.VK_LMENU));
    Check(altUp.Suppress, "withheld Alt up must replace original");
    Events(altUp.Replay, Up(AltKeyStateMachine.VK_LMENU));
}

static void AltSpaceTogglesOnce()
{
    var m = new AltKeyStateMachine();
    Send(m, Down(AltKeyStateMachine.VK_LMENU));
    var first = Send(m, Down(AltKeyStateMachine.VK_SPACE));
    Check(first.Suppress && first.Toggle, "first Space must toggle");
    var repeat = Send(m, Down(AltKeyStateMachine.VK_SPACE));
    Check(repeat.Suppress && !repeat.Toggle, "repeat must be swallowed");
    Check(Send(m, Up(AltKeyStateMachine.VK_SPACE)).Suppress, "Space up must be swallowed");
    var second = Send(m, Down(AltKeyStateMachine.VK_SPACE));
    Check(second.Suppress && second.Toggle, "new Space press must toggle again");
}

static void AltSpaceThenTabRestoresAlt()
{
    var m = new AltKeyStateMachine();
    Send(m, Down(AltKeyStateMachine.VK_LMENU));
    Send(m, Down(AltKeyStateMachine.VK_SPACE));
    Send(m, Up(AltKeyStateMachine.VK_SPACE));
    var tab = Send(m, Down(0x09));
    Check(tab.Suppress, "Tab after toggle must receive restored Alt atomically");
    Events(tab.Replay, Down(AltKeyStateMachine.VK_LMENU), Down(0x09));
}

static void UnavailableLayoutFallsThrough()
{
    var m = new AltKeyStateMachine();
    Send(m, Down(AltKeyStateMachine.VK_LMENU));
    var space = Send(m, Down(AltKeyStateMachine.VK_SPACE), canToggle: false);
    Check(space.Suppress && !space.Toggle, "Space must replay when no target shortcut is available");
    Events(space.Replay, Down(AltKeyStateMachine.VK_LMENU), Down(AltKeyStateMachine.VK_SPACE));
}

static void AltReleasedBeforeSpace()
{
    var m = new AltKeyStateMachine();
    Send(m, Down(AltKeyStateMachine.VK_LMENU));
    Send(m, Down(AltKeyStateMachine.VK_SPACE));
    Check(Send(m, Up(AltKeyStateMachine.VK_LMENU)).Suppress, "Alt up must stay hidden");
    Check(Send(m, Down(AltKeyStateMachine.VK_SPACE)).Suppress, "Space repeat must stay hidden");
    Check(Send(m, Up(AltKeyStateMachine.VK_SPACE)).Suppress, "late Space up must stay hidden");
}

static void PauseDrainsHiddenReleases()
{
    var m = new AltKeyStateMachine();
    Send(m, Down(AltKeyStateMachine.VK_LMENU));
    Send(m, Down(AltKeyStateMachine.VK_SPACE));
    Events(m.SetEnabled(false));
    Check(!m.IsEnabled, "machine must be paused");
    Check(Send(m, Up(AltKeyStateMachine.VK_SPACE)).Suppress, "consumed Space up must drain while paused");
    Check(Send(m, Up(AltKeyStateMachine.VK_LMENU)).Suppress, "consumed Alt up must drain while paused");
    Check(!Send(m, Down(0x41)).Suppress, "ordinary input must pass while paused");
}

static void PauseReleasesOnlySyntheticAlt()
{
    var m = new AltKeyStateMachine();
    Send(m, Down(AltKeyStateMachine.VK_LMENU));
    Send(m, Down(0x09));
    Events(m.SetEnabled(false), Up(AltKeyStateMachine.VK_LMENU));
    Check(!Send(m, Up(0x09)).Suppress, "a delivered chord key up must not be duplicated or swallowed");
    Check(Send(m, Up(AltKeyStateMachine.VK_LMENU)).Suppress, "the original withheld Alt up must still drain");
}

static void HeldKeysCannotTriggerAfterResume()
{
    var m = new AltKeyStateMachine();
    m.SetEnabled(false);
    Send(m, Down(AltKeyStateMachine.VK_LMENU));
    m.SetEnabled(true);
    Check(!Send(m, Down(AltKeyStateMachine.VK_SPACE)).Toggle, "held Alt across resume must not trigger");
    Send(m, Up(AltKeyStateMachine.VK_SPACE));
    Send(m, Up(AltKeyStateMachine.VK_LMENU));
    Send(m, Down(AltKeyStateMachine.VK_LMENU));
    Check(Send(m, Down(AltKeyStateMachine.VK_SPACE)).Toggle, "fresh Alt after release must trigger");
}

static void ResetDrainsAltAndSpaceStillHeld()
{
    var m = new AltKeyStateMachine();
    Send(m, Down(AltKeyStateMachine.VK_LMENU));
    Send(m, Down(AltKeyStateMachine.VK_SPACE));
    m.SetEnabled(false);
    Events(m.Reset([AltKeyStateMachine.VK_LMENU, AltKeyStateMachine.VK_SPACE]));
    m.SetEnabled(true);

    Check(Send(m, Down(AltKeyStateMachine.VK_SPACE)).Suppress, "held Space repeat must remain drained after reset");
    Check(Send(m, Up(AltKeyStateMachine.VK_SPACE)).Suppress, "held Space up must remain drained after reset");
    Check(Send(m, Up(AltKeyStateMachine.VK_LMENU)).Suppress, "held Alt up must remain drained after reset");
}

static void ResetDrainsSpaceAfterAltRelease()
{
    var m = new AltKeyStateMachine();
    Send(m, Down(AltKeyStateMachine.VK_LMENU));
    Send(m, Down(AltKeyStateMachine.VK_SPACE));
    Send(m, Up(AltKeyStateMachine.VK_LMENU));
    m.SetEnabled(false);
    Events(m.Reset([AltKeyStateMachine.VK_SPACE]));
    m.SetEnabled(true);

    Check(Send(m, Down(AltKeyStateMachine.VK_SPACE)).Suppress, "Space repeat must remain drained when Alt released before reset");
    Check(Send(m, Up(AltKeyStateMachine.VK_SPACE)).Suppress, "Space up must remain drained when Alt released before reset");
}

static void PointerPromotionReplaysAltOnce()
{
    var m = new AltKeyStateMachine();
    Send(m, Down(AltKeyStateMachine.VK_LMENU));
    Events(m.PromoteAltForPointer(), Down(AltKeyStateMachine.VK_LMENU));
    Events(m.PromoteAltForPointer());
    Check(!Send(m, Down(0x41)).Suppress, "keyboard input after pointer promotion must pass normally");
    var altUp = Send(m, Up(AltKeyStateMachine.VK_LMENU));
    Check(altUp.Suppress, "the original withheld Alt up must replace the physical event");
    Events(altUp.Replay, Up(AltKeyStateMachine.VK_LMENU));
}

static void PreheldKeysDoNotToggle()
{
    var modifier = new AltKeyStateMachine();
    Send(modifier, Down(AltKeyStateMachine.VK_LCONTROL));
    Check(!Send(modifier, Down(AltKeyStateMachine.VK_LMENU)).Suppress, "preheld Ctrl must leave Alt alone");
    Check(!Send(modifier, Down(AltKeyStateMachine.VK_SPACE)).Toggle, "Ctrl Alt Space must not toggle");

    var space = new AltKeyStateMachine();
    Send(space, Down(AltKeyStateMachine.VK_SPACE));
    Check(!Send(space, Down(AltKeyStateMachine.VK_LMENU)).Suppress, "preheld Space must leave Alt alone");
    Check(!Send(space, Down(AltKeyStateMachine.VK_SPACE)).Toggle, "preheld Space must not toggle");

    var letter = new AltKeyStateMachine();
    Send(letter, Down(0x41));
    Check(!Send(letter, Down(AltKeyStateMachine.VK_LMENU)).Suppress, "a preheld ordinary key must leave Alt alone");
}

static void NativeLayoutMatchesWindowsAbi()
{
    var expected = IntPtr.Size == 8
        ? new (string Name, int Actual, int Expected)[]
        {
            (nameof(NativeMethods.Input), Marshal.SizeOf<NativeMethods.Input>(), 40),
            (nameof(NativeMethods.KeyboardData), Marshal.SizeOf<NativeMethods.KeyboardData>(), 24),
            (nameof(NativeMethods.KeyboardInput), Marshal.SizeOf<NativeMethods.KeyboardInput>(), 24),
            (nameof(NativeMethods.MouseInput), Marshal.SizeOf<NativeMethods.MouseInput>(), 32),
            (nameof(NativeMethods.MouseData), Marshal.SizeOf<NativeMethods.MouseData>(), 32),
            (nameof(NativeMethods.Message), Marshal.SizeOf<NativeMethods.Message>(), 48),
            (nameof(NativeMethods.GuiThreadInfo), Marshal.SizeOf<NativeMethods.GuiThreadInfo>(), 72),
        }
        : new (string Name, int Actual, int Expected)[]
        {
            (nameof(NativeMethods.Input), Marshal.SizeOf<NativeMethods.Input>(), 28),
            (nameof(NativeMethods.KeyboardData), Marshal.SizeOf<NativeMethods.KeyboardData>(), 20),
            (nameof(NativeMethods.KeyboardInput), Marshal.SizeOf<NativeMethods.KeyboardInput>(), 16),
            (nameof(NativeMethods.MouseInput), Marshal.SizeOf<NativeMethods.MouseInput>(), 24),
            (nameof(NativeMethods.MouseData), Marshal.SizeOf<NativeMethods.MouseData>(), 24),
            (nameof(NativeMethods.Message), Marshal.SizeOf<NativeMethods.Message>(), 32),
            (nameof(NativeMethods.GuiThreadInfo), Marshal.SizeOf<NativeMethods.GuiThreadInfo>(), 48),
        };

    foreach (var (name, actual, expectedSize) in expected)
        Check(actual == expectedSize, $"{name} size must match the Windows ABI ({actual} != {expectedSize})");

    var expectedInputDataOffset = IntPtr.Size == 8 ? 8 : 4;
    var actualInputDataOffset = Marshal.OffsetOf<NativeMethods.Input>(nameof(NativeMethods.Input.Data)).ToInt32();
    Check(actualInputDataOffset == expectedInputDataOffset,
        $"INPUT.Data must begin at {expectedInputDataOffset}, but begins at {actualInputDataOffset}");
}

static void PartialSenderRecoveryAvoidsDuplicateAltRelease()
{
    var batches = new List<NativeMethods.Input[]>();
    var accepted = new Queue<uint>([1, 1]);
    var sender = new ShortcutSender(inputs => { batches.Add(inputs); return accepted.Dequeue(); });

    Check(!sender.Send([AltDown(), Down(0x09)]), "partial batch must report failure");
    Check(sender.ReleaseAlt([AltUp()]), "already-recovered Alt release must succeed");

    Check(batches.Count == 2, "recovery must avoid a duplicate Alt-up after the core cleanup");
    AssertOnlyAltUp(batches[1]);
}

static void ZeroAcceptedSenderBatchNeedsNoRelease()
{
    var batches = new List<NativeMethods.Input[]>();
    var sender = new ShortcutSender(inputs => { batches.Add(inputs); return 0; });

    Check(!sender.Send([AltDown()]), "zero accepted inputs must report failure");
    Check(sender.ReleaseAlt(Array.Empty<KeyEvent>()), "no accepted Alt must need no cleanup");
    Check(batches.Count == 1, "zero accepted inputs must not cause a spurious Alt-up");
}

static void FailedRecoveryRetriesOnlyAltRelease()
{
    var batches = new List<NativeMethods.Input[]>();
    var accepted = new Queue<uint>([1, 0, 1]);
    var sender = new ShortcutSender(inputs => { batches.Add(inputs); return accepted.Dequeue(); });

    Check(!sender.Send([AltDown(), Down(0x09)]), "partial batch must report failure");
    Check(sender.ReleaseAlt(Array.Empty<KeyEvent>()), "a still-held accepted Alt must be retried");

    Check(batches.Count == 3, "failed recovery requires one final Alt-up attempt");
    AssertOnlyAltUp(batches[1]);
    AssertOnlyAltUp(batches[2]);
}

static void PartialToggleRecoveryNeverRepeatsGrave()
{
    var batches = new List<NativeMethods.Input[]>();
    var accepted = new Queue<uint>([3, 1]);
    var sender = new ShortcutSender(inputs => { batches.Add(inputs); return accepted.Dequeue(); });
    var grave = new KeyEvent(0xC0, 0x29, false, false);

    Check(!sender.Send([
        AltDown(), grave, grave with { IsUp = true }, AltUp()
    ]), "partial toggle batch must report failure");
    Check(sender.ReleaseAlt(Array.Empty<KeyEvent>()), "successful recovery leaves no extra cleanup to send");

    Check(batches.Count == 2, "partial toggle must have one recovery batch only");
    AssertOnlyAltUp(batches[1]);
    Check(!batches.Skip(1).SelectMany(batch => batch).Any(IsGrave), "recovery must never repeat the grave key");
}

static void FailedTwoKeyRecoveryRetriesGraveAndAltOnce()
{
    var batches = new List<NativeMethods.Input[]>();
    var accepted = new Queue<uint>([2, 0, 2]);
    var sender = new ShortcutSender(inputs => { batches.Add(inputs); return accepted.Dequeue(); });
    var grave = new KeyEvent(0xC0, 0x29, false, false);

    Check(!sender.Send([AltDown(), grave, grave with { IsUp = true }, AltUp()]),
        "partial toggle batch must report failure");
    Check(sender.ReleaseAlt(Array.Empty<KeyEvent>()), "pending recovery releases must be retried");
    Check(sender.ReleaseAlt(Array.Empty<KeyEvent>()), "completed pending recovery must not repeat releases");

    Check(batches.Count == 3, "the pending two-key recovery must be sent once");
    AssertGraveThenAltUp(batches[2]);
}

static void AssertOnlyAltUp(NativeMethods.Input[] inputs)
{
    Check(inputs.Length == 1, "recovery must send exactly one event");
    var key = inputs[0].Data.Keyboard;
    Check(key.ScanCode == 0x38 && (key.Flags & NativeMethods.KeyEventUp) != 0,
        $"recovery must send only an Alt key-up (scan={key.ScanCode:X}, flags={key.Flags:X})");
}

static bool IsGrave(NativeMethods.Input input) => input.Data.Keyboard.ScanCode == 0x29;

static void AssertGraveThenAltUp(NativeMethods.Input[] inputs)
{
    Check(inputs.Length == 2, "two held keys require exactly two recovery releases");
    var grave = inputs[0].Data.Keyboard;
    var alt = inputs[1].Data.Keyboard;
    Check(grave.ScanCode == 0x29 && (grave.Flags & NativeMethods.KeyEventUp) != 0,
        "the pending grave release must be first");
    Check(alt.ScanCode == 0x38 && (alt.Flags & NativeMethods.KeyEventUp) != 0,
        "the pending Alt release must be second");
}

static void StateMachineSequenceInvariants()
{
    var machine = new AltKeyStateMachine();
    var physical = new HashSet<ushort>();
    var keys = new ushort[]
    {
        AltKeyStateMachine.VK_LMENU, AltKeyStateMachine.VK_SPACE, 0x09, 0x41,
        AltKeyStateMachine.VK_LCONTROL, AltKeyStateMachine.VK_LSHIFT,
        AltKeyStateMachine.VK_RMENU, AltKeyStateMachine.VK_LWIN,
    };
    var random = new Random(7331);
    var syntheticAltBalance = 0;

    for (var step = 0; step < 400; step++)
    {
        var key = keys[random.Next(keys.Length)];
        var wasDown = physical.Contains(key);
        var isUp = wasDown && random.Next(3) != 0;
        if (isUp) physical.Remove(key); else physical.Add(key);

        var canToggle = random.Next(4) != 0;
        var decision = Send(machine, new KeyEvent(key, key, isUp, false), canToggle);
        TrackSyntheticAlt(decision.Replay, ref syntheticAltBalance);

        if (decision.Toggle)
        {
            Check(canToggle, "a toggle cannot occur when the target shortcut is unavailable");
            Check(key == AltKeyStateMachine.VK_SPACE && !isUp && !wasDown,
                "a toggle must originate from a new physical Space down");
            Check(physical.SetEquals([AltKeyStateMachine.VK_LMENU, AltKeyStateMachine.VK_SPACE]),
                "a toggle requires exactly physical left Alt and Space");
        }

        if (step > 0 && step % 37 == 0)
        {
            TrackSyntheticAlt(machine.SetEnabled(false), ref syntheticAltBalance);
            TrackSyntheticAlt(machine.Reset(physical), ref syntheticAltBalance);
            TrackSyntheticAlt(machine.SetEnabled(true), ref syntheticAltBalance);
        }
    }

    TrackSyntheticAlt(machine.SetEnabled(false), ref syntheticAltBalance);
    foreach (var key in physical.ToArray())
    {
        physical.Remove(key);
        TrackSyntheticAlt(Send(machine, Up(key)).Replay, ref syntheticAltBalance);
    }

    Check(syntheticAltBalance == 0, "all replayed synthetic Alt downs must be paired with one release");
}

static void TrackSyntheticAlt(IReadOnlyList<KeyEvent> replay, ref int balance)
{
    foreach (var key in replay.Where(key => key.VirtualKey == AltKeyStateMachine.VK_LMENU))
    {
        balance += key.IsUp ? -1 : 1;
        Check(balance >= 0, "a synthetic Alt release cannot precede its replayed down");
    }
}
