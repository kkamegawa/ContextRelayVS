using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ContextRelay.Core.Tests.ToolWindows;

/// <summary>
/// Groups every test that mutates the loaded extension assembly's static
/// <c>ContextRelay.VSExtension.ToolWindows.ContextRelayLocalizedStrings</c> language/locale state, so xUnit
/// never runs two of them in parallel. Each such test still saves and restores that state around its own
/// body, but those save/restore blocks only protect against sequential leakage between tests, not against
/// one test's assertions observing a language change made concurrently by another.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ContextRelayLocalizedStringsStateCollection
{
    public const string Name = "ContextRelayLocalizedStrings shared state";
}

/// <summary>
/// Covers the cross-process language refresh path added for Issue #192: the coalescer that guarantees a
/// shared-settings save is never lost or applied out of order, and the status-message relocalization that
/// runs when a language change is applied to an already-open tool window.
/// </summary>
[Collection(ContextRelayLocalizedStringsStateCollection.Name)]
public sealed class UiLanguageRefreshTests
{
    [Fact]
    public async Task ReloadCoalescer_SecondSaveDuringActiveReload_IsNotLost()
    {
        var (_, instance, trigger) = CreateCoalescer(out var callLog, out var release);
        try
        {
            // First trigger enters the reload and blocks on 'release' so a second trigger arrives while it
            // is still running, exercising the "another call already owns the drain loop" path.
            var first = (Task)trigger.Invoke(instance, new object?[] { CancellationToken.None })!;
            await WaitUntilAsync(() => callLog.Count >= 1);

            var second = (Task)trigger.Invoke(instance, new object?[] { CancellationToken.None })!;

            release.Set();
            await first;
            await second;

            // The running worker must have re-checked the pending flag set by the second trigger and
            // reloaded again, so the final state reflects both saves rather than only the first.
            Assert.True(callLog.Count >= 2, $"Expected at least 2 reloads, saw {callLog.Count}.");
        }
        finally
        {
            release.Dispose();
        }
    }

