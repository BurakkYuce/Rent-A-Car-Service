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
/// (1) Yapısal: cari adı/iletişimi sütunu taşıyan her projeksiyon <see cref="CustomerPrivacy"/> alır ve kullanır
/// (yeni bir export eklenince maskesiz yazılamaz). (2) Davranış: anonim carinin adı/telefonu hiçbir hücreye geçmez.
/// </summary>
public sealed class ListExportCatalogPrivacyTests
{
    /// <summary>Cari kişisel verisi taşıyan başlıklar (birebir, tırnaklı).</summary>
    private static readonly string[] CustomerHeaders =
        ["\"Müşteri\"", "\"Cari\"", "\"Ünvan/Ad\"", "\"Cep Tel\"", "\"Müşteri Tel\"", "\"Telefon\"", "\"E-posta\"", "\"Mail\"", "\"GSM2\""];

    /// <summary>Başlığı benzeyen ama cari verisi taşımayan projeksiyonlar — gerekçeli.</summary>
    private static readonly Dictionary<string, string> Exempt = new()
    {
        ["Locations"] = "Telefon/E-posta ofis (lokasyon) iletişimidir, cari değil.",
    };

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    [Fact]
    public void Every_projection_with_customer_columns_takes_and_uses_the_privacy_rule()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "src/RentACar.Web/Reports/ListExportCatalog.cs"));
        var starts = Regex.Matches(text, @"public static ExportTable (\w+)\(").ToList();
        Assert.NotEmpty(starts);
        var violations = new List<string>();
        var guarded = 0;
        for (var i = 0; i < starts.Count; i++)
        {
            // Pencere: bir projeksiyonun başından bir SONRAKİNİN başına (tek regex aralığı uçları yutar — UcIzinKapsama dersi).
            var end = i + 1 < starts.Count ? starts[i + 1].Index : text.Length;
            var window = text[starts[i].Index..end];
            var name = starts[i].Groups[1].Value;
            if (!CustomerHeaders.Any(window.Contains) || Exempt.ContainsKey(name)) continue;
            guarded++;
            var arrow = window.IndexOf("=>", StringComparison.Ordinal);
            var signature = arrow > 0 ? window[..arrow] : window;
            if (!signature.Contains("CustomerPrivacy privacy", StringComparison.Ordinal))
                violations.Add($"{name}: imzada CustomerPrivacy yok");
            else if (!window.Contains("privacy.", StringComparison.Ordinal))
                violations.Add($"{name}: CustomerPrivacy alınıyor ama kullanılmıyor");
        }
        Assert.Empty(violations);
        Assert.True(guarded >= 10, $"Beklenen en az 10 korumalı projeksiyon, bulunan {guarded} (tarama bozuldu mu?).");
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

        // Kimliksiz ada da (bilinmeyen cari) anonim ad kümesiyle maske uygulanır; diğer adlar aynen kalır.
        Assert.Equal(CustomerAnonymity.NameLabel, privacy.Name(null, real));
        Assert.Equal("Başka Cari", privacy.Name(Guid.NewGuid(), "Başka Cari"));
    }
}
