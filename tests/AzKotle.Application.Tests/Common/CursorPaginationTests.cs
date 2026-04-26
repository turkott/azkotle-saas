using AzKotle.Application.Common;
using FluentAssertions;

namespace AzKotle.Application.Tests.Common;

[Trait("Category", "Unit")]
public class CursorPaginationTests
{
    [Fact]
    public void Encode_Then_Decode_RoundTripsBothFields()
    {
        var ts = new DateTime(2026, 04, 26, 12, 00, 00, DateTimeKind.Utc).AddTicks(123456);
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var encoded = CursorPagination.Encode(ts, id);
        var ok = CursorPagination.TryDecode(encoded, out var dt, out var decId);

        ok.Should().BeTrue();
        dt.Should().Be(ts);
        dt.Kind.Should().Be(DateTimeKind.Utc);
        decId.Should().Be(id);
    }

    [Fact]
    public void TryDecode_NullOrEmpty_ReturnsFalse()
    {
        CursorPagination.TryDecode(null, out _, out _).Should().BeFalse();
        CursorPagination.TryDecode("", out _, out _).Should().BeFalse();
        CursorPagination.TryDecode("   ", out _, out _).Should().BeFalse();
    }

    [Fact]
    public void TryDecode_Garbage_ReturnsFalse()
    {
        CursorPagination.TryDecode("not_base64!!!", out _, out _).Should().BeFalse();
    }

    [Fact]
    public void TryDecode_LegacySingleValueCursor_ReturnsFalse()
    {
        // Old format encoded only Ticks (8 bytes). After F10 the wire format
        // requires 24 bytes (ticks + guid). Old cursors must fail-soft so the
        // client falls back to page 1 instead of crashing or paginating wrong.
        var legacy = Convert.ToBase64String(BitConverter.GetBytes(DateTime.UtcNow.Ticks))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        CursorPagination.TryDecode(legacy, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void Encode_IsUrlSafe()
    {
        var encoded = CursorPagination.Encode(DateTime.UtcNow, Guid.NewGuid());

        encoded.Should().NotContain("+").And.NotContain("/").And.NotContain("=");
    }

    [Theory]
    [InlineData(null, CursorPagination.DefaultPageSize)]
    [InlineData(0, CursorPagination.DefaultPageSize)]
    [InlineData(-5, CursorPagination.DefaultPageSize)]
    [InlineData(10, 10)]
    [InlineData(CursorPagination.MaxPageSize, CursorPagination.MaxPageSize)]
    [InlineData(CursorPagination.MaxPageSize + 1, CursorPagination.MaxPageSize)]
    public void ClampPageSize_BoundsAreEnforced(int? requested, int expected)
    {
        CursorPagination.ClampPageSize(requested).Should().Be(expected);
    }
}
