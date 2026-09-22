using System;
using System.Threading;
using System.Threading.Tasks;

namespace ContextRelay.VSExtension.Services;

/// <summary>
/// Coalesces repeated triggers of an asynchronous reload so that at most one reload runs at a time, while
/// guaranteeing that a trigger which arrives while a reload is already running is never lost: the running
/// reload observes it and runs once more before releasing ownership. A reload that throws is reported
/// through <see cref="onReloadFailed"/> but does not stop later triggers from being drained, so a single
/// failure cannot leave a later save stranded until an unrelated event happens to fire again.
/// </summary>
/// <remarks>
/// Extracted from the shared-settings file watcher in <c>ContextRelayHost</c> so the coalescing logic can
/// be exercised directly by tests, independent of file I/O or the rest of the host.
/// </remarks>
internal sealed class ReloadCoalescer
{
    private readonly Func<Task> reload;
    private readonly Action<Exception>? onReloadFailed;
    private int pending;
    private int running;

    public ReloadCoalescer(Func<Task> reload, Action<Exception>? onReloadFailed = null)
    {
        this.reload = reload ?? throw new ArgumentNullException(nameof(reload));
        this.onReloadFailed = onReloadFailed;
    }

    /// <summary>
    /// Requests a reload. Returns once every trigger observed up to and including this call has been
    /// drained by some caller's reload loop (possibly a concurrently running call to this method).
    /// </summary>
    public async Task TriggerAsync()
    {
        Volatile.Write(ref pending, 1);
        if (Interlocked.Exchange(ref running, 1) == 1)
        {
            // Another call already owns the drain loop; it will observe this trigger via the pending flag.
            return;
        }

        do
        {
            try
            {
                while (Interlocked.Exchange(ref pending, 0) == 1)
                {
                    try
                    {
                        await reload().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        onReloadFailed?.Invoke(ex);
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref running, 0);
            }
        }

        // A trigger can arrive after the inner loop observed pending == 0 but before running is cleared;
        // re-enter instead of dropping it.
        while (Volatile.Read(ref pending) == 1 && Interlocked.Exchange(ref running, 1) == 0);
    }
}
