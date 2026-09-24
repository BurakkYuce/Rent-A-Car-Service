using System.Text.RegularExpressions;
using RentACar.Application.Authorization;
using RentACar.Web.Identity;

namespace RentACar.IntegrationTests;

/// <summary>
/// <b>Yapısal kilit:</b> grup kapısından DAHA DAR bir izin isteyen ("dar uç") her POST ucunu
/// tetikleyen ekran, o dar izinle kapılanmış olmalı.
///
/// <para><b>Neden var (canlı hata, 2026-08-26):</b> "yeni kira oluştururken durduk yere ana ekrana
/// atıyor" şikayetinin kaynaklarından biri. <c>/kiralar/cancel</c> ucu <c>OperationsDelete</c>
/// istiyor ama düğmesi <c>OperationsWrite</c>'a bakıyordu; Operatör'de OperationsDelete olmadığı
/// için düğme AÇIK görünüyor, basınca 403 alınıyordu. Ölçüm: bu desendeki 13 ucun <b>13'ünde de</b>
/// ekran yanlış kapıdaydı — yani tek tek düzeltmek yetmez, kalıcı çit gerekir.</para>
///
/// <para><b>Neden kaynak taraması (çalışma zamanı değil):</b> uçları <c>EndpointDataSource</c>'tan
/// okumak <c>RequestDelegate</c> üretimini tetikliyor; bu da tüm iş servislerinin DI'da kayıtlı
/// olmasını şart koşuyor (aksi halde servis parametreleri "JSON gövde" sanılıp form yükleyen uçlar
/// patlıyor). Testi tüm uygulama DI'sına bağlamak kırılganlık getirirdi. Repo zaten kaynak-tarama
/// çitleri kullanıyor (<c>MenuKapsamaTests</c>).</para>
///
/// <para><b>Kapsam ve bilinçli sınırları:</b> eşleştirme DOSYA seviyesindedir — bir razor dosyası
/// doğru policy'yi başka bir öğe için kullanıyorsa test yanlış-yeşil verebilir. Kabul edilen zayıflık;
/// amaç "bu ekran o izni hiç duymamış" durumunu yakalamak. Rotası hiçbir razor'da geçmeyen uçlar
/// (JS fetch / API yolları) cezalandırılmaz.</para>
/// </summary>
public sealed class UcIzinKapsamaTests
{
    private static string RepoKok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    private sealed record DarUc(string Rota, Permission Etkin, Permission Grup, string Dosya);

    /// <summary>
    /// <c>var grp = app.MapGroup("/x").RequirePermission(Permission.A);</c> ve
    /// <c>grp.MapPost("/y", …).RequirePermission(Permission.B);</c> desenlerini eşleştirir.
    /// B ≠ A ise uç "dar"dır.
    /// </summary>
    private static List<DarUc> DarUclar(string kok)
    {
        var grupRe = new Regex(
            @"var\s+(?<degisken>\w+)\s*=\s*[\w\.]+\s*\.MapGroup\(\s*""(?<onek>[^""]+)""\s*\)(?<kuyruk>[^;]*);",
            RegexOptions.Singleline);
        // Uç BAŞLANGIÇLARI. Tek bir regex'le "başlangıç → RequirePermission" aralığı yakalamak
        // ÇALIŞMAZ: .NET eşleşmeleri örtüşmez, uzun bir aralık aradaki uçları yutar ve onlar sessizce
        // taranmamış kalır (ilk denemede 13 dar uçtan yalnız 3'ü bulundu). Bunun yerine her uç,
        // BİR SONRAKİ uç başlangıcına kadarki pencerede incelenir.
        var baslangicRe = new Regex(@"(?<degisken>\w+)\s*\.Map(Post|Get)\(\s*""(?<alt>[^""]*)""");
        var izinRe = new Regex(@"\.RequirePermission\(Permission\.(?<izin>\w+)\)");

        var sonuc = new List<DarUc>();
        foreach (var dosya in Directory.EnumerateFiles(
                     Path.Combine(kok, "src/RentACar.Web"), "*Endpoints.cs", SearchOption.AllDirectories))
        {
            var metin = File.ReadAllText(dosya);

            var gruplar = new Dictionary<string, (string Onek, Permission? Izin)>(StringComparer.Ordinal);
            foreach (Match g in grupRe.Matches(metin))
            {
                var gi = izinRe.Match(g.Groups["kuyruk"].Value);
                gruplar[g.Groups["degisken"].Value] =
                    (g.Groups["onek"].Value, gi.Success ? Enum.Parse<Permission>(gi.Groups["izin"].Value) : null);
            }

            var baslangiclar = baslangicRe.Matches(metin);
            for (var i = 0; i < baslangiclar.Count; i++)
            {
                var m = baslangiclar[i];
                var pencereSonu = i + 1 < baslangiclar.Count ? baslangiclar[i + 1].Index : metin.Length;
                var pencere = metin[m.Index..pencereSonu];

                var ui = izinRe.Match(pencere);
                if (!ui.Success) continue;                                   // uç kendi izni yok → grup izni geçerli
                if (!gruplar.TryGetValue(m.Groups["degisken"].Value, out var grp) || grp.Izin is null) continue;

                var etkin = Enum.Parse<Permission>(ui.Groups["izin"].Value);
                if (etkin == grp.Izin) continue;                             // dar değil

                var rota = (grp.Onek + m.Groups["alt"].Value).Replace("//", "/");
                sonuc.Add(new DarUc(rota, etkin, grp.Izin.Value, Path.GetRelativePath(kok, dosya)));
            }
        }
        return sonuc;
    }

