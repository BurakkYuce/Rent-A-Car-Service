namespace RentACar.Domain.Validation;

/// <summary>
/// Türkiye'ye özgü kimlik/vergi numarası doğrulayıcıları. SAF + deterministik →
/// bağımsız oracle ile birim-testlenir (beklenen değerler resmî algoritmadan, koddan değil).
/// </summary>
public static class TurkishIdentity
{
    /// <summary>
    /// TC Kimlik No (11 hane) checksum doğrulaması:
    ///  - d1 != 0
    ///  - d10 = ((d1+d3+d5+d7+d9)*7 - (d2+d4+d6+d8)) mod 10
    ///  - d11 = (d1+...+d10) mod 10
    /// </summary>
    public static bool IsValidTcKimlik(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        value = value.Trim();
        if (value.Length != 11) return false;

        var d = new int[11];
        for (var i = 0; i < 11; i++)
        {
            if (value[i] < '0' || value[i] > '9') return false;
            d[i] = value[i] - '0';
        }
        if (d[0] == 0) return false;

        var oddSum = d[0] + d[2] + d[4] + d[6] + d[8];   // 1,3,5,7,9. konum
        var evenSum = d[1] + d[3] + d[5] + d[7];          // 2,4,6,8. konum
        var d10 = ((oddSum * 7) - evenSum) % 10;
        if (d10 < 0) d10 += 10;
        if (d10 != d[9]) return false;

        var sumFirstTen = 0;
        for (var i = 0; i < 10; i++) sumFirstTen += d[i];
        return sumFirstTen % 10 == d[10];
    }

    /// <summary>
    /// Vergi No (VKN) FORMAT doğrulaması: tam 10 hane.
    /// NOT: Tam VKN checksum'u (Maliye algoritması) bilerek eklenmedi — güvenilir
    /// bağımsız test vektörü (canlı parite ya da resmî kaynak) olmadan checksum yazıp
    /// kendi testimle "doğrulamak" projenin bağımsız-oracle ilkesini ihlal eder.
    /// Checksum, doğrulanmış vektörlerle birlikte follow-up'ta eklenecek.
    /// </summary>
    public static bool IsValidVergiNoFormat(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        value = value.Trim();
        if (value.Length != 10) return false;
        foreach (var c in value)
            if (c < '0' || c > '9') return false;
        return true;
    }
}
