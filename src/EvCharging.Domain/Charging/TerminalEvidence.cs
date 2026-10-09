using EvCharging.Domain.Equipment;

namespace EvCharging.Domain.Charging;

public sealed record TerminalEvidence(ProviderIdentity ProviderSession, bool IsAuthoritative);
