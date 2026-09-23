using System.ComponentModel;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using MushaAltSpaceIme.Core;

namespace MushaAltSpaceIme;

public sealed class KeyboardHookService : IDisposable
{
    private const uint SetStateMessage = 0x8001, ResetMessage = 0x8002, StopMessage = 0x8003;
    private readonly AltKeyStateMachine _machine = new();
    private readonly ShortcutSender _sender = new();
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly NativeMethods.HookProc _callback;
    private readonly NativeMethods.HookProc _mouseCallback;
    private readonly HashSet<nint> _notifiedLayouts = [];
    private readonly ConcurrentQueue<Action> _notifications = new();
    private int _notifying;
    private Thread? _thread;
    private uint _threadId;
    private nint _hook;
    private nint _mouseHook;
    private Exception? _startupError;
    private bool _faulted;
    private bool _disposed;
    private volatile bool _stopRequested;

    public event Action<string>? Faulted;
    public event Action<string>? Notice;
    public event Action<bool>? EnabledChanged;

    public KeyboardHookService()
    {
        _callback = OnKeyboard;
        _mouseCallback = OnMouse;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_thread is not null) throw new InvalidOperationException("入力処理は既に起動しています。");
        _thread = new Thread(Run) { IsBackground = true, Name = "Musha IME keyboard hook" };
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("入力処理の起動がタイムアウトしました。");
        if (_startupError is not null)
            throw new InvalidOperationException("キーボードフックを開始できませんでした。", _startupError);
    }

    public void SetEnabled(bool enabled) => Post(SetStateMessage, enabled ? (nuint)1 : 0);
    public void Reset() => Post(ResetMessage, 0);

    private void Post(uint message, nuint value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_thread is null || !_thread.IsAlive || _threadId == 0)
            throw new InvalidOperationException("入力処理が起動していません。");
        if (!NativeMethods.PostThreadMessageW(_threadId, message, value, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private void Run()
    {
        try
        {
            _threadId = NativeMethods.GetCurrentThreadId();
            NativeMethods.PeekMessageW(out _, 0, 0, 0, 0); // Create the thread's message queue.
            _machine.Reset(CurrentlyDown());
            if (_stopRequested) return;
            InstallHook();
            Notify(() => EnabledChanged?.Invoke(true));
            _ready.Set();
            int result = 0;
            while (!_stopRequested && (result = NativeMethods.GetMessageW(out var message, 0, 0, 0)) > 0)
            {
                if (message.Id == StopMessage) break;
                if (message.Id == SetStateMessage)
                {
                    bool enabled = message.WParam != 0;
                    if (enabled) _faulted = false;
                    if (!Cleanup(_machine.SetEnabled(false))) continue;
                    if (enabled)
                    {
                        // The disabled hook still tracks physical input. Do not replace
                        // it with async OS state: withheld keys may be absent there.
                        ReinstallHook();
                        _faulted = false;
                        _machine.SetEnabled(true);
                    }
                    Notify(() => EnabledChanged?.Invoke(enabled));
                }
                else if (message.Id == ResetMessage)
                {
                    if (!Cleanup(_machine.Reset(CurrentlyDown()))) continue;
                    ReinstallHook();
                }
            }
            if (result < 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        catch (Exception error)
        {
            if (!_ready.IsSet) _startupError = error;
            else ReportFault("入力処理が停止しました。アプリを起動し直してください。");
        }
        finally
        {
            _sender.ReleaseAlt(_machine.SetEnabled(false));
            if (_hook != 0) NativeMethods.UnhookWindowsHookEx(_hook);
            if (_mouseHook != 0) NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _hook = 0;
            _mouseHook = 0;
            _ready.Set();
        }
    }

    private void InstallHook()
    {
        _hook = NativeMethods.SetWindowsHookExW(NativeMethods.WhKeyboardLl, _callback,
            NativeMethods.GetModuleHandleW(null), 0);
        if (_hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        _mouseHook = NativeMethods.SetWindowsHookExW(NativeMethods.WhMouseLl, _mouseCallback,
            NativeMethods.GetModuleHandleW(null), 0);
        if (_mouseHook == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private void ReinstallHook()
    {
        if (_hook != 0) NativeMethods.UnhookWindowsHookEx(_hook);
        if (_mouseHook != 0) NativeMethods.UnhookWindowsHookEx(_mouseHook);
        _hook = 0;
        _mouseHook = 0;
        InstallHook();
    }

    private nint OnMouse(int code, nuint wParam, nint lParam)
    {
        if (code < 0) return NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam);
        uint flags = (uint)wParam switch
        {
            0x0201 => 0x0002, 0x0202 => 0x0004, // left button
            0x0204 => 0x0008, 0x0205 => 0x0010, // right button
            0x0207 => 0x0020, 0x0208 => 0x0040, // middle button
            0x020B => 0x0080, 0x020C => 0x0100, // X buttons
            0x020A => 0x0800, 0x020E => 0x1000, // vertical/horizontal wheel
            _ => 0
        };
        if (flags == 0) return NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam);
        try
        {
            var data = Marshal.PtrToStructure<NativeMethods.MouseData>(lParam);
            if ((data.Flags & 1) != 0 || data.ExtraInfo == NativeMethods.InputTag)
                return NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam);
            IReadOnlyList<KeyEvent> prefix = _machine.PromoteAltForPointer();
            if (prefix.Count == 0) return NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam);
            // Inserting only Alt then passing the original click can reverse their order.
            // Replace both in one SendInput batch and suppress the original event.
            if (!_sender.SendPointer(prefix, data, flags))
            {
                _sender.ReleaseAlt(_machine.SetEnabled(false));
                ReportFault("Altとマウスの入力を送信できなかったため一時停止しました。");
            }
            return 1;
        }
        catch
        {
            _sender.ReleaseAlt(_machine.SetEnabled(false));
            ReportFault("マウス入力処理でエラーが発生したため一時停止しました。");
            return NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam);
        }
    }

    private nint OnKeyboard(int code, nuint wParam, nint lParam)
    {
        if (code < 0) return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
        try
        {
            var data = Marshal.PtrToStructure<NativeMethods.KeyboardData>(lParam);
            // Our replay must pass unchanged. Leave other input injectors alone, too.
            if ((data.Flags & NativeMethods.Injected) != 0 || data.ExtraInfo == NativeMethods.InputTag)
                return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);

            var key = new KeyEvent((ushort)data.VirtualKey, (ushort)data.ScanCode,
                (data.Flags & NativeMethods.KeyUp) != 0, (data.Flags & NativeMethods.Extended) != 0);
            InputTarget? target = null;
            ToggleShortcut? shortcut = null;
            if (key.VirtualKey == 0x20 && !key.IsUp && _machine.IsEnabled)
            {
                target = InputTarget.Capture();
                shortcut = ShortcutSender.Resolve(target);
            }
            InputDecision decision = _machine.Process(key, shortcut.HasValue);
            bool success = decision.Toggle
                ? shortcut is { } resolved && _sender.SendToggle(resolved)
                : _sender.Send(decision.Replay);
            if (!success)
            {
                _sender.ReleaseAlt(_machine.SetEnabled(false));
                ReportFault("キー入力を送信できなかったため一時停止しました。入力先の権限やフォーカスを確認し、再開してください。");
            }
            else if (shortcut is null && target is { } unsupported && decision.Replay.Count > 0
                && key.VirtualKey == 0x20 && !key.IsUp && _notifiedLayouts.Add(unsupported.Layout))
            {
                ReportNotice("この配列ではAlt + バッククォート相当を生成できないため、通常のAlt + Spaceを使用します。");
            }
            return decision.Suppress ? 1 : NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
        }
        catch
        {
            _sender.ReleaseAlt(_machine.SetEnabled(false));
            ReportFault("入力処理でエラーが発生したため一時停止しました。");
            return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
        }
    }

    private bool Cleanup(IReadOnlyList<KeyEvent> releases)
    {
        if (_sender.ReleaseAlt(releases)) return true;
        _machine.SetEnabled(false);
        ReportFault("キー状態の復元に失敗したため一時停止しました。Altキーを押して離してから再開してください。");
        return false;
    }

    private void ReportFault(string message)
    {
        if (_faulted) return;
        _faulted = true;
        Notify(() => Faulted?.Invoke(message));
    }

    private void ReportNotice(string message) => Notify(() => Notice?.Invoke(message));

    private void Notify(Action action)
    {
        _notifications.Enqueue(action);
        if (Interlocked.CompareExchange(ref _notifying, 1, 0) == 0)
            ThreadPool.QueueUserWorkItem(_ => DrainNotifications());
    }

    private void DrainNotifications()
    {
        do
        {
            while (_notifications.TryDequeue(out var action))
            {
                try { action(); }
                catch (InvalidOperationException) { /* UI may already be disposed. */ }
            }
            Interlocked.Exchange(ref _notifying, 0);
        } while (!_notifications.IsEmpty && Interlocked.CompareExchange(ref _notifying, 1, 0) == 0);
    }

    private static IEnumerable<ushort> CurrentlyDown()
    {
        // Called only at lifecycle boundaries, outside low-level input callbacks.
        for (ushort key = 8; key < 256; key++)
            if (key is not (0x10 or 0x11 or 0x12) && (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0)
                yield return key;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stopRequested = true;
        if (_thread?.IsAlive == true)
        {
            NativeMethods.PostThreadMessageW(_threadId, StopMessage, 0, 0);
            // Never join from a hook callback. The UI does not receive synchronous calls.
            if (!_thread.Join(TimeSpan.FromSeconds(3))) return;
        }
        _ready.Dispose();
    }
}
