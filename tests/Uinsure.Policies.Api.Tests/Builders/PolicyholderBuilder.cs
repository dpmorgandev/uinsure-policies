using System.Text.Json.Nodes;
using Uinsure.Policies.Api.Tests.Support;

namespace Uinsure.Policies.Api.Tests.Builders;

// A policyholder as the Sell request carries it. Build() with no overrides is valid: an adult on any
// start date the sale window allows. None of it is the api.md example holder (Ada Lovelace, 1985-12-10);
// BuilderDefaultsTests guards that.
public sealed class PolicyholderBuilder
{
    private string _firstName = "Katherine";
    private string _lastName = "Johnson";
    private DateOnly _dateOfBirth = new(1981, 8, 26);

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
