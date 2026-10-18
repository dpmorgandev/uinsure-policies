using System.Text.Json.Nodes;
using Uinsure.Policies.Api.Tests.Support;

namespace Uinsure.Policies.Api.Tests.Builders;

// A policyholder as the Sell request carries it. Build() with no overrides is valid: an adult on any
// start date the sale window allows.
public sealed class PolicyholderBuilder
{
    private string _firstName = "Ada";
    private string _lastName = "Lovelace";
    private DateOnly _dateOfBirth = new(1985, 12, 10);

    public PolicyholderBuilder WithName(string firstName, string lastName)
    {
        _firstName = firstName;
        _lastName = lastName;
        return this;
    }

    public PolicyholderBuilder WithDateOfBirth(DateOnly dateOfBirth)
    {
        _dateOfBirth = dateOfBirth;
        return this;
    }

    public JsonObject Build() => new()
    {
        ["firstName"] = _firstName,
        ["lastName"] = _lastName,
        ["dateOfBirth"] = _dateOfBirth.Iso(),
    };
}
