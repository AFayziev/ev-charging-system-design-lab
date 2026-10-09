namespace EvCharging.Domain.Charging;

public static class CommandPolicy
{
    /// <summary>Evidence is authoritative when confirmed or definitively rejected; acknowledgements remain pending.</summary>
    public static CommandOutcome ApplyEvidence(CommandOutcome current, CommandOutcome evidence) =>
        current is CommandOutcome.Confirmed or CommandOutcome.Rejected
            || (current == CommandOutcome.Uncertain && evidence == CommandOutcome.Pending)
            ? current
            : evidence;
}