    /// <summary>
    /// Kesişi yapılmış fazlarda (F4, F5, F6, F7, F10, F9, F8) silinecek Blazor POST uçları (docs/roadmap/F?.md envanteri, "F? (bu faz)"
    /// satırları). Silme PR'ları (F4.6b, F5 silmesi) bu uçları (ör. <c>/kiralar/cancel</c>, <c>/rezervasyonlar/cancel</c>)
    /// sildiğinde tarama çiti boşa düşmesin diye ön koşul onları SAYMAZ.
    /// </summary>
    private static HashSet<string> KesisteSilinecekUclar(string kok, string faz)
        => Regex.Matches(File.ReadAllText(Path.Combine(kok, $"docs/roadmap/{faz}.md")),
                $@"^\|\s*`(?<uc>/[^`]+)`\s*\|[^|]*\|\s*{faz} \(bu faz\)\s*\|", RegexOptions.Multiline)
            .Select(m => m.Groups["uc"].Value).ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void Tarama_calisiyor()
    {
        // Kendi kendini doğrulayan ön koşul: tarama bozulursa aşağıdaki kilit SESSİZCE boş küme
        // üzerinde çalışır ve sonsuza dek yeşil kalırdı.
        // F4.6 devri: çapa artık TEK rota (/kiralar/cancel — F4.6b'de silinir) değil; F4 kesişinden SONRA da
        // yaşayacak dar uçlar sayılır ve üç dar-izin türünün her biri en az bir kez aranır. /kiralar/cancel'in
        // (canlı hatanın kaynağı) SPA karşılığı UiDugmeIzinTests'te (kiraIptal: OperationsWrite + OperationsDelete).
        var kok = RepoKok();
        var silinecek = KesisteSilinecekUclar(kok, "F4");
        Assert.Contains("/kiralar/cancel", silinecek); // envanter ayrıştırması çalışıyor
        Assert.True(silinecek.Count >= 15, $"F4 envanteri şüpheli: {silinecek.Count} uç.");
        // F5.4 devri: F5 envanterinin 18 ucu (dar olanlar /rezervasyonlar/cancel, /filo-kiralama/iptal) da sayılmaz;
        // SPA karşılıkları sunucunun `yetkiler` bayrakları + UiRezervasyonTests.Izin_haritasi_Blazor_ile_ayni.
        var f5 = KesisteSilinecekUclar(kok, "F5");
        Assert.Equal(18, f5.Count);
        Assert.Contains("/rezervasyonlar/cancel", f5);
        silinecek.UnionWith(f5);
        // F6.4 devri: F6 envanterinin 43 ucu (/servisler/create F9'a kalır) da sayılmaz; dar olanların
        // (/arac-kredi/create|taksit-ode|taksit-iptal|iptal, /musteri-taksit/*) SPA karşılıkları sunucunun `yetkiler`
        // bayrakları + UiAracFinansTests / UiAracTests izin testleri.
        var f6 = KesisteSilinecekUclar(kok, "F6");
        Assert.Equal(43, f6.Count);
        Assert.Contains("/arac-kredi/create", f6);
        Assert.DoesNotContain("/servisler/create", f6);
        silinecek.UnionWith(f6);
        // F7.3 devri: F7 envanterinin 15 ucu (/cariler, /anketler, /sikayetler, /assistans, /hukuk × create|update|delete)
        // da sayılmaz; SPA karşılıkları /api/ui/v1 cari/CRM uçları (UiCustomerApiTests izin testleri).
        var f7 = KesisteSilinecekUclar(kok, "F7");
        Assert.Equal(15, f7.Count);
        Assert.Contains("/cariler/delete", f7);
        silinecek.UnionWith(f7);
        // F10.3 devri: F10 envanterinin 3 ucu (/raporlar/personel-calisma/create|update|delete) da sayılmaz. Üçü de
        // grup izniyle (OperationsWrite) — dar değil, taban değişmez; SPA karşılığı /api/ui/v1/vardiyalar (UiShiftTests).
        var f10 = KesisteSilinecekUclar(kok, "F10");
        Assert.Equal(3, f10.Count);
        Assert.Contains("/raporlar/personel-calisma/create", f10);
        silinecek.UnionWith(f10);
        // F9.3 devri: F9 envanterinin 46 ucu (servis, sigorta/MTV/muayene ödeme ve kayıt, zeyil, servis yansıtma, fiyat/tarife
        // tanımları, tarife aktar, maliyet teklifi; /servisler/create F6'dan buraya kalmıştı) da sayılmaz. Dar olanların
        // (/servisler/kalem, /regulasyon/muayene …) SPA karşılıkları sunucunun `yetkiler` bayrakları + UiServiceInsurance /
        // UiPricing izin testleri (#292).
        var f9 = KesisteSilinecekUclar(kok, "F9");
        Assert.Equal(46, f9.Count);
        Assert.Contains("/servisler/create", f9);
        Assert.Contains("/servisler/kalem", f9);
        silinecek.UnionWith(f9);
        // F8.3 devri: F8 envanterinin 36 ucu (/finans/*, /cezalar/*, /depozito/*, /gelen-efatura/*, /giderler/*,
        // /kurlar/*, /donem-kapanis/*, /satislar/create) da sayılmaz; SPA karşılıkları /api/ui/v1 finans uçları
        // (F8.1 izin testleri) ve ekranlardaki izin kapılı düğmeler (#299, #300).
        var f8 = KesisteSilinecekUclar(kok, "F8");
        Assert.Equal(36, f8.Count);
        Assert.Contains("/finans/tahsilat/ters", f8);
        silinecek.UnionWith(f8);

        var kalici = DarUclar(kok).Where(u => !silinecek.Contains(u.Rota)).ToList();
        // Taban F6.4'te 10 → 8: F6'nın dar uçları (kredi/müşteri taksit) artık silinecek kümede; bugün 9 kalıcı dar uç
        // var. Üç dar-izin türünün her biri aşağıda ayrıca aranır, bu yüzden taban yalnız kaba bir çittir.
        // F9.3'te 8 → 5: F9'un dört dar ucu silinecek kümeye geçti.
        // F8.3'te 5 → 2: F8'in dar uçları (fatura iade, tahsilat ters — FinanceReverse; ceza iptal …) da silinecek
        // kümede (SPA karşılıklarında düğmeler dar izinle gizli, #300).
        // F9.3 + F8.3 birlikte: bugün 2 kalıcı dar uç var (/kiralar/ornek-sozlesme/pdf FinanceWrite ⊂ OperationsWrite,
        // /listeler/export/{liste} ManageUsers ⊂ ViewReports).
        Assert.True(kalici.Count >= 2, $"Beklenenden az dar uç bulundu ({kalici.Count}) — tarama bozulmuş olabilir.");
        Assert.Contains(kalici, u => u.Etkin == Permission.FinanceWrite && u.Grup == Permission.OperationsWrite);
        // Kalıcı OperationsDelete ve FinanceReverse ucu kalmadı (hepsi F8/F9 envanterinde: /servisler/iptal, /cezalar/iptal,
        // /finans/fatura-iade, /finans/tahsilat/ters …). Taramanın bu türleri hâlâ tanıdığı, uçlar yaşadıkça silinecek
        // kümede aranır; SPA karşılıkları düğmelerin dar izin kapısı (#292, #299, #300). F8/F9 Blazor POST silme PR'ları
        // bu iki satırı kaldırır.
        var all = DarUclar(kok);
        Assert.Contains(all, u => u.Etkin == Permission.OperationsDelete && u.Grup == Permission.OperationsWrite && silinecek.Contains(u.Rota));
        Assert.Contains(all, u => u.Etkin == Permission.FinanceReverse && f8.Contains(u.Rota));
    }

