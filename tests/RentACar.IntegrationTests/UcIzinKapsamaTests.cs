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
    /// F4 kesişinde silinecek Blazor POST uçları (docs/roadmap/F4.md envanteri, "F4 (bu faz)" satırları). F4.6b bu
    /// uçları (ör. <c>/kiralar/cancel</c>) sildiğinde tarama çiti boşa düşmesin diye ön koşul onları SAYMAZ.
    /// </summary>
    private static HashSet<string> F4KesisindeSilinecekUclar(string kok)
        => Regex.Matches(File.ReadAllText(Path.Combine(kok, "docs/roadmap/F4.md")),
                @"^\|\s*`(?<uc>/[^`]+)`\s*\|[^|]*\|\s*F4 \(bu faz\)\s*\|", RegexOptions.Multiline)
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
        var silinecek = F4KesisindeSilinecekUclar(kok);
        Assert.Contains("/kiralar/cancel", silinecek); // envanter ayrıştırması çalışıyor
        Assert.True(silinecek.Count >= 15, $"F4 envanteri şüpheli: {silinecek.Count} uç.");

        var kalici = DarUclar(kok).Where(u => !silinecek.Contains(u.Rota)).ToList();
        Assert.True(kalici.Count >= 10, $"Beklenenden az dar uç bulundu ({kalici.Count}) — tarama bozulmuş olabilir.");
        Assert.Contains(kalici, u => u.Etkin == Permission.OperationsDelete && u.Grup == Permission.OperationsWrite);
        Assert.Contains(kalici, u => u.Etkin == Permission.FinanceWrite && u.Grup == Permission.OperationsWrite);
        Assert.Contains(kalici, u => u.Etkin == Permission.FinanceReverse);
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
