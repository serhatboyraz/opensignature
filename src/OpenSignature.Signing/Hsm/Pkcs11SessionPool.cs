using OpenSignature.Signing.Pkcs11;

namespace OpenSignature.Signing.Hsm;

/// <summary>
/// Bounded pool of logged-in PKCS#11 sessions for HSM concurrency control.
/// </summary>
public sealed class Pkcs11SessionPool : IDisposable
{
    private readonly IPkcs11Slot _slot;
    private readonly string _pin;
    private readonly SemaphoreSlim _semaphore;
    private readonly Stack<IPkcs11Session> _idle = new();
    private readonly HashSet<IPkcs11Session> _all = [];
    private readonly object _gate = new();
    private readonly int _maxSessions;
    private bool _disposed;
    private int _created;

    public Pkcs11SessionPool(IPkcs11Slot slot, string pin, int maxSessions)
    {
        ArgumentNullException.ThrowIfNull(slot);
        if (string.IsNullOrEmpty(pin))
        {
            throw new ArgumentException("PIN must not be empty.", nameof(pin));
        }

        if (maxSessions < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSessions), maxSessions, "Max sessions must be at least 1.");
        }

        _slot = slot;
        _pin = pin;
        _maxSessions = maxSessions;
        _semaphore = new SemaphoreSlim(maxSessions, maxSessions);
    }

    public int MaxSessions => _maxSessions;

    public int AvailablePermits => _semaphore.CurrentCount;

    public int CreatedSessionCount
    {
        get
        {
            lock (_gate)
            {
                return _created;
            }
        }
    }

    public async Task<IPkcs11Session> RentAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            lock (_gate)
            {
                ThrowIfDisposed();

                if (_idle.Count > 0)
                {
                    return _idle.Pop();
                }

                if (_created >= _maxSessions)
                {
                    throw new InvalidOperationException(
                        "PKCS#11 session pool exhausted unexpectedly while holding a concurrency permit.");
                }

                var session = _slot.OpenSession(readWrite: false);
                try
                {
                    session.Login(_pin);
                }
                catch
                {
                    session.Dispose();
                    throw;
                }

                _all.Add(session);
                _created++;
                return session;
            }
        }
        catch
        {
            _semaphore.Release();
            throw;
        }
    }

    public void Return(IPkcs11Session session)
    {
        ArgumentNullException.ThrowIfNull(session);

        lock (_gate)
        {
            if (_disposed)
            {
                DisposeSession(session);
                return;
            }

            if (!_all.Contains(session))
            {
                DisposeSession(session);
                return;
            }

            _idle.Push(session);
        }

        _semaphore.Release();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        lock (_gate)
        {
            while (_idle.Count > 0)
            {
                DisposeSession(_idle.Pop());
            }

            foreach (var session in _all.ToArray())
            {
                DisposeSession(session);
            }

            _all.Clear();
            _created = 0;
        }

        _semaphore.Dispose();
    }

    private static void DisposeSession(IPkcs11Session session)
    {
        try
        {
            if (session.IsLoggedIn)
            {
                session.Logout();
            }
        }
        catch (InvalidOperationException)
        {
            // Best-effort logout.
        }

        session.Dispose();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
