namespace AzKotle.Domain.Common;

public readonly record struct PasswordResetTokenId(Guid Value)
{
    public static PasswordResetTokenId New() => new(Guid.NewGuid());

    public static PasswordResetTokenId Empty => new(Guid.Empty);

    public override string ToString() => Value.ToString();
}
