namespace AzKotle.Domain.Tests;

internal sealed class FakeTimeProvider(DateTimeOffset fixedNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => fixedNow;
}