    [Fact]
    public async Task ReloadCoalescer_FailedReload_RetriesWithBackoffInsteadOfDroppingTheTrigger()
    {
        var callCount = 0;
        var failures = new ConcurrentBag<Exception>();
        var requestedDelays = new ConcurrentQueue<TimeSpan>();
        var type = LoadType("ContextRelay.VSExtension.Services.ReloadCoalescer");
        Func<Task> reload = () =>
        {
            var attempt = Interlocked.Increment(ref callCount);
            if (attempt <= 3)
            {
                throw new InvalidOperationException("Simulated reload failure.");
            }

            return Task.CompletedTask;
        };
        Action<Exception> onFailed = ex => failures.Add(ex);

        // A fake delay that records the requested backoff but completes immediately keeps this
        // deterministic and fast while still verifying the coalescer never gives up on the trigger.
        Func<TimeSpan, CancellationToken, Task> fakeDelay = (delay, _) =>
        {
            requestedDelays.Enqueue(delay);
            return Task.CompletedTask;
        };
        var instance = Activator.CreateInstance(type, reload, onFailed, fakeDelay)!;
        var trigger = type.GetMethod("TriggerAsync", BindingFlags.Public | BindingFlags.Instance)!;

        // A single trigger call must retry through every failure on its own, never depending on an
        // unrelated later trigger to happen to arrive and pick the save back up.
        await (Task)trigger.Invoke(instance, new object?[] { CancellationToken.None })!;

        Assert.Equal(4, callCount);
        Assert.Equal(3, failures.Count);
        Assert.Equal(
            new[] { TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1) },
            requestedDelays.ToArray());
    }

    [Fact]
    public async Task ReloadCoalescer_SecondTriggerArrivingDuringAFailingReload_IsNotDropped()
    {
        var callCount = 0;
        var failures = new ConcurrentBag<Exception>();
        var firstAttemptStarted = new ManualResetEventSlim(false);
        var releaseFirstAttempt = new ManualResetEventSlim(false);
        var type = LoadType("ContextRelay.VSExtension.Services.ReloadCoalescer");
        Func<Task> reload = async () =>
        {
            var attempt = Interlocked.Increment(ref callCount);
            if (attempt == 1)
            {
                // Block so a second trigger is guaranteed to arrive while this (failing) reload is active,
                // instead of only ever being issued after it has already finished.
                firstAttemptStarted.Set();
                await Task.Run(() => releaseFirstAttempt.Wait(TimeSpan.FromSeconds(5)));
                throw new InvalidOperationException("Simulated reload failure.");
            }
        };
        Action<Exception> onFailed = ex => failures.Add(ex);
        Func<TimeSpan, CancellationToken, Task> fakeDelay = (_, _) => Task.CompletedTask;

        try
        {
            var instance = Activator.CreateInstance(type, reload, onFailed, fakeDelay)!;
            var trigger = type.GetMethod("TriggerAsync", BindingFlags.Public | BindingFlags.Instance)!;

            var first = (Task)trigger.Invoke(instance, new object?[] { CancellationToken.None })!;
            Assert.True(firstAttemptStarted.Wait(TimeSpan.FromSeconds(5)), "First reload did not start in time.");

            // Arrives while the first reload is still active and blocked: must take the "another call
            // already owns the drain loop" path and return without waiting for the first reload to finish.
            var second = (Task)trigger.Invoke(instance, new object?[] { CancellationToken.None })!;
            await second;

            releaseFirstAttempt.Set();
            await first;

            // A trigger arriving mid-reload must not be dropped by the reload it arrived during: the
            // failure is reported, and a further attempt runs (via the failure's own retry, the second
            // trigger's pending flag, or both — the coalescer only tracks one pending flag) rather than the
            // window being left on the old language until an unrelated event happens to fire again.
            Assert.Single(failures);
            Assert.True(callCount >= 2, $"Expected at least 2 reload attempts, saw {callCount}.");
        }
        finally
        {
            firstAttemptStarted.Dispose();
            releaseFirstAttempt.Dispose();
        }
    }

    [Fact]
    public async Task ReloadCoalescer_CancelledDuringBackoff_LeavesTriggerPendingForALaterCall()
    {
        var callCount = 0;
        var type = LoadType("ContextRelay.VSExtension.Services.ReloadCoalescer");
        Func<Task> reload = () =>
        {
            Interlocked.Increment(ref callCount);
            throw new InvalidOperationException("Simulated reload failure.");
        };
        using var cancellation = new CancellationTokenSource();
        Func<TimeSpan, CancellationToken, Task> fakeDelay = (_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        };
        var instance = Activator.CreateInstance(type, reload, null, fakeDelay)!;
        var trigger = type.GetMethod("TriggerAsync", BindingFlags.Public | BindingFlags.Instance)!;

        // Dispose-time cancellation must stop the backoff loop rather than retry forever in the background.
        var invoke = () => (Task)trigger.Invoke(instance, new object?[] { cancellation.Token })!;
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await invoke());
        Assert.Equal(1, callCount);
    }

    [Theory]
    [InlineData("en", "ja")]
    [InlineData("ja", "en")]
    public void TryRelocalizeStaticStatus_KnownStatus_FollowsLanguageChange(string fromLanguage, string toLanguage)
    {
        // Uses the always-present ReadyStatus resource so the assertion stays valid if wording changes.
        var strings = LoadType("ContextRelay.VSExtension.ToolWindows.ContextRelayLocalizedStrings");
        var setLanguage = strings.GetMethod("SetUiLanguage", BindingFlags.Public | BindingFlags.Static)!;
        var tryRelocalize = strings.GetMethod("TryRelocalizeStaticStatus", BindingFlags.NonPublic | BindingFlags.Static)!;
        var originalLanguage = strings.GetProperty("CurrentUiLanguage")!.GetValue(null);
        try
        {
            setLanguage.Invoke(null, new object?[] { fromLanguage });
            var readyBefore = (string)strings.GetProperty("ReadyStatus")!.GetValue(null)!;

            setLanguage.Invoke(null, new object?[] { toLanguage });
            var readyAfter = (string)strings.GetProperty("ReadyStatus")!.GetValue(null)!;

            var args = new object?[] { readyBefore, null };
            var matched = (bool)tryRelocalize.Invoke(null, args)!;

            Assert.True(matched);
            Assert.Equal(readyAfter, args[1]);
        }
        finally
        {
            setLanguage.Invoke(null, new[] { originalLanguage });
        }
    }

    [Fact]
    public void TryRelocalizeStaticStatus_ArgumentCarryingStatus_IsLeftUnchanged()
    {
        var strings = LoadType("ContextRelay.VSExtension.ToolWindows.ContextRelayLocalizedStrings");
        var setLanguage = strings.GetMethod("SetUiLanguage", BindingFlags.Public | BindingFlags.Static)!;
        var tryRelocalize = strings.GetMethod("TryRelocalizeStaticStatus", BindingFlags.NonPublic | BindingFlags.Static)!;
        var getFoundResults = strings.GetMethod("GetFoundResultsStatus", BindingFlags.Public | BindingFlags.Static)!;
        var originalLanguage = strings.GetProperty("CurrentUiLanguage")!.GetValue(null);
        try
        {
            setLanguage.Invoke(null, new object?[] { "en" });
            var status = (string)getFoundResults.Invoke(null, new object[] { 3 })!;

            setLanguage.Invoke(null, new object?[] { "ja" });
            var args = new object?[] { status, null };
            var matched = (bool)tryRelocalize.Invoke(null, args)!;

            // A message built from a captured argument is not one of the fixed status resources, so it must
            // survive a language change unchanged rather than risk an incorrect or malformed value.
            Assert.False(matched);
            Assert.Equal(status, args[1]);
        }
        finally
        {
            setLanguage.Invoke(null, new[] { originalLanguage });
        }
    }

    private static (Type Type, object Instance, MethodInfo Trigger) CreateCoalescer(
        out ConcurrentQueue<int> callLog, out ManualResetEventSlim release)
    {
        var log = new ConcurrentQueue<int>();
        var releaseEvent = new ManualResetEventSlim(false);
        var callCount = 0;
        var type = LoadType("ContextRelay.VSExtension.Services.ReloadCoalescer");
        Func<Task> reload = async () =>
        {
            var attempt = Interlocked.Increment(ref callCount);
            log.Enqueue(attempt);
            if (attempt == 1)
            {
                // Block the first reload so a second trigger is guaranteed to arrive while it is active.
                await Task.Run(() => releaseEvent.Wait(TimeSpan.FromSeconds(5)));
            }
        };
        var instance = Activator.CreateInstance(type, reload, null, null)!;
        var trigger = type.GetMethod("TriggerAsync", BindingFlags.Public | BindingFlags.Instance)!;

        callLog = log;
        release = releaseEvent;
        return (type, instance, trigger);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "Condition was not met within the timeout.");
    }

    private static Type LoadType(string fullName)
    {
        var assembly = Assembly.LoadFrom(BuiltExtensionArtifactLocator.ResolveExtensionArtifactPath("ContextRelay.VSExtension.dll"));
        return assembly.GetType(fullName, true)!;
    }
}
