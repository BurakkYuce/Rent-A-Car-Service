using RentACar.Application.EInvoicing;
using RentACar.Application.Integrations;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// e-Belge saf kuralları (DB'siz): ayar hazırlığı, ilk durum, maskeleme ve faturadan UBL girdisi. Beklenen cümleler ve
/// değerler elle yazıldı.
/// </summary>
public sealed class EDocumentPolicyTests
{
    private static TenantSettings Ready() => new()
    {
        FirmaUnvan = "Deneme A.Ş.", FirmaVergiNo = "1234567890", FirmaIl = "İstanbul", FirmaIlce = "Kadıköy",
        EFaturaEntegrator = FakeEInvoiceIntegrator.TestKey, EFaturaKullanici = "k", EFaturaSifreEnc = "cipher",
    };

    public static TheoryData<string, string?> Gaps => new()
    {
        { "hazir", null },
        { "entegrator-yok", "entegratör seçilmedi" },
        { "kullanici-yok", "entegratör kullanıcı adı girilmedi" },
        { "sifre-yok", "entegratör şifresi / API anahtarı girilmedi" },
        { "unvan-yok", "firma unvanı girilmedi" },
        { "vkn-kisa", "firma vergi kimlik numarası (10 hane) girilmedi" },
        { "sahis", "şahıs firması (11 haneli satıcı kimliği) henüz desteklenmiyor — yetkili ad/soyad alanı yok" },
        { "il-yok", "firma il/ilçe girilmedi" },
    };

    [Theory]
    [MemberData(nameof(Gaps))]
    public void Readiness_reports_the_first_missing_setting(string scenario, string? expected)
    {
        var s = Ready();
        switch (scenario)
        {
            case "entegrator-yok": s.EFaturaEntegrator = " "; break;
            case "kullanici-yok": s.EFaturaKullanici = null; break;
            case "sifre-yok": s.EFaturaSifreEnc = null; break;
            case "unvan-yok": s.FirmaUnvan = ""; break;
            case "vkn-kisa": s.FirmaVergiNo = "12345"; break;
            case "sahis": s.FirmaVergiNo = TestNationalId.New(); break;
            case "il-yok": s.FirmaIlce = null; break;
        }
        Assert.Equal(expected, EInvoiceReadiness.Gap(s));
        Assert.Equal("entegratör seçilmedi", EInvoiceReadiness.Gap(null));
    }

    [Fact]
    public void Catalog_check_detects_missing_adapter_and_rejects_bad_or_duplicate_keys()
    {
        Assert.Equal("seçili entegratörün adaptörü bu sürümde yok", EInvoiceReadiness.Gap(Ready(), new EInvoiceIntegratorCatalog([])));
        Assert.Null(EInvoiceReadiness.Gap(Ready(), new EInvoiceIntegratorCatalog([new FakeEInvoiceIntegrator()])));
        Assert.Throws<InvalidOperationException>(() => new EInvoiceIntegratorCatalog([new FakeEInvoiceIntegrator(), new FakeEInvoiceIntegrator()]));
    }

    [Fact]
    public void New_invoice_is_queued_only_when_ready_and_refund_never()
    {
        var now = DateTimeOffset.UtcNow;
        var invoice = new Invoice { TenantId = Guid.NewGuid() };
        var queued = EDocumentPolicy.ForNewInvoice(invoice, Ready(), now);
        Assert.Equal((EDocumentStatus.Gonderilecek, (string?)null, invoice.Id, invoice.TenantId), (queued.Durum, queued.SonMesaj, queued.InvoiceId, queued.TenantId));
        Assert.NotEqual(Guid.Empty, queued.Ettn);
        Assert.NotEqual(queued.Ettn, EDocumentPolicy.ForNewInvoice(invoice, Ready(), now).Ettn); // her kayıt kendi ETTN'i

        var draft = EDocumentPolicy.ForNewInvoice(invoice, null, now);
        Assert.Equal((EDocumentStatus.Taslak, "e-Fatura gönderilmedi: entegratör bağlı değil (entegratör seçilmedi)."), (draft.Durum, draft.SonMesaj));

        var refund = EDocumentPolicy.ForNewInvoice(new Invoice { IadeMi = true }, Ready(), now);
        Assert.Equal((EDocumentStatus.Taslak, EDocumentPolicy.RefundNotSent), (refund.Durum, refund.SonMesaj));
    }

