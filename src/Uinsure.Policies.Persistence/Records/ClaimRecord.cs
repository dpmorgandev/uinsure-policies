namespace Uinsure.Policies.Persistence.Records;

internal sealed class ClaimRecord
{
    public int Id { get; set; }
    public DateOnly ClaimDate { get; set; }
    public string? Description { get; set; }
}
