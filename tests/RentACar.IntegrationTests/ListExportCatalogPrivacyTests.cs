using System.Text.RegularExpressions;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.Legal;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Web.Reports;

namespace RentACar.IntegrationTests;

/// <summary>
/// KVKK çiti — liste export kataloğu (<see cref="ListExportCatalog"/>) anonim cariyi ekrandaki kuralla maskeler. DB yok.
/// (1) Yapısal, SÜTUN bazında: kişisel veri başlığı taşıyan her sütunun hücre ifadesi <see cref="CustomerPrivacy"/>'den
/// geçer ya da (projeksiyon, başlık) gerekçeli muafiyet listesindedir — yeni bir sütun maskesiz eklenemez.
/// (2) Davranış: anonim carinin adı/telefonu/e-postası ve bireysel carinin vergi no'su (TC) hiçbir hücreye geçmez.
/// </summary>
public sealed class ListExportCatalogPrivacyTests
{
    /// <summary>Kişisel veri başlıkları (birebir).</summary>
    private static readonly HashSet<string> PersonalHeaders =
    [
        "Müşteri", "Müşteri Adı", "Cari", "Ünvan/Ad", "Alıcı", "Vergi No", "TC", "TC Kimlik",
        "Telefon", "Cep Tel", "GSM2", "Müşteri Tel", "E-posta", "Mail", "Mail Adresi",
        "Adres", "İl", "İlçe", "Şehir",
    ];

    /// <summary>Kişisel başlıklı ama cari verisi olmayan / ayrı kapıyla korunan sütunlar — gerekçeli.</summary>
    private static readonly Dictionary<(string Projection, string Header), string> Exempt = new()
    {
        [("Locations", "Telefon")] = "Ofis (lokasyon) iletişimi, cari değil.",
        [("Locations", "E-posta")] = "Ofis (lokasyon) iletişimi, cari değil.",
        [("Locations", "Adres")] = "Ofis adresi, cari değil.",
        [("Personnel", "TC Kimlik")] = "Personel (cari değil); uç ManageUsers kapılı + KVKK notu (docs/ops/kvkk-export-notu.md).",
    };

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    [Fact]
    public void Every_personal_data_column_passes_through_the_privacy_rule()
    {
        // Satır yorumları atılır (içlerindeki virgül/tırnak hücre ayrıştırmasını bozmasın).
        var text = Regex.Replace(
            File.ReadAllText(Path.Combine(RepoRoot(), "src/RentACar.Web/Reports/ListExportCatalog.cs")), @"(?m)^\s*//.*$", "");
        var starts = Regex.Matches(text, @"public static ExportTable (\w+)\(").ToList();
        Assert.NotEmpty(starts);
        var violations = new List<string>();
        var guardedColumns = 0;
        for (var i = 0; i < starts.Count; i++)
        {
            // Pencere: bir projeksiyonun başından bir SONRAKİNİN başına (tek regex aralığı uçları yutar — UcIzinKapsama dersi).
            var end = i + 1 < starts.Count ? starts[i + 1].Index : text.Length;
            var window = text[starts[i].Index..end];
            var name = starts[i].Groups[1].Value;

            var headerStart = Regex.Match(window, @"\[\s*""");
            if (!headerStart.Success) continue;
            var headers = Regex.Matches(Block(window, headerStart.Index, '[', ']'), "\"((?:[^\"\\\\]|\\\\.)*)\"")
                .Select(m => m.Groups[1].Value).ToList();
            var personal = headers.Select((h, idx) => (h, idx)).Where(x => PersonalHeaders.Contains(x.h)).ToList();
            if (personal.Count == 0) continue;

            var rowStart = window.IndexOf("new object?[]", StringComparison.Ordinal);
            if (rowStart < 0) { violations.Add($"{name}: hücre dizisi bulunamadı"); continue; }
            var cells = SplitTopLevel(Block(window, window.IndexOf('{', rowStart), '{', '}'));
            if (cells.Count != headers.Count)
            {
                violations.Add($"{name}: {headers.Count} başlık ↔ {cells.Count} hücre (ayrıştırılamadı ya da kaymış)");
                continue;
            }
            foreach (var (header, idx) in personal)
            {
                if (Exempt.ContainsKey((name, header))) continue;
                guardedColumns++;
                if (!cells[idx].Contains("privacy.", StringComparison.Ordinal)
                    && !cells[idx].Contains("CustomerPrivacy.", StringComparison.Ordinal))
                    violations.Add($"{name}.\"{header}\": '{cells[idx].Trim()}' maskeden geçmiyor");
            }
        }
        Assert.Empty(violations);
        Assert.True(guardedColumns >= 15, $"Beklenen en az 15 korumalı sütun, bulunan {guardedColumns} (tarama bozuldu mu?).");
    }

