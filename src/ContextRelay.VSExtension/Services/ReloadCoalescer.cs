using System;
using System.Threading;
using System.Threading.Tasks;

namespace ContextRelay.VSExtension.Services;

/// <summary>
/// Coalesces repeated triggers of an asynchronous reload so that at most one reload runs at a time, while
/// guaranteeing that a trigger which arrives while a reload is already running is never lost: the running
/// reload observes it and runs once more before releasing ownership. A reload that throws re-arms the same
/// trigger instead of dropping it, and retries with a bounded backoff so a persistently failing reload
/// (for example a settings file that stays locked or unreadable) does not spin in a tight failure loop
/// while still recovering on its own once the underlying problem clears, without depending on another,
/// unrelated trigger to happen to arrive.
/// </summary>
/// <remarks>
/// Extracted from the shared-settings file watcher in <c>ContextRelayHost</c> so the coalescing logic can
/// be exercised directly by tests, independent of file I/O or the rest of the host.
/// </remarks>
internal sealed class ReloadCoalescer
{
    private static readonly TimeSpan[] BackoffDelays =
    {
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
    };

    private readonly Func<Task> reload;
    private readonly Action<Exception>? onReloadFailed;
    private readonly Func<TimeSpan, CancellationToken, Task> delayAsync;
    private int pending;
    private int running;

    public ReloadCoalescer(
        Func<Task> reload,
        Action<Exception>? onReloadFailed = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        this.reload = reload ?? throw new ArgumentNullException(nameof(reload));
        this.onReloadFailed = onReloadFailed;
        this.delayAsync = delayAsync ?? Task.Delay;
    }

    /// <summary>
    /// Requests a reload. Returns once every trigger observed up to and including this call has been
    /// drained by some caller's reload loop (possibly a concurrently running call to this method), or the
    /// reload keeps failing and <paramref name="cancellationToken"/> cancels the retry backoff.
    /// </summary>
    public async Task TriggerAsync(CancellationToken cancellationToken = default)
    {
        Volatile.Write(ref pending, 1);
        if (Interlocked.Exchange(ref running, 1) == 1)
        {
            // Another call already owns the drain loop; it will observe this trigger via the pending flag.
            return;
        }

        var failureStreak = 0;
        try
        {
            do
            {
                try
                {
                    while (Interlocked.Exchange(ref pending, 0) == 1)
                    {
                        try
                        {
                            await reload().ConfigureAwait(false);
                            failureStreak = 0;
                        }
                        catch (Exception ex)
                        {
                            onReloadFailed?.Invoke(ex);

                            // The failed attempt's trigger must not be lost: re-arm pending so the loop
                            // retries it, backing off so a persistently failing reload does not spin tightly.
                            Volatile.Write(ref pending, 1);
                            var backoffIndex = Math.Min(failureStreak, BackoffDelays.Length - 1);
                            failureStreak++;
                            await delayAsync(BackoffDelays[backoffIndex], cancellationToken).ConfigureAwait(false);
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancellation (for example host disposal) stops the retry backoff. 'running' was already
            // released by the inner finally, and 'pending' is left set so a later, non-cancelled call still
            // picks the save back up.
            throw;
        }
    }
}
