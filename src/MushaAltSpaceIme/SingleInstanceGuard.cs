using System;
using System.Threading;
using System.Security.Principal;

namespace MushaAltSpaceIme;

internal sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex? _mutex;
    private readonly bool _ownsMutex;

    public SingleInstanceGuard()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value
            ?? throw new InvalidOperationException("現在のユーザーを取得できませんでした。");
        var name = $"Local\\MushaAltSpaceIme-{sid}";

        _mutex = new Mutex(true, name, out var createdNew);
        IsPrimaryInstance = createdNew;
        _ownsMutex = createdNew;
    }

    public bool IsPrimaryInstance { get; }

    public void Dispose()
    {
        if (_ownsMutex)
        {
            _mutex?.ReleaseMutex();
        }

        _mutex?.Dispose();
    }
}