    /// <summary><paramref name="start"/>'taki açılış parantezinden eşleşen kapanışa kadarki iç metin (dize içleri sayılmaz).</summary>
    private static string Block(string s, int start, char open, char close)
    {
        var depth = 0;
        var inString = false;
        for (var i = start; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '"' && s[i - 1] != '\\') inString = !inString;
            if (inString) continue;
            if (c == open) depth++;
            else if (c == close && --depth == 0) return s[(start + 1)..i];
        }
        throw new InvalidOperationException("Kapanmayan parantez.");
    }

    /// <summary>Üst düzey virgüllerden böler (parantez/köşeli/süslü ve dize içi virgüller bölmez).</summary>
    private static List<string> SplitTopLevel(string s)
    {
        var parts = new List<string>();
        var depth = 0;
        var inString = false;
        var last = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '"' && (i == 0 || s[i - 1] != '\\')) inString = !inString;
            if (inString) continue;
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') depth--;
            else if (c == ',' && depth == 0) { parts.Add(s[last..i]); last = i + 1; }
        }
        if (s[last..].Trim().Length > 0) parts.Add(s[last..]);
        return parts;
    }

    [Fact]
    public void Anonymous_customer_never_reaches_a_cell()
    {
        const string real = "Gerçek Ad";
        const string phone = "05321112233";
        var anon = new Customer
        {
            Tip = CustomerType.Bireysel, Ad = "Gerçek", Soyad = "Ad", CepTel = phone, Gsm2 = phone, Email = "g@x.test",
            Adres = "Adres 1", Il = "İl", Ilce = "İlçe", AnonimAd = true, AnonimTelefon = true, AnonimMail = true, AnonimAdres = true,
        };
        var privacy = new CustomerPrivacy(
            new Dictionary<Guid, CustomerPrivacy.Flags> { [anon.Id] = new(true, true, true, true) }, [real]);
        string Name(Guid _) => real;

        var rez = new Reservation { MusteriId = anon.Id, ReservationNo = "RE-1" };
        var hukuk = new HukukDosya { CariId = anon.Id, DosyaNo = "H-1" };
        var cash = new CashTransaction { CariId = anon.Id, No = "TH-1" };
        var tables = new[]
        {
            ListExportCatalog.Customers([anon], privacy),
            ListExportCatalog.Invoices([new Invoice { No = "F-1", CariId = anon.Id }], Name, privacy),
            ListExportCatalog.CashTransactions([new NakitIslemSatirDto(cash, real, "K-1")], privacy),
            ListExportCatalog.Rentals([new RentalRow { SozlesmeNo = "K-1", MusteriId = anon.Id, MusteriAd = real }], privacy),
            ListExportCatalog.Reservations([new ReservationRow(rez, real, phone, "34AA01")], privacy),
            ListExportCatalog.LegalCases([new HukukDosyaSatirDto(hukuk, real, phone)], privacy),
            ListExportCatalog.FleetRentals([new FiloKiralama { No = "FK-1", MusteriId = anon.Id }], _ => "34AA01", Name, privacy),
            ListExportCatalog.VehicleLoans([new AracKredi { No = "KR-1", CariId = anon.Id }], privacy, Name),
            ListExportCatalog.VehicleOrders([new AracSiparis { No = "AS-1", TedarikciCariId = anon.Id }], privacy, Name),
        };
        foreach (var t in tables)
        {
            Assert.All(t.Rows, r => Assert.Equal(t.Headers.Count, r.Length));   // maske hücre kaydırmadı
            var cells = t.Rows.SelectMany(r => r).Select(c => c?.ToString()).ToList();
            Assert.DoesNotContain(real, cells);
            Assert.DoesNotContain(phone, cells);
            Assert.DoesNotContain("g@x.test", cells);
            Assert.Contains(CustomerAnonymity.NameLabel, cells);
        }

        // Kimliksiz ada (bilinmeyen cari) anonim ad kümesiyle maske uygulanır; kimliği bilinen bayraksız cari, ADI anonim
        // bir cariyle aynı olsa bile maskelenmez (ekran kimlikle karar verir — #378 adversarial L1).
        Assert.Equal(CustomerAnonymity.NameLabel, privacy.Name(null, real));
        Assert.Equal(real, privacy.Name(Guid.NewGuid(), real));
        Assert.Equal("Başka Cari", privacy.Name(Guid.NewGuid(), "Başka Cari"));

        // Bireysel carinin vergi no'su TC olabilir → anonim bayrağı olmasa da hiçbir hücreye geçmez (cari kartı kuralı).
        const string tc = "98765432109";
        var individual = new Customer { Tip = CustomerType.Bireysel, Ad = "Açık", Soyad = "Cari", VergiNo = tc };
        Assert.DoesNotContain(tc, ListExportCatalog.Customers([individual], privacy).Rows.SelectMany(r => r).Select(c => c?.ToString()));
    }
}
