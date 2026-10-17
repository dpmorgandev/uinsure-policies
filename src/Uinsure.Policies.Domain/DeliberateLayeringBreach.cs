using System;

namespace Uinsure.Policies.Domain;

public static class DeliberateLayeringBreach
{
    public static DateOnly Today(TimeProvider clock) => throw new NotImplementedException();
}
