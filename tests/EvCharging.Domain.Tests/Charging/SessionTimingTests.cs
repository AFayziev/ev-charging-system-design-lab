using EvCharging.Domain.Charging;
using Xunit;

namespace EvCharging.Domain.Tests.Charging;

public sealed class SessionTimingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(SessionLifecycle.Active, 5, false, false)]
    [InlineData(SessionLifecycle.Active, 11, false, true)]
    [InlineData(SessionLifecycle.Stopping, 11, false, true)]
    [InlineData(SessionLifecycle.Stopping, 11, true, false)]
    [InlineData(SessionLifecycle.Completed, 11, false, false)]
    [InlineData(SessionLifecycle.Failed, 11, false, false)]
    [InlineData(SessionLifecycle.Starting, 11, false, false)]
    public void IsSilentFor_LastHeardAgo_FollowsLifecycleAndFinalReading(
        SessionLifecycle lifecycle, int secondsAgo, bool isFinal, bool expected)
    {
        var silent = SessionTiming.IsSilentFor(lifecycle, Now.AddSeconds(-secondsAgo), isFinal, Now, Limit);

        Assert.Equal(expected, silent);
    }

    [Fact]
    public void IsSilentFor_StartedWithoutAnyReading_CountsFromTheStart()
    {
        // The caller passes the start as the last-heard instant when nothing has been reported.
        Assert.True(SessionTiming.IsSilentFor(SessionLifecycle.Active, Now.AddMinutes(-1), false, Now, Limit));
        Assert.False(SessionTiming.IsSilentFor(SessionLifecycle.Active, Now.AddSeconds(-3), false, Now, Limit));
    }

    [Fact]
    public void IsSilentFor_NothingHeardOrStarted_IsNotSilent() =>
        Assert.False(SessionTiming.IsSilentFor(SessionLifecycle.Active, null, false, Now, Limit));

    [Fact]
    public void Elapsed_LiveSession_RunsUntilNow()
    {
        var elapsed = SessionTiming.Elapsed(
            SessionLifecycle.Active, Now.AddMinutes(-3), Now.AddSeconds(-2), false, Now);

        Assert.Equal(TimeSpan.FromMinutes(3), elapsed);
    }

    [Fact]
    public void Elapsed_FinalReadingIn_StopsAtTheLastReport()
    {
        var elapsed = SessionTiming.Elapsed(
            SessionLifecycle.Stopping, Now.AddMinutes(-3), Now.AddMinutes(-1), true, Now);

        Assert.Equal(TimeSpan.FromMinutes(2), elapsed);
    }

    [Theory]
    [InlineData(SessionLifecycle.Completed)]
    [InlineData(SessionLifecycle.Failed)]
    public void Elapsed_EndedSession_StopsAtTheLastReportOrIsUnknown(SessionLifecycle lifecycle)
    {
        Assert.Equal(
            TimeSpan.FromMinutes(2),
            SessionTiming.Elapsed(lifecycle, Now.AddMinutes(-3), Now.AddMinutes(-1), false, Now));
        Assert.Null(SessionTiming.Elapsed(lifecycle, Now.AddMinutes(-3), null, false, Now));
    }

    [Fact]
    public void Elapsed_NotStarted_IsUnknown() =>
        Assert.Null(SessionTiming.Elapsed(SessionLifecycle.Starting, null, null, false, Now));

    [Fact]
    public void Elapsed_ReportDatedBeforeTheStart_IsZero() =>
        Assert.Equal(
            TimeSpan.Zero,
            SessionTiming.Elapsed(SessionLifecycle.Stopping, Now, Now.AddSeconds(-5), true, Now));
}
