using System.Net.Mail;
using FluentResults;

namespace EvCharging.Domain.Identity;

public sealed record EmailAddress
{
    public const int MaximumLength = 254;

    private EmailAddress(string value) => Value = value;

    public string Value { get; }

    public static Result<EmailAddress> Create(string value)
    {
        if (value.Length > MaximumLength || !MailAddress.TryCreate(value, out var address)
            || !string.Equals(value, address.Address, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Fail<EmailAddress>("A valid email address is required.");
        }

        return Result.Ok(new EmailAddress(address.Address));
    }
}
