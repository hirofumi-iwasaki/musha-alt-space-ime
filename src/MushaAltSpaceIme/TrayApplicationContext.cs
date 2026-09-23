using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MushaAltSpaceIme;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string ApplicationName = "musha-alt-space-ime";

    private readonly Control _uiDispatcher = new();
    private readonly StartupRegistration _startupRegistration = new();
    private readonly KeyboardHookService _keyboardHook = new();
    private readonly Icon _enabledIcon = TrayIcons.CreateEnabled();
    private readonly Icon _pausedIcon = TrayIcons.CreatePaused();
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ContextMenuStrip _menu;
    private readonly NotifyIcon _notifyIcon;
    private bool _isPaused;
    private bool _isDisposed;

    public TrayApplicationContext()
    {
        _uiDispatcher.CreateControl();

        _startupItem = new ToolStripMenuItem("ログオン時に起動")
        {
            CheckOnClick = true,
            Checked = _startupRegistration.IsRegistered(),
        };
        _startupItem.Click += StartupItem_Click;

        _pauseItem = new ToolStripMenuItem("一時停止");
        _pauseItem.Click += PauseItem_Click;

        var exitItem = new ToolStripMenuItem("終了");
        exitItem.Click += ExitItem_Click;

        _menu = new ContextMenuStrip();
        _menu.Items.AddRange([_startupItem, _pauseItem, new ToolStripSeparator(), exitItem]);

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = _menu,
            Icon = _enabledIcon,
            Text = $"{ApplicationName} - 有効",
            Visible = true,
        };

        try
        {
            _keyboardHook.Faulted += KeyboardHook_Faulted;
            _keyboardHook.Notice += KeyboardHook_Notice;
            _keyboardHook.EnabledChanged += KeyboardHook_EnabledChanged;
            SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
            SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;

            _keyboardHook.Start();
            _pauseItem.Enabled = false;
            _keyboardHook.SetEnabled(true);
        }
        catch
        {
            DisposeResources();
            throw;
        }

        if (_startupItem.Checked && _startupRegistration.IsDisabledByWindowsStartupSettings())
        {
            BeginOnUiThread(ShowStartupDisabledMessage);
        }
    }

    private void StartupItem_Click(object? sender, EventArgs eventArgs)
    {
        try
        {
            _startupRegistration.SetRegistered(_startupItem.Checked);
            _startupItem.Checked = _startupRegistration.IsRegistered();

            if (_startupItem.Checked && _startupRegistration.IsDisabledByWindowsStartupSettings())
            {
                ShowStartupDisabledMessage();
            }
        }
        catch (Exception exception)
        {
            // Restore the UI from the value actually stored in the Run key.
            try
            {
                _startupItem.Checked = _startupRegistration.IsRegistered();
            }
            catch
            {
                _startupItem.Checked = false;
            }

            ShowError("自動起動の設定を変更できませんでした。", exception);
        }
    }

    private void PauseItem_Click(object? sender, EventArgs eventArgs)
    {
        _pauseItem.Enabled = false;
        try
        {
            _keyboardHook.SetEnabled(_isPaused);
        }
        catch (Exception exception)
        {
            _isPaused = true;
            _pauseItem.Enabled = true;
            UpdateStatusDisplay();
            ShowError("入力処理の状態を変更できませんでした。", exception);
        }
    }

    private void ExitItem_Click(object? sender, EventArgs eventArgs)
    {
        ExitThread();
    }

    private void KeyboardHook_Faulted(string message)
    {
        BeginOnUiThread(() => HandleKeyboardHookFault(message));
    }

    private void KeyboardHook_EnabledChanged(bool enabled)
    {
        BeginOnUiThread(() =>
        {
            if (_isDisposed)
            {
                return;
            }

            _isPaused = !enabled;
            _pauseItem.Enabled = true;
            UpdateStatusDisplay();
        });
    }

    private void KeyboardHook_Notice(string message)
    {
        BeginOnUiThread(() =>
            _notifyIcon.ShowBalloonTip(5000, ApplicationName, message, ToolTipIcon.Info));
    }

    private void SystemEvents_PowerModeChanged(object? sender, PowerModeChangedEventArgs eventArgs)
    {
        if (eventArgs.Mode is PowerModes.Suspend or PowerModes.Resume)
        {
            BeginOnUiThread(ResetKeyboardHook);
        }
    }

    private void SystemEvents_SessionSwitch(object? sender, SessionSwitchEventArgs eventArgs)
    {
        if (eventArgs.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionUnlock)
        {
            BeginOnUiThread(ResetKeyboardHook);
        }
    }

    private void ResetKeyboardHook()
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            _keyboardHook.Reset();
        }
        catch (Exception exception)
        {
            HandleKeyboardHookFault($"状態をリセットできませんでした。\n{exception.Message}");
        }
    }

    private void HandleKeyboardHookFault(string message)
    {
        if (_isDisposed)
        {
            return;
        }

        _isPaused = true;
        _pauseItem.Enabled = true;
        UpdateStatusDisplay();
        ShowError("入力処理でエラーが発生しました。", message);
    }

    private void UpdateStatusDisplay()
    {
        _pauseItem.Text = _isPaused ? "再開" : "一時停止";
        _notifyIcon.Icon = _isPaused ? _pausedIcon : _enabledIcon;
        _notifyIcon.Text = _isPaused
            ? $"{ApplicationName} - 一時停止中"
            : $"{ApplicationName} - 有効";
    }

    private void ShowStartupDisabledMessage()
    {
        MessageBox.Show(
            "ログオン時に起動は登録されていますが、Windows のスタートアップ設定で無効になっています。\n" +
            "設定アプリの [アプリ] > [スタートアップ] から有効にしてください。",
            ApplicationName,
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static void ShowError(string heading, Exception exception)
    {
        ShowError(heading, exception.Message);
    }

    private static void ShowError(string heading, string detail)
    {
        MessageBox.Show(
            $"{heading}\n\n{detail}",
            ApplicationName,
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private void BeginOnUiThread(Action action)
    {
        if (_isDisposed || _uiDispatcher.IsDisposed || !_uiDispatcher.IsHandleCreated)
        {
            return;
        }

        try
        {
            _uiDispatcher.BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // The application is shutting down.
        }
    }

    protected override void ExitThreadCore()
    {
        DisposeResources();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeResources();
        }

        base.Dispose(disposing);
    }

    private void DisposeResources()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
        SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
        _keyboardHook.Faulted -= KeyboardHook_Faulted;
        _keyboardHook.Notice -= KeyboardHook_Notice;
        _keyboardHook.EnabledChanged -= KeyboardHook_EnabledChanged;

        try
        {
            try
            {
                _keyboardHook.SetEnabled(false);
            }
            catch (Exception)
            {
                // The hook may already have stopped. Disposal still releases its resources.
            }
        }
        finally
        {
            _keyboardHook.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _menu.Dispose();
            _enabledIcon.Dispose();
            _pausedIcon.Dispose();
            _uiDispatcher.Dispose();
        }
    }
}
