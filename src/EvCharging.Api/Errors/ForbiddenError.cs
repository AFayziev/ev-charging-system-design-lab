using FluentResults;

namespace EvCharging.Api.Errors;

internal sealed class ForbiddenError() : Error("You do not have permission to access this resource.");
