using EvCharging.Domain.Observations;
using Xunit;

namespace EvCharging.Domain.Tests.Observations;

public sealed class SessionEvidenceTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Skew = TimeSpan.FromSeconds(5);

    [Fact]
    public void Record_FirstReading_SetsLastObservedAt()
    {
        var evidence = NewEvidence();

        Record(evidence, occurredAt: Start);

        Assert.Equal(Start, evidence.LastObservedAt);
        Assert.Null(evidence.FinalEnergyKWh);
    }

    [Fact]
    public void Record_NewerReading_AdvancesLastObservedAt()
    {
        var evidence = NewEvidence();
        Record(evidence, occurredAt: Start);

        Record(evidence, occurredAt: Start.AddSeconds(5));

        Assert.Equal(Start.AddSeconds(5), evidence.LastObservedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Record_EqualOrOlderReading_LeavesLastObservedAt(int secondsFromLast)
    {
        var evidence = NewEvidence();
        Record(evidence, occurredAt: Start.AddSeconds(10));

        Record(evidence, occurredAt: Start.AddSeconds(10 + secondsFromLast));

        Assert.Equal(Start.AddSeconds(10), evidence.LastObservedAt);
    }

    [Fact]
    public void Record_SameReadingTwice_ChangesNothingTheSecondTime()
    {
        var evidence = NewEvidence();
        Record(evidence, occurredAt: Start, energyKWh: 4.5m, isFinal: true);
        var version = (evidence.LastObservedAt, evidence.FinalEnergyKWh, evidence.FinalReadingAt);

        Record(evidence, occurredAt: Start, energyKWh: 4.5m, isFinal: true);

        Assert.Equal(version, (evidence.LastObservedAt, evidence.FinalEnergyKWh, evidence.FinalReadingAt));
    }

    [Fact]
    public void Record_ReadingAheadOfReceiptBeyondSkew_DoesNotAdvanceLastObservedAt()
    {
        var evidence = NewEvidence();
        Record(evidence, occurredAt: Start);

        Record(evidence, occurredAt: Start.AddSeconds(66), receivedAt: Start.AddSeconds(1));

        Assert.Equal(Start, evidence.LastObservedAt);
    }

    [Fact]
    public void Record_FinalReadingAheadOfReceiptBeyondSkew_FillsFinalFactsWithoutAdvancingLastObservedAt()
    {
        var evidence = NewEvidence();

        Record(evidence, occurredAt: Start.AddSeconds(60), receivedAt: Start, energyKWh: 7.25m, isFinal: true);

        Assert.Null(evidence.LastObservedAt);
        Assert.Equal(7.25m, evidence.FinalEnergyKWh);
        Assert.Equal(Start.AddSeconds(60), evidence.FinalReadingAt);
    }

    [Fact]
    public void Record_ReadingAheadOfReceiptWithinSkew_IsAccepted()
    {
        var evidence = NewEvidence();

        Record(evidence, occurredAt: Start.AddSeconds(5), receivedAt: Start);

        Assert.Equal(Start.AddSeconds(5), evidence.LastObservedAt);
    }

    [Fact]
    public void Record_FinalReading_FillsFinalFacts()
    {
        var evidence = NewEvidence();

        Record(evidence, occurredAt: Start, energyKWh: 7.25m, isFinal: true);

        Assert.Equal(7.25m, evidence.FinalEnergyKWh);
        Assert.Equal(Start, evidence.FinalReadingAt);
    }

    [Fact]
    public void Record_FinalReadingOlderThanLastObserved_StillFillsFinalFactsWithoutMovingLastObservedAt()
    {
        var evidence = NewEvidence();
        Record(evidence, occurredAt: Start.AddSeconds(10), energyKWh: 8m);

        Record(evidence, occurredAt: Start, energyKWh: 7.25m, isFinal: true);

        Assert.Equal(Start.AddSeconds(10), evidence.LastObservedAt);
        Assert.Equal(7.25m, evidence.FinalEnergyKWh);
        Assert.Equal(Start, evidence.FinalReadingAt);
    }

    [Fact]
    public void Record_SecondFinalReading_KeepsTheFirst()
    {
        var evidence = NewEvidence();
        Record(evidence, occurredAt: Start, energyKWh: 7.25m, isFinal: true);

        Record(evidence, occurredAt: Start.AddSeconds(1), energyKWh: 9m, isFinal: true);

        Assert.Equal(7.25m, evidence.FinalEnergyKWh);
        Assert.Equal(Start, evidence.FinalReadingAt);
    }

    [Fact]
    public void Record_FinalReading_ReportsTheFinalFactsOnlyTheFirstTime()
    {
        var evidence = NewEvidence();

        Assert.False(Record(evidence, occurredAt: Start));
        Assert.True(Record(evidence, occurredAt: Start.AddSeconds(1), isFinal: true));
        Assert.False(Record(evidence, occurredAt: Start.AddSeconds(1), isFinal: true));
        Assert.False(Record(evidence, occurredAt: Start.AddSeconds(2), isFinal: true));
    }

    [Fact]
    public void Record_SubMicrosecondOccurrence_IsKeptToTheMicrosecond()
    {
        var evidence = NewEvidence();

        Record(evidence, occurredAt: Start.AddTicks(7));

        Assert.Equal(Start, evidence.LastObservedAt);
    }

    [Fact]
    public void Create_EmptySessionId_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SessionEvidence.Create(Guid.Empty));

    private static SessionEvidence NewEvidence() => SessionEvidence.Create(Guid.CreateVersion7());

    private static bool Record(
        SessionEvidence evidence,
        DateTimeOffset occurredAt,
        DateTimeOffset? receivedAt = null,
        decimal energyKWh = 1m,
        bool isFinal = false) =>
        evidence.Record(occurredAt, receivedAt ?? occurredAt, energyKWh, isFinal, Skew);
}
