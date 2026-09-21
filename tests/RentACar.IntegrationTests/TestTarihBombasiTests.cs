using System.Text.RegularExpressions;

namespace RentACar.IntegrationTests;

/// <summary>
/// Rezervasyon/teklif açan testlerde SABİT TARİH yasak — "tarih saatli bombası" çiti.
///
/// <para><b>Neden var (2026-09-21):</b> <c>TarihPolitikasi.RezervasyonBaslangic</c> başlangıcı "dünden eski"
/// ya da "1 yıldan ileri" ise reddeder. 2026-09-01'e sabitlenmiş 4 test (<c>RezDuzenlemeTests</c> ×2,
/// <c>AdversarialFinanceApiTests</c> ×2) hiçbir kod değişmeden, yalnız takvim ilerlediği için
/// "Rezervasyon geçmiş tarihe alınamaz." ile kırmızıya döndü ve ilgisiz bir belge PR'ının (#235) CI'ını
/// kırdı. 2026-11-01'e sabitli <c>AcceptanceTests</c> Kasım'da aynı şekilde dönecekti. Hepsi
/// <see cref="Infrastructure.TestZaman.GunSonra"/> ile göreli yapıldı.</para>
///
/// <para><b>Kural:</b> rezervasyon ya da teklif oluşturan (servis, API ya da form ucu) bir test dosyası
/// sabit bir yıl-ay-gün yazamaz. İstisnalar aşağıda dosya dosya, GEREKÇESİYLE listelidir.</para>
/// </summary>
public sealed class TestTarihBombasiTests
{
    /// <summary>Tarihi rezervasyon/teklif politikasından hiç geçirmeyen dosyalar.</summary>
    private static readonly Dictionary<string, string> Istisnalar = new()
    {
        ["RezervasyonFiltreTests.cs"] = "Sabit tarihler yalnız saf AvailabilityService.Pencere fonksiyonuna gider; politika çalışmaz.",
        ["OperasyonelYetkiTests.cs"] = "Sabit tarih yalnız yetki reddi testlerinde; yetki kontrolü tarih kontrolünden önce reddeder.",
    };

    private static readonly Regex Akis = new(
        @"ReservationService|QuotationService|/api/v1/reservations|/api/v1/quotations|rezervasyonlar/(create|update)|teklifler/(create|update)",
        RegexOptions.Compiled);

    private static readonly Regex SabitTarih = new(
        @"new\s+DateTimeOffset\(\s*20\d{2}\s*,|new\(\s*20\d{2}\s*,\s*\d{1,2}\s*,\s*\d{1,2}|DateTimeOffset\.Parse\(\s*""20\d{2}",
        RegexOptions.Compiled);

    private static string TestKok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx"))) d = d.Parent;
        Assert.NotNull(d);
        return Path.Combine(d!.FullName, "tests/RentACar.IntegrationTests");
    }

    private static IEnumerable<(string Ad, string Metin)> AkisDosyalari()
        => Directory.EnumerateFiles(TestKok(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && Path.GetFileName(f) != nameof(TestTarihBombasiTests) + ".cs")
            .Select(f => (Ad: Path.GetFileName(f), Metin: File.ReadAllText(f)))
            .Where(x => Akis.IsMatch(x.Metin));

    [Fact]
    public void Rezervasyon_ve_teklif_testlerinde_sabit_tarih_yok()
    {
        var bulgular = new List<string>();
        foreach (var (ad, metin) in AkisDosyalari())
        {
            if (Istisnalar.ContainsKey(ad)) continue;
            var satirlar = metin.Split('\n');
            for (var i = 0; i < satirlar.Length; i++)
                if (SabitTarih.IsMatch(satirlar[i]))
                    bulgular.Add($"{ad}:{i + 1}  {satirlar[i].Trim()}");
        }

        Assert.True(bulgular.Count == 0,
            "Rezervasyon/teklif açan testte sabit tarih var. TarihPolitikasi rezervasyonu geçmişe kapatır; sabit tarih " +
            "takvim ilerleyince kodu değişmeden kırmızıya döner. TestZaman.GunSonra(n) kullanın ya da (tarih politikadan " +
            "hiç geçmiyorsa) dosyayı gerekçesiyle Istisnalar'a ekleyin.\n  " + string.Join("\n  ", bulgular));
    }

    [Fact]
    public void Tarayici_calisiyor()
    {
        // Akış deseni bozulursa yukarıdaki test "hiç dosya bulamadım" diye sessizce yeşil kalırdı.
        var dosyalar = AkisDosyalari().Select(x => x.Ad).ToList();
        Assert.True(dosyalar.Count >= 10, $"Rezervasyon/teklif açan yalnız {dosyalar.Count} test dosyası bulundu.");
        Assert.Contains("RezDuzenlemeTests.cs", dosyalar);
        Assert.Contains("AcceptanceTests.cs", dosyalar);
        // Sabit tarih deseni de gerçekten eşleşiyor mu (istisna dosyası bilinen bir sabit tarih içeriyor):
        Assert.Contains(AkisDosyalari(), x => x.Ad == "OperasyonelYetkiTests.cs" && SabitTarih.IsMatch(x.Metin));
    }
}