    [Fact]
    public void Sanitize_masks_known_secrets_and_every_eleven_digit_run_and_caps_length()
    {
        var tckn = TestNationalId.New();
        var other = TestNationalId.New();
        Assert.Equal("Alıcı *********** ve ***********; VKN 1234567890 kalır",
            EDocumentPolicy.Sanitize($"Alıcı {tckn} ve {other}; VKN 1234567890 kalır", tckn));
        Assert.Equal("biçimli: ***********", EDocumentPolicy.Sanitize($"biçimli: {tckn}", null));
        Assert.Null(EDocumentPolicy.Sanitize("  "));
        Assert.Equal(EDocumentPolicy.MessageMax, EDocumentPolicy.Sanitize(new string('x', 2000))!.Length);
        Assert.Equal(TimeSpan.FromMinutes(5), EDocumentPolicy.Backoff(1));
        Assert.Equal(TimeSpan.FromHours(4), EDocumentPolicy.Backoff(9));
    }

    /// <summary>Adversarial L3: entegratör TCKN'yi gruplu yansıtırsa ("123 456 789 01", "123-456-789-01") da maskelenir;
    /// 10 haneli VKN ve tarih gibi kısa diziler kalır.</summary>
    [Fact]
    public void Sanitize_masks_grouped_eleven_digit_runs_but_leaves_shorter_numbers()
    {
        var tckn = TestNationalId.New();
        var spaced = $"{tckn[..3]} {tckn[3..6]} {tckn[6..9]} {tckn[9..]}";
        var dashed = $"{tckn[..3]}-{tckn[3..6]}-{tckn[6..9]}-{tckn[9..]}";
        Assert.Equal("Alıcı kimliği *********** GİB'de bulunamadı",
            EDocumentPolicy.Sanitize($"Alıcı kimliği {spaced} GİB'de bulunamadı", tckn));
        Assert.Equal("kimlik ***********", EDocumentPolicy.Sanitize($"kimlik {dashed}", null));
        Assert.Equal("VKN 123 456 78 90, tarih 2026-10-17 kalır",
            EDocumentPolicy.Sanitize("VKN 123 456 78 90, tarih 2026-10-17 kalır", null));
    }

