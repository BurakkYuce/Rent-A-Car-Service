using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace RentACar.IntegrationTests;

/// <summary>
/// <b>Yapısal kilit:</b> geliştirme seed'inin ve platform konsolunun eski SABİT parolaları repoya geri
/// dönmesin. Eski seed parolası sahibinin gerçek bir dış sistemdeki parolasıyla aynıydı ve kodda
/// (<c>DbInitializer</c>, giriş sayfası ön-doldurması), betiklerde ve belgelerde düz metin duruyordu.
/// Artık parola yapılandırmadan gelir ya da çalışma anında üretilir (<see cref="SeedParolasiTests"/>).
///
/// <para><b>Çit kendi koruduğu sırrı yazmaz:</b> yasaklı dizgiler burada düz metin DEĞİL, tuzlu
/// PBKDF2-SHA256 özetleri olarak durur; repodaki biçimi uyan her alt dizgi özetlenip karşılaştırılır.
/// Bu bir gizlilik mekanizması değildir (düşük entropili bir parolanın doğrulayıcısı tahminle kırılabilir
/// ve parola git geçmişinde zaten var — asıl çare dış sistemde parolayı değiştirmek); amaç düz metnin
/// ağaca geri girmesini engellemek.</para>
///
/// <para><b>Kapsam:</b> <c>git ls-files --cached --others --exclude-standard</c> — izlenen dosyalar +
/// henüz eklenmemiş yeni dosyalar (commit'ten ÖNCE yakalar); yerel çıktılar (bin/obj, node_modules,
/// graphify-out, iç içe worktree'ler) taranmaz. Uzantı süzgeci yok: .cs/.md/.json/.razor/.sh/.mjs/.yml…
/// hepsi; yalnız ikili dosyalar atlanır.</para>
/// </summary>
public sealed class SabitParolaYokTests
{
    private const string Tuz = "racar/sabit-parola-citi/v1";
    private const int Tur = 100_000;

    /// <summary>(aday biçimi, PBKDF2 özeti): eski seed parolası ve eski platform dev parolası.</summary>
    private static readonly (Regex Aday, string Ozet)[] Yasaklar =
    [
        (new Regex("(?=([a-z]{4}[0-9]{4}))", RegexOptions.CultureInvariant),
            "b61e09888938f460dbc6afff9b5ad860ca71a8d4f945649d1f306fd70f83ed74"),
        (new Regex("(?=([a-z]{8}[0-9]{4}))", RegexOptions.CultureInvariant),
            "345593b398d60171a3484d43026549b1325b6081a1d32a13a1392c3099ca1c42"),
    ];

    private static readonly HashSet<string> IkiliUzantilar = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".ico", ".bmp", ".pdf", ".woff", ".woff2", ".ttf", ".otf",
        ".eot", ".zip", ".gz", ".tgz", ".xlsx", ".xls", ".docx", ".dll", ".exe", ".pdb", ".bundle", ".mp4",
        ".webm", ".snk", ".pfx", ".p12",
    };

    [Fact]
    public void Repoda_eski_sabit_seed_ve_platform_parolalari_yok()
    {
        var kok = RepoKok();
        var dosyalar = RepoDosyalari(kok).ToList();
        Assert.True(dosyalar.Count > 500, $"tarama kapsamı şüpheli dar: {dosyalar.Count} dosya");

        var bulgular = Tara(dosyalar.Select(d => (Path.GetRelativePath(kok, d), (Func<string>)(() => File.ReadAllText(d)))), Yasaklar);

        Assert.True(bulgular.Count == 0,
            "Eski sabit parola repoya geri girmiş — yapılandırmadan (Seed:Parola / Platform:AdminPasswordHash) "
            + "ya da ortam değişkeninden okuyun:\n" + string.Join("\n", bulgular));
    }

    /// <summary>Çitin kendisi çalışıyor mu: sentetik bir yasakla (gerçek parolayı yazmadan) mekanizmayı dener —
    /// büyük/küçük harf, sözcük içinde geçme ve biçimi uymayan komşu dizgiler.</summary>
    [Fact]
    public void Cit_ozeti_tutan_diziyi_buyuk_kucuk_harf_ve_sozcuk_icinde_de_yakalar()
    {
        (Regex, string)[] sentetik = [(new Regex("(?=([a-z]{4}[0-9]{4}))"), Ozet("qxzw9071"))];

        var bulgular = Tara(
        [
            ("temiz.md", () => "qxzw907 qxz9071 qxzw 9071 wxzq9071"),
            ("gomulu.cs", () => "var s = \"abcQXZW90712\";"),
            ("yalin.sh", () => "sifre=qxzw9071"),
        ], sentetik);

        Assert.Equal(new[] { "gomulu.cs", "yalin.sh" }, bulgular.Select(b => b.Split(':')[0]).Order().ToList());
    }

    private static List<string> Tara(IEnumerable<(string Ad, Func<string> Oku)> dosyalar, (Regex Aday, string Ozet)[] yasaklar)
    {
        // aday dizgi → geçtiği dosyalar (yasak başına). Özet pahalı → her farklı aday bir kez özetlenir.
        var adaylar = yasaklar.Select(_ => new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)).ToArray();
        foreach (var (ad, oku) in dosyalar)
        {
            var metin = oku().ToLowerInvariant();
            for (var i = 0; i < yasaklar.Length; i++)
                foreach (Match m in yasaklar[i].Aday.Matches(metin))
                {
                    var aday = m.Groups[1].Value;
                    if (!adaylar[i].TryGetValue(aday, out var yerler)) adaylar[i][aday] = yerler = [];
                    yerler.Add(ad);
                }
        }

        return adaylar
            .SelectMany((d, i) => d.Select(kv => (i, kv.Key, kv.Value)))
            .AsParallel()
            .Where(x => CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(Ozet(x.Key)), Convert.FromHexString(yasaklar[x.i].Ozet)))
            .SelectMany(x => x.Value.Select(dosya => $"{dosya}: yasaklı parola #{x.i + 1}"))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static string Ozet(string aday) => Convert.ToHexStringLower(Rfc2898DeriveBytes.Pbkdf2(
        Encoding.UTF8.GetBytes(aday), Encoding.UTF8.GetBytes(Tuz), Tur, HashAlgorithmName.SHA256, 32));

    private static IEnumerable<string> RepoDosyalari(string kok)
    {
        var psi = new ProcessStartInfo("git", "ls-files -z --cached --others --exclude-standard")
        {
            WorkingDirectory = kok, RedirectStandardOutput = true, UseShellExecute = false,
        };
        using var p = Process.Start(psi)!;
        var cikti = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, $"git ls-files başarısız (çıkış {p.ExitCode})");

        return cikti.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(r => Path.Combine(kok, r))
            .Where(d => !IkiliUzantilar.Contains(Path.GetExtension(d)))
            .Where(File.Exists) // index'te olup diskte silinmiş dosya
            .ToList();
    }

    private static string RepoKok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }
}
