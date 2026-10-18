using Uinsure.Policies.Domain.Policies;

namespace Uinsure.Policies.Domain.Tests.Builders;

// Build() with no overrides is an adult on any start date the sale window allows.
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

    public Policyholder Build() => new(_firstName, _lastName, _dateOfBirth);
}
