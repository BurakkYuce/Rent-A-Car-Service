using RentACar.Domain.Entities;

namespace RentACar.Application.Personnel;

/// <summary>Vardiya yaz modeli (FAZ-45). Saatler TimeOnly — web ucu "HH:mm" metnini çevirir.</summary>
public sealed class VardiyaInput
{
    public Guid PersonelId { get; set; }
    public DateOnly Tarih { get; set; }
    public TimeOnly BaslangicSaat { get; set; }
    public TimeOnly BitisSaat { get; set; }
    public string? Sube { get; set; }
    public string? Aciklama { get; set; }
}

/// <summary>Vardiya listesi filtresi. Tarih aralığı ZORUNLU sayılır (servis varsayılan pencere uydurur).</summary>
public sealed class VardiyaFilter
{
    public DateOnly? Bas { get; set; }
    public DateOnly? Bit { get; set; }
    public Guid? PersonelId { get; set; }
    /// <summary>Şube METNİ ile ek süzme (kullanıcı filtresi) — rol kapsamından AYRI.</summary>
    public string? Sube { get; set; }
}

/// <summary>Vardiya satırı + personel künyesi (PII taşımaz).</summary>
public sealed record VardiyaSatir(PersonelVardiya Vardiya, string PersonelAd, string? PersonelKadroSube);

/// <summary>
/// Personel × gün matrisi. <paramref name="Gunler"/> istenen aralığın TAMAMI (vardiyasız günler
/// dahil) — sütun başlıkları veriden türetilseydi boş gün sessizce kaybolur, matris kayardı.
/// </summary>
public sealed record VardiyaMatris(
    IReadOnlyList<DateOnly> Gunler,
    IReadOnlyList<VardiyaMatrisSatir> Satirlar,
    int ToplamVardiya,
    int ToplamDk);

/// <summary>Bir personelin satırı: gün → o güne ait vardiyalar (bölünmüş mesai birden çok olabilir).</summary>
public sealed record VardiyaMatrisSatir(
    Guid PersonelId,
    string PersonelAd,
    IReadOnlyDictionary<DateOnly, IReadOnlyList<PersonelVardiya>> Gunler,
    int ToplamDk)
{
    /// <summary>Toplam süre "s sa d dk" biçiminde (rapor sunumu tek yerde).</summary>
    public string ToplamSaatMetni => VardiyaBicim.SaatMetni(ToplamDk);
}

/// <summary>Vardiya sunum biçimleri — sayfa ve export AYNI metni üretsin diye tek yerde.</summary>
public static class VardiyaBicim
{
    public static string SaatMetni(int dk) => dk <= 0 ? "—" : $"{dk / 60} sa {dk % 60:00} dk";

    /// <summary>"08:00-18:00" ya da gece vardiyasında "22:00-06:00 (+1)".</summary>
    public static string Aralik(PersonelVardiya v)
        => v.BitisSaat <= v.BaslangicSaat
            ? $"{v.BaslangicSaat:HH\\:mm}-{v.BitisSaat:HH\\:mm} (+1)"
            : $"{v.BaslangicSaat:HH\\:mm}-{v.BitisSaat:HH\\:mm}";
}
