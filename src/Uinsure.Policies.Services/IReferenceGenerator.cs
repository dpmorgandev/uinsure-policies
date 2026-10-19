namespace Uinsure.Policies.Services;

public interface IReferenceGenerator
{
    string Payment();

    public string Refund() => throw new NotImplementedException();
}
