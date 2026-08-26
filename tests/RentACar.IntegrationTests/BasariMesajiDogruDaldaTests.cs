using System.Text.RegularExpressions;

namespace RentACar.IntegrationTests;

/// <summary>
/// <c>Sonuc.Tamam</c> (başarı mesajı) yalnızca BAŞARI dalında kullanılmalı.
///
/// <para><b>Neden var (kendi ürettiğim hata):</b> kalan ~79 POST ucunu bildirim şeridine bağlarken
/// dönüşümü betikle yaptım; betik başarı ile başarısızlık dallarını ayırt edemedi ve 7 yeri yanlış
/// çevirdi. En kötüsü: kullanıcı dosya SEÇMEDEN "İçe Aktar"a basınca ekran "Araçlar içe aktarıldı."
/// diyordu — hiçbir şey aktarılmamışken. Bir tanesi de <c>catch</c> bloğunun içindeydi: doğrulama
/// hatası alınca "Kanal silindi." yazıyordu.</para>
///
/// <para>PR-4'ün commit mesajında bu riski ("bir catch bloğunun yanlışlıkla Tamam döndürmesi")
/// yazmıştım ama elle inceleme yerine betiğe güvendim. Bu çit, incelemeyi kalıcı kılar.</para>
///
/// <para><b>Kapsam sınırı:</b> yalnız iki mekanik desen yakalanır — tek satırlık <c>if (…) return
/// Sonuc.Tamam</c> guard'ları ve <c>catch</c> bloğu içindeki <c>Sonuc.Tamam</c>. Çok satırlı bir
/// guard gövdesindeki yanlış kullanım yakalanmaz; amaç betikle üretilen sınıfı kilitlemek.</para>
/// </summary>
public sealed class BasariMesajiDogruDaldaTests
{
    private static string RepoKok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    private static IEnumerable<string> UcDosyalari(string kok)
        => Directory.EnumerateFiles(Path.Combine(kok, "src/RentACar.Web"), "*Endpoints.cs", SearchOption.AllDirectories);

    [Fact]
    public void Guard_dalinda_basari_mesaji_yok()
    {
        // `if (dosya is null) return Sonuc.Tamam(...)` → kullanıcı hiçbir şey yapmadan "başarılı" görür.
        var kok = RepoKok();
        var bulgular = new List<string>();

        foreach (var dosya in UcDosyalari(kok))
            foreach (var (sat, i) in File.ReadLines(dosya).Select((s, i) => (s, i)))
                if (Regex.IsMatch(sat, @"\bif\s*\(.*\)\s*return\s+Sonuc\.Tamam\b"))
                    bulgular.Add($"{Path.GetRelativePath(kok, dosya)}:{i + 1}  {sat.Trim()}");

        Assert.True(bulgular.Count == 0,
            "Tek satırlık guard'da Sonuc.Tamam kullanılmış. Guard'lar erken ÇIKIŞ içindir " +
            "(dosya seçilmedi, kayıt yok...) — işlem YAPILMADIĞI için başarı mesajı yanlıştır; " +
            "Sonuc.Hata kullanın ya da sessizce dönün.\n  " + string.Join("\n  ", bulgular));
    }

    [Fact]
    public void Catch_bloklarinda_basari_mesaji_yok()
    {
        var kok = RepoKok();
        var bulgular = new List<string>();

        foreach (var dosya in UcDosyalari(kok))
        {
            var satirlar = File.ReadAllLines(dosya);
            for (var i = 0; i < satirlar.Length; i++)
            {
                if (!satirlar[i].Contains("Sonuc.Tamam", StringComparison.Ordinal)) continue;

                // Aynı satırda ya da hemen üstündeki 2 satırda `catch (...)` varsa → hata dalı.
                var pencere = string.Join('\n', satirlar.Skip(Math.Max(0, i - 2)).Take(3));
                if (Regex.IsMatch(pencere, @"catch\s*\([^)]*\)"))
                    bulgular.Add($"{Path.GetRelativePath(kok, dosya)}:{i + 1}  {satirlar[i].Trim()}");
            }
        }

        Assert.True(bulgular.Count == 0,
            "catch bloğunda Sonuc.Tamam kullanılmış — işlem BAŞARISIZ olmuşken kullanıcıya başarı " +
            "mesajı gösterilir. Sonuc.Hata kullanın.\n  " + string.Join("\n  ", bulgular));
    }
}