    /// <summary>Adversarial L1: entegratör çağrılarına ayrılan süre kilit bitişinden 1 dk önce biter; en az 1 sn.</summary>
    [Fact]
    public void Call_budget_ends_a_minute_before_the_lease()
    {
        var clock = new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromMinutes(9), EDocumentPolicy.CallBudget(clock.AddMinutes(10), clock));
        Assert.Equal(TimeSpan.FromSeconds(1), EDocumentPolicy.CallBudget(clock.AddSeconds(30), clock));
        Assert.True(EDocumentPolicy.CallBudget(clock + EDocumentPolicy.Lease, clock) < EDocumentPolicy.Lease);
    }

    [Fact]
    public void Credentials_and_submission_never_print_their_secrets()
    {
        var c = new EInvoiceCredentials("kullanici", "COK-GIZLI-SIFRE", null, null, false);
        Assert.DoesNotContain("COK-GIZLI-SIFRE", c.ToString(), StringComparison.Ordinal);
        var tckn = TestNationalId.New();
        var s = new EInvoiceSubmission(Guid.NewGuid(), "RNT2026000000001", EDocumentKind.EArsiv, "EARSIVFATURA", tckn, null, [1, 2]);
        Assert.DoesNotContain(tckn, s.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(tckn, new BuyerIdentity(tckn, true).ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(tckn, new UblParty(tckn, null, "A", "B", null, null, "İlçe", "İl", null).ToString(), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ faturadan UBL girdisi

    private static Invoice Invoice(decimal net = 1000m, decimal vat = 200m, Action<Invoice>? tweak = null)
    {
        var at = DateTimeOffset.UtcNow.AddMinutes(-1);
        var i = new Invoice
        {
            No = "RNT" + at.Year + "000000042", CariId = Guid.NewGuid(), Tarih = at,
            NetTutar = net, KdvTutar = vat, GenelToplam = net + vat, Currency = "TRY", Kur = 1m,
        };
        i.Lines.Add(new InvoiceLine { Aciklama = "Kira", Miktar = 1m, BirimNetFiyat = net, KdvOrani = 0.20m, SatirNet = net, SatirKdv = vat, SatirToplam = net + vat });
        tweak?.Invoke(i);
        return i;
    }

    private static Customer Individual() => new()
    {
        Tip = CustomerType.Bireysel, Ad = "Ayşe", Soyad = "Yılmaz", Adres = "Ev adresi", Il = "İzmir", Ilce = "Konak",
        FaturaAdresFarkli = true, FaturaAdresi = "Fatura adresi",
    };

    private static UblInvoiceInput Create(Invoice i, Customer c, string? tckn, UblProfile p = UblProfile.EArsivFatura)
        => EDocumentInputFactory.Create(new EDocumentSource(i, c, tckn, Ready(), Guid.NewGuid()), p, TimeZoneInfo.Utc);

    [Fact]
    public void Factory_takes_amounts_as_is_uses_invoice_address_and_decrypted_tckn()
    {
        var tckn = TestNationalId.New();
        var input = Create(Invoice(), Individual(), tckn);
        Assert.Equal((tckn, "Ayşe", "Yılmaz", "Fatura adresi"), (input.Alici.VknTckn, input.Alici.Ad, input.Alici.Soyad, input.Alici.Adres));
        var line = Assert.Single(input.Satirlar);
        Assert.Equal((1000m, 200m, 0.20m), (line.Tutar, line.KdvTutar, line.KdvOrani));
        Assert.Equal(("1234567890", "Deneme A.Ş.", "İstanbul", "Kadıköy"), (input.Satici.VknTckn, input.Satici.Unvan, input.Satici.Il, input.Satici.Ilce));
        Assert.Equal(UblInvoiceType.Satis, input.Tip);
        Assert.Null(input.Kur);
        Assert.Equal(new UblTotals(1000m, 200m, 1200m, 0m, 1200m), UblInvoiceBuilder.Build(input).Toplamlar);

        var fx = Create(Invoice(tweak: x => { x.Currency = "eur"; x.Kur = 37.5m; }), Individual(), tckn);
        Assert.Equal(("EUR", (decimal?)37.5m), (fx.ParaBirimi, fx.Kur));
    }

    [Fact]
    public void Factory_uses_consumer_placeholder_only_for_e_archive_individuals_without_tckn()
    {
        Assert.Equal(UblInvoiceBuilder.UnknownConsumerTckn, Create(Invoice(), Individual(), null).Alici.VknTckn);
        var ex = Assert.Throws<EDocumentDataException>(() => Create(Invoice(), Individual(), null, UblProfile.TemelFatura));
        Assert.Equal("Alıcının TC kimlik numarası kayıtlı değil ya da geçersiz.", ex.Message);
        var corporate = new Customer { Tip = CustomerType.Kurumsal, Unvan = "X Ltd.", VergiNo = "12", Il = "Ankara", Ilce = "Çankaya" };
        ex = Assert.Throws<EDocumentDataException>(() => Create(Invoice(), corporate, null));
        Assert.Equal("Alıcının vergi numarası (10 hane VKN ya da 11 hane TCKN) kayıtlı değil ya da geçersiz.", ex.Message);
        Assert.Equal(new BuyerIdentity("9876543210", false),
            EDocumentInputFactory.Identify(new Customer { Tip = CustomerType.Kurumsal, VergiNo = "987 654 3210" }, null));
        Assert.Equal(new BuyerIdentity(null, true), EDocumentInputFactory.Identify(Individual(), "0123456789"));
    }

    [Fact]
    public void Factory_refuses_documents_whose_money_would_differ_from_the_ledger()
    {
        var tckn = TestNationalId.New();
        var mismatch = Invoice(tweak: x => x.GenelToplam = 1300m);
        Assert.StartsWith("Fatura satırlarının toplamı", Assert.Throws<EDocumentDataException>(() => Create(mismatch, Individual(), tckn)).Message);
        var withholding = Invoice(tweak: x => { x.TevkifatOran = 20m; x.TevkifatTutar = 40m; });
        Assert.StartsWith("Tevkifatlı fatura", Assert.Throws<EDocumentDataException>(() => Create(withholding, Individual(), tckn)).Message);
        var cancelled = Invoice(tweak: x => x.Durum = InvoiceStatus.Iptal);
        Assert.StartsWith("İptal edilmiş fatura", Assert.Throws<EDocumentDataException>(() => Create(cancelled, Individual(), tckn)).Message);
    }
}
