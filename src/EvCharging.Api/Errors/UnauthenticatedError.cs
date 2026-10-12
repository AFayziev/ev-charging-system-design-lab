using FluentResults;

namespace EvCharging.Api.Errors;

internal sealed class UnauthenticatedError() : Error("Authentication is required.");
