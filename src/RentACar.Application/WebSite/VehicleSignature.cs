using RentACar.Application.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.WebSite;

/// <summary>
/// PR-13 — "aynı araç" tanımı. Sihirbazın "aynı araçları beraber göster" anahtarı ve sonradan alınan
/// aracın hangi ilana ait olduğu HEP bu imzadan hesaplanır.
///
/// <para><b>Anahtar:</b> <c>Normalize(Marka) | Normalize(Tip) | Vites | Yakit</c></para>
///
/// <para><b>Neden <see cref="TurkishText.Normalize"/>, <c>ToLowerInvariant</c> DEĞİL:</b> ERP araç
/// listesindeki mevcut gruplama (`VehicleList.razor`) `ToLowerInvariant()` kullanıyor ve bu Türkçe'de
/// YANLIŞ — "FIAT" ile "Fıat" ayrı gruplara düşer, "beraber göster" bazı araçları SESSİZCE dışarıda
/// bırakırdı. Repo bu sınıf hata için `TurkishText`'i yazdı (PR-4.5).</para>
///
/// <para><b>Neden Vites ve Yakit VAR:</b> kullanıcının kendi örneği "Fiat Egea <b>Manuel Dizel</b>" —
/// otomatik ve manuel Egea aynı kart olmamalı (farklı fiyat, farklı müşteri beklentisi).</para>
///
/// <para><b>Neden ModelYili YOK:</b> 2022 ve 2023 Egea'yı ayırmak kart enflasyonu üretir; yıl farkı
/// başlıkta aralık olarak gösterilir ("2022–2023").</para>
/// </summary>
public static class VehicleSignature
{
    public static string Calculate(Vehicle v)
        => string.Join('|',
            TurkishText.Normalize(v.Marka ?? string.Empty),
            TurkishText.Normalize(v.Tip ?? string.Empty),
            v.Vites?.ToString() ?? "-",
            // PR-21: yakıt artık nullable. İmzada "-" ile temsil edilir → yakıtı GİRİLMEMİŞ araçlar
            // kendi aralarında gruplanır, yakıtı girilmiş olanlarla KARIŞMAZ (aksi halde "girilmedi"
            // olanlar sessizce Benzin grubuna düşerdi).
            v.Yakit?.ToString() ?? "-");

    /// <summary>Kart başlığı: "Fiat Egea Manuel Dizel". Marka/Tip boşsa plakaya düşer (ilan başlıksız kalmasın).</summary>
    public static string Header(IReadOnlyList<Vehicle> vehicles)
    {
        var first = vehicles[0];
        var parts = new[]
        {
            (first.Marka ?? string.Empty).Trim(),
            (first.Tip ?? string.Empty).Trim(),
            first.Vites?.ToString() ?? string.Empty,
            // Girilmemiş yakıt başlığa YAZILMAZ: "Fiat Egea Manuel" doğru, "Fiat Egea Manuel Benzin"
            // uydurma olurdu.
            first.Yakit?.ToString() ?? string.Empty,
        }.Where(p => p.Length > 0);

        var title = string.Join(' ', parts).Trim();
        return title.Length > 0 ? title : first.Plaka;
    }

    /// <summary>Model yılı aralığı: tek yıl → "2023", farklıysa → "2022–2023", hiç yoksa null.
    /// (`VehicleList.razor`'daki mevcut mantığın aynısı.)</summary>
    public static string? YearRange(IReadOnlyList<Vehicle> vehicles)
    {
        var years = vehicles.Where(v => v.ModelYili is > 0).Select(v => v.ModelYili!.Value).ToList();
        if (years.Count == 0) return null;
        var min = years.Min();
        var max = years.Max();
        return min == max ? min.ToString() : $"{min}–{max}";
    }
}
