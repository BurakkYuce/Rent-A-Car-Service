using RentACar.Domain.Validation;

namespace RentACar.IntegrationTests;

/// <summary>
/// Saf birim testleri (DB yok). Bağımsız oracle: beklenen değerler resmî TC Kimlik
/// algoritmasından elle doğrulanmıştır (kodun kendi hesabından DEĞİL).
///   10000000146, 12345678950 → resmî algoritmaya göre geçerli.
/// </summary>
public sealed class TurkishIdentityTests
{
    [Theory]
    [InlineData("10000000146")]
    [InlineData("12345678950")]
    public void Valid_tckn_passes(string tckn)
        => Assert.True(TurkishIdentity.IsValidTcKimlik(tckn));

    [Theory]
    [InlineData("11111111111")]   // checksum tutmaz
    [InlineData("12345678901")]   // d10 yanlış
    [InlineData("00000000000")]   // d1 = 0
    [InlineData("01234567890")]   // d1 = 0
    [InlineData("1000000014")]    // 10 hane (kısa)
    [InlineData("100000001466")]  // 12 hane (uzun)
    [InlineData("1000000014A")]   // rakam değil
    [InlineData("")]              // boş
    [InlineData(null)]            // null
    public void Invalid_tckn_fails(string? tckn)
        => Assert.False(TurkishIdentity.IsValidTcKimlik(tckn));

    [Theory]
    [InlineData("1234567890")]    // 10 hane
    [InlineData("0000000000")]
    public void Valid_vergino_format_passes(string vkn)
        => Assert.True(TurkishIdentity.IsValidVergiNoFormat(vkn));

    [Theory]
    [InlineData("123456789")]     // 9 hane
    [InlineData("12345678901")]   // 11 hane
    [InlineData("12345678AB")]    // rakam değil
    [InlineData("")]
    [InlineData(null)]
    public void Invalid_vergino_format_fails(string? vkn)
        => Assert.False(TurkishIdentity.IsValidVergiNoFormat(vkn));
}
