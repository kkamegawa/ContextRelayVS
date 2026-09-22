using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ContextRelay.Core.Tests.ToolWindows;

/// <summary>
/// Covers the cross-process language refresh path added for Issue #192: the coalescer that guarantees a
/// shared-settings save is never lost or applied out of order, and the status-message relocalization that
/// runs when a language change is applied to an already-open tool window.
/// </summary>
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
            var first = (Task)trigger.Invoke(instance, null)!;
            await WaitUntilAsync(() => callLog.Count >= 1);

            var second = (Task)trigger.Invoke(instance, null)!;

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
    public async Task ReloadCoalescer_FailedReload_StillDrainsALaterPendingTrigger()
    {
        var callCount = 0;
        var failures = new ConcurrentBag<Exception>();
        var type = LoadType("ContextRelay.VSExtension.Services.ReloadCoalescer");
        Func<Task> reload = () =>
        {
            var attempt = Interlocked.Increment(ref callCount);
            if (attempt == 1)
            {
                throw new InvalidOperationException("Simulated reload failure.");
            }

            return Task.CompletedTask;
        };
        Action<Exception> onFailed = ex => failures.Add(ex);
        var instance = Activator.CreateInstance(type, reload, onFailed)!;
        var trigger = type.GetMethod("TriggerAsync", BindingFlags.Public | BindingFlags.Instance)!;

        // A single trigger call drains its own failure; a second logical save queued behind it (simulated
        // here by the reload itself failing once) must still be observed on a later call.
        await (Task)trigger.Invoke(instance, null)!;
        Assert.Single(failures);

        await (Task)trigger.Invoke(instance, null)!;
        Assert.Equal(2, callCount);
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
        var instance = Activator.CreateInstance(type, reload, null)!;
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