    [Fact]
    public void Dar_izin_isteyen_uclarin_ekrani_dogru_kapida()
    {
        var kok = RepoKok();
        var razorlar = Directory
            .EnumerateFiles(Path.Combine(kok, "src/RentACar.Web/Components"), "*.razor", SearchOption.AllDirectories)
            .ToDictionary(f => f, File.ReadAllText);

        var bulgular = new List<string>();
        foreach (var uc in DarUclar(kok))
        {
            var beklenen = $"Policy=\"{AuthExtensions.PolicyName(uc.Etkin)}\"";
            foreach (var (yol, metin) in razorlar)
            {
                if (!metin.Contains($"action=\"{uc.Rota}\"", StringComparison.Ordinal)) continue;
                if (!metin.Contains(beklenen, StringComparison.Ordinal))
                    bulgular.Add($"{uc.Rota} ({uc.Etkin}, grup: {uc.Grup})  →  {Path.GetRelativePath(kok, yol)}");
            }
        }

        Assert.True(bulgular.Count == 0,
            "Bu uçlar grup kapısından DAHA DAR bir izin istiyor ama tetikleyici ekran o izinle " +
            "kapılanmamış. Kullanıcı düğmeyi AÇIK görür, basar, 403 alır ve /yetkisiz'e düşer — " +
            "yapamayacağı bir işlem ona sunulmuş olur.\n  " + string.Join("\n  ", bulgular));
    }
}
