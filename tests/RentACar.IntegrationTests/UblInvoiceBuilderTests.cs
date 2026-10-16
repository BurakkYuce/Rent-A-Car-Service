using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using RentACar.Application.EInvoicing;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// UBL-TR 2.1 (e-Fatura paketi 1.2) üreticisi — DB'siz.
///
/// <para><b>BAĞIMSIZ ORACLE (iki katman):</b> (1) beklenen alan değerleri elle kurulmuş faturadan yazıldı
/// (1000 × %20 = 200; 100 × %10 = 10; toplam 1.100 + 210 = 1.310; tevkifat 200 × 2/10 = 40 → ödenecek 1.160) —
/// üretici kodundan türetilmez; (2) çıktı GİB'in yayınladığı UBL-TR 1.2.1 XSD'si VE şematronu ile doğrulanır
/// (<see cref="UblTrValidator"/>; dosyalar ve kaynakları <c>Fixtures/UblTr121</c>). Şematron yorumlayıcısının boş
/// geçmediği ayrıca bozulmuş belgelerle kanıtlanır.</para>
///
/// <para>Tarihler göreli (bugün − 2 gün): GİB şematronu düzenleme tarihinin bugünden ileri olmasını reddeder.</para>
/// </summary>
public sealed class UblInvoiceBuilderTests
{
    private static readonly XNamespace Cbc = UblInvoiceBuilder.Cbc;
    private static readonly XNamespace Cac = UblInvoiceBuilder.Cac;

    private static readonly DateOnly Day = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2));
    private static string No(int order = 1) => "TST" + Day.Year.ToString(CultureInfo.InvariantCulture) + order.ToString("D9", CultureInfo.InvariantCulture);

    private static readonly UblParty Seller = new("1234567890", "Deneme Oto Kiralama A.Ş.", null, null, "Kadıköy",
        "Moda Cad. No:1", "Kadıköy", "İstanbul", null, "02160000000", "info@deneme.example");
    private static readonly UblParty CorporateBuyer = new("9876543210", "Alıcı Lojistik Ltd. Şti.", null, null, "Çankaya",
        "Atatürk Blv. 5", "Çankaya", "Ankara", "Türkiye");
    private static UblParty PersonBuyer(string tckn) => new(tckn, null, "Ayşe", "Yılmaz", null, "Gül Sk. 3", "Konak", "İzmir", null);

    private static readonly UblLine[] TwoRates =
    [
        new("Kira sözleşmesi KS-1", 1m, 1000m, 1000.00m, 0.20m, 200.00m),
        new("Bebek koltuğu (ek hizmet)", 3m, 33.3333m, 100.00m, 0.10m, 10.00m),
    ];

    private static UblInvoiceInput Input(UblProfile profile = UblProfile.TemelFatura, UblInvoiceType type = UblInvoiceType.Satis,
        UblParty? buyer = null, IReadOnlyList<UblLine>? lines = null, string currency = "TRY", decimal? rate = null,
        UblBillingReference? refund = null, string? no = null, Guid? ettn = null)
        => new(ettn ?? Guid.NewGuid(), no ?? No(), Day, new TimeOnly(14, 30, 5), profile, type, currency, rate, Seller,
            buyer ?? CorporateBuyer, lines ?? TwoRates, refund);

    private static XElement Root(UblDocument d) => XDocument.Parse(d.XmlText).Root!;
    private static string V(XElement root, params XName[] path)
    {
        var e = root;
        foreach (var n in path) e = e.Element(n) ?? throw new Xunit.Sdk.XunitException($"Eleman yok: {n.LocalName}");
        return e.Value;
    }

    private static void AssertValid(UblDocument d, string type)
    {
        var xsd = UblTrValidator.ValidateXsd(d.Xml);
        // Tek beklenen uyarı: imza yer tutucusunun şeması yok (xsd:any processContents="lax" — hata değil).
        Assert.True(xsd.All(e => e.StartsWith("Warning", StringComparison.Ordinal) && e.Contains("imza-yer-tutucu", StringComparison.Ordinal)),
            "XSD: " + string.Join(" | ", xsd));
        var sch = UblTrValidator.ValidateSchematron(d.Xml, type);
        Assert.True(sch.Count == 0, "Şematron: " + string.Join(" | ", sch));
    }

    [Fact]
    public void Basic_invoice_two_vat_rates_matches_hand_built_values_and_gib_schema()
    {
        var ettn = Guid.NewGuid();
        var doc = UblInvoiceBuilder.Build(Input(ettn: ettn));
        var r = Root(doc);

        Assert.Equal("2.1", V(r, Cbc + "UBLVersionID"));
        Assert.Equal("TR1.2", V(r, Cbc + "CustomizationID"));
        Assert.Equal("TEMELFATURA", V(r, Cbc + "ProfileID"));
        Assert.Equal(No(), V(r, Cbc + "ID"));
        Assert.Equal("false", V(r, Cbc + "CopyIndicator"));
        Assert.Equal(ettn.ToString("D").ToUpperInvariant(), V(r, Cbc + "UUID"));
        Assert.Equal(Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), V(r, Cbc + "IssueDate"));
        Assert.Equal("14:30:05", V(r, Cbc + "IssueTime"));
        Assert.Equal("SATIS", V(r, Cbc + "InvoiceTypeCode"));
        Assert.Equal("TRY", V(r, Cbc + "DocumentCurrencyCode"));
        Assert.Equal("2", V(r, Cbc + "LineCountNumeric"));
        Assert.Null(r.Element(Cac + "PricingExchangeRate"));

        // Taraflar: satıcı VKN + imza bilgisi; kurumsal alıcı VKN + unvan.
        Assert.Equal("1234567890", V(r, Cac + "AccountingSupplierParty", Cac + "Party", Cac + "PartyIdentification", Cbc + "ID"));
        Assert.Equal("VKN", r.Element(Cac + "AccountingSupplierParty")!.Element(Cac + "Party")!.Element(Cac + "PartyIdentification")!
            .Element(Cbc + "ID")!.Attribute("schemeID")!.Value);
        Assert.Equal("İstanbul", V(r, Cac + "AccountingSupplierParty", Cac + "Party", Cac + "PostalAddress", Cbc + "CityName"));
        Assert.Equal("Kadıköy", V(r, Cac + "AccountingSupplierParty", Cac + "Party", Cac + "PostalAddress", Cbc + "CitySubdivisionName"));
        Assert.Equal("1234567890", V(r, Cac + "Signature", Cbc + "ID"));
        Assert.Equal("9876543210", V(r, Cac + "AccountingCustomerParty", Cac + "Party", Cac + "PartyIdentification", Cbc + "ID"));
        Assert.Equal("Alıcı Lojistik Ltd. Şti.", V(r, Cac + "AccountingCustomerParty", Cac + "Party", Cac + "PartyName", Cbc + "Name"));

        // Vergi: oran başına kırılım (elle: 1000 × %20 = 200, 100 × %10 = 10).
        var tax = r.Element(Cac + "TaxTotal")!;
        Assert.Equal("210.00", V(tax, Cbc + "TaxAmount"));
        var subs = tax.Elements(Cac + "TaxSubtotal").ToList();
        Assert.Equal(2, subs.Count);
        Assert.Equal(("1000.00", "200.00", "20"), (V(subs[0], Cbc + "TaxableAmount"), V(subs[0], Cbc + "TaxAmount"), V(subs[0], Cbc + "Percent")));
        Assert.Equal(("100.00", "10.00", "10"), (V(subs[1], Cbc + "TaxableAmount"), V(subs[1], Cbc + "TaxAmount"), V(subs[1], Cbc + "Percent")));
        Assert.All(subs, s => Assert.Equal("0015", V(s, Cac + "TaxCategory", Cac + "TaxScheme", Cbc + "TaxTypeCode")));

        var total = r.Element(Cac + "LegalMonetaryTotal")!;
        Assert.Equal("1100.00", V(total, Cbc + "LineExtensionAmount"));
        Assert.Equal("1100.00", V(total, Cbc + "TaxExclusiveAmount"));
        Assert.Equal("1310.00", V(total, Cbc + "TaxInclusiveAmount"));
        Assert.Equal("0.00", V(total, Cbc + "AllowanceTotalAmount"));
        Assert.Equal("1310.00", V(total, Cbc + "PayableAmount"));
        Assert.Equal(new UblTotals(1100m, 210m, 1310m, 0m, 1310m), doc.Toplamlar);

        var line2 = r.Elements(Cac + "InvoiceLine").ElementAt(1);
        Assert.Equal("2", V(line2, Cbc + "ID"));
        Assert.Equal("3", V(line2, Cbc + "InvoicedQuantity"));
        Assert.Equal("C62", line2.Element(Cbc + "InvoicedQuantity")!.Attribute("unitCode")!.Value);
        Assert.Equal("100.00", V(line2, Cbc + "LineExtensionAmount"));
        Assert.Equal("33.3333", V(line2, Cac + "Price", Cbc + "PriceAmount"));
        Assert.Equal("Bebek koltuğu (ek hizmet)", V(line2, Cac + "Item", Cbc + "Name"));

        // Tüm tutarlar belge dövizinde.
        Assert.All(r.Descendants().Where(e => e.Attribute("currencyID") is not null), e => Assert.Equal("TRY", e.Attribute("currencyID")!.Value));
        AssertValid(doc, "efatura");
    }

    [Fact]
    public void Commercial_profile_is_valid_e_invoice()
    {
        var doc = UblInvoiceBuilder.Build(Input(UblProfile.TicariFatura));
        Assert.Equal("TICARIFATURA", V(Root(doc), Cbc + "ProfileID"));
        AssertValid(doc, "efatura");
    }

    [Fact]
    public void E_archive_individual_buyer_carries_tckn_person_and_send_type()
    {
        var tckn = TestNationalId.New();
        var doc = UblInvoiceBuilder.Build(Input(UblProfile.EArsivFatura, buyer: PersonBuyer(tckn)));
        var r = Root(doc);
        var party = r.Element(Cac + "AccountingCustomerParty")!.Element(Cac + "Party")!;
        Assert.Equal("EARSIVFATURA", V(r, Cbc + "ProfileID"));
        Assert.Equal(tckn, V(party, Cac + "PartyIdentification", Cbc + "ID"));
        Assert.Equal("TCKN", party.Element(Cac + "PartyIdentification")!.Element(Cbc + "ID")!.Attribute("schemeID")!.Value);
        Assert.Equal(("Ayşe", "Yılmaz"), (V(party, Cac + "Person", Cbc + "FirstName"), V(party, Cac + "Person", Cbc + "FamilyName")));
        Assert.Null(party.Element(Cac + "PartyName"));
        var send = r.Element(Cac + "AdditionalDocumentReference")!;
        Assert.Equal(("ELEKTRONIK", "SEND_TYPE"), (V(send, Cbc + "ID"), V(send, Cbc + "DocumentTypeCode")));
        AssertValid(doc, "earchive");
        // $type parametresi gerçekten etkili: aynı belge e-Fatura kurallarıyla EARSIVFATURA'yı reddeder.
        Assert.Contains(UblTrValidator.ValidateSchematron(doc.Xml, "efatura"), e => e.StartsWith("[ProfileIDCheck]", StringComparison.Ordinal));
    }

    [Fact]
    public void Foreign_currency_invoice_carries_rate_and_currency_on_every_amount()
    {
        var doc = UblInvoiceBuilder.Build(Input(UblProfile.EArsivFatura, buyer: PersonBuyer(TestNationalId.New()), currency: "EUR",
            rate: 38.123456m, lines: [new UblLine("Kira (EUR)", 1m, 500m, 500.00m, 0.20m, 100.00m)]));
        var r = Root(doc);
        Assert.Equal("EUR", V(r, Cbc + "DocumentCurrencyCode"));
        var fx = r.Element(Cac + "PricingExchangeRate")!;
        Assert.Equal(("EUR", "TRY", "38.123456"), (V(fx, Cbc + "SourceCurrencyCode"), V(fx, Cbc + "TargetCurrencyCode"), V(fx, Cbc + "CalculationRate")));
        Assert.Equal("600.00", V(r, Cac + "LegalMonetaryTotal", Cbc + "PayableAmount"));
        Assert.All(r.Descendants().Where(e => e.Attribute("currencyID") is not null), e => Assert.Equal("EUR", e.Attribute("currencyID")!.Value));
        AssertValid(doc, "earchive");
    }

    [Fact]
    public void Refund_invoice_references_returned_invoice()
    {
        var original = No(7);
        var doc = UblInvoiceBuilder.Build(Input(type: UblInvoiceType.Iade, refund: new UblBillingReference(original, Day)));
        var r = Root(doc);
        Assert.Equal("IADE", V(r, Cbc + "InvoiceTypeCode"));
        var reference = r.Element(Cac + "BillingReference")!.Element(Cac + "InvoiceDocumentReference")!;
        Assert.Equal(original, V(reference, Cbc + "ID"));
        Assert.Equal("IADE", V(reference, Cbc + "DocumentTypeCode"));
        AssertValid(doc, "efatura");
    }

    [Fact]
    public void Zero_vat_line_gets_exemption_code_351_and_reason()
    {
        var doc = UblInvoiceBuilder.Build(Input(lines: [new UblLine("Kira (KDV'siz)", 1m, 500m, 500.00m, 0m, 0m)]));
        var category = Root(doc).Element(Cac + "TaxTotal")!.Element(Cac + "TaxSubtotal")!.Element(Cac + "TaxCategory")!;
        Assert.Equal("351", V(category, Cbc + "TaxExemptionReasonCode"));
        Assert.False(string.IsNullOrWhiteSpace(V(category, Cbc + "TaxExemptionReason")));
        Assert.Equal("0", V(Root(doc).Element(Cac + "TaxTotal")!.Element(Cac + "TaxSubtotal")!, Cbc + "Percent"));
        AssertValid(doc, "efatura");
    }

    [Fact]
    public void Withholding_reduces_payable_but_not_tax_inclusive_amount()
    {
        // Elle: KDV 200, tevkifat 2/10 (624, %20) = 40 → KDV dahil 1.200, ödenecek 1.160.
        var doc = UblInvoiceBuilder.Build(Input(type: UblInvoiceType.Tevkifat,
            lines: [new UblLine("Taşıma hizmeti", 1m, 1000m, 1000.00m, 0.20m, 200.00m, Tevkifat: new UblWithholding("624", 20m, 40.00m))]));
        var r = Root(doc);
        Assert.Equal("TEVKIFAT", V(r, Cbc + "InvoiceTypeCode"));
        Assert.Equal("1200.00", V(r, Cac + "LegalMonetaryTotal", Cbc + "TaxInclusiveAmount"));
        Assert.Equal("1160.00", V(r, Cac + "LegalMonetaryTotal", Cbc + "PayableAmount"));
        var w = r.Element(Cac + "WithholdingTaxTotal")!;
        Assert.Equal("40.00", V(w, Cbc + "TaxAmount"));
        Assert.Equal(("200.00", "20", "624"), (V(w, Cac + "TaxSubtotal", Cbc + "TaxableAmount"), V(w, Cac + "TaxSubtotal", Cbc + "Percent"),
            V(w, Cac + "TaxSubtotal", Cac + "TaxCategory", Cac + "TaxScheme", Cbc + "TaxTypeCode")));
        Assert.Equal(new UblTotals(1000m, 200m, 1200m, 40m, 1160m), doc.Toplamlar);
        AssertValid(doc, "efatura");
    }

    // ------------------------------------------------------------------ red (belge üretilmez)

    public static TheoryData<string, string> Rejections => new()
    {
        { "no", "GİB biçiminde" },
        { "yil", "yılıyla aynı" },
        { "iade-ticari", "TICARIFATURA" },
        { "iade-referanssiz", "16 haneli" },
        { "il-yok", "il ve ilçe" },
        { "kurus", "kuruştan hassas" },
        { "kur-yok", "kur zorunludur" },
        { "tevkifat-satisda", "yalnız TEVKIFAT" },
        { "tevkifat-yok", "en az bir satırda tevkifat" },
        { "kdv0-tutar", "%0 KDV'li satırda" },
    };

    [Theory]
    [MemberData(nameof(Rejections))]
    public void Invalid_input_is_rejected_without_rounding_or_guessing(string scenario, string expected)
    {
        var input = scenario switch
        {
            "no" => Input(no: "FT-000001"),
            "yil" => Input(no: "TST" + (Day.Year - 1).ToString(CultureInfo.InvariantCulture) + "000000001"),
            "iade-ticari" => Input(UblProfile.TicariFatura, UblInvoiceType.Iade, refund: new UblBillingReference(No(3), Day)),
            "iade-referanssiz" => Input(type: UblInvoiceType.Iade),
            "il-yok" => Input(buyer: CorporateBuyer with { Il = null }),
            "kurus" => Input(lines: [new UblLine("Kira", 1m, 100.005m, 100.005m, 0.20m, 20.00m)]),
            "kur-yok" => Input(currency: "USD"),
            "tevkifat-satisda" => Input(lines: [new UblLine("Kira", 1m, 100m, 100m, 0.20m, 20m, Tevkifat: new UblWithholding("624", 20m, 4m))]),
            "tevkifat-yok" => Input(type: UblInvoiceType.Tevkifat),
            "kdv0-tutar" => Input(lines: [new UblLine("Kira", 1m, 100m, 100m, 0m, 5m)]),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        var ex = Assert.Throws<EDocumentDataException>(() => UblInvoiceBuilder.Build(input));
        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Party_errors_never_echo_the_national_id()
    {
        var tckn = TestNationalId.New();
        var ex = Assert.Throws<EDocumentDataException>(() =>
            UblInvoiceBuilder.Build(Input(UblProfile.EArsivFatura, buyer: PersonBuyer(tckn) with { Soyad = " " })));
        Assert.Contains("ad ve soyad", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(tckn, ex.Message, StringComparison.Ordinal);
        var bad = tckn[..10];
        ex = Assert.Throws<EDocumentDataException>(() =>
            UblInvoiceBuilder.Build(Input(UblProfile.EArsivFatura, buyer: PersonBuyer(bad + "x"))));
        Assert.DoesNotContain(bad, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Free_text_control_characters_are_stripped_so_xml_stays_well_formed()
    {
        var doc = UblInvoiceBuilder.Build(Input(lines: [new UblLine("Kira\u0001 sözleşmesi\u0007", 1m, 100m, 100m, 0.20m, 20m)]));
        Assert.Equal("Kira sözleşmesi", V(Root(doc).Element(Cac + "InvoiceLine")!, Cac + "Item", Cbc + "Name"));
        AssertValid(doc, "efatura");
    }

    // ------------------------------------------------------------------ oracle boş geçmiyor

    [Fact]
    public void Validator_catches_broken_documents()
    {
        var tckn = TestNationalId.New();
        var valid = UblInvoiceBuilder.Build(Input(UblProfile.EArsivFatura, buyer: PersonBuyer(tckn)));
        var text = valid.XmlText;
        byte[] B(string s) => Encoding.UTF8.GetBytes(s);

        Assert.Contains(UblTrValidator.ValidateSchematron(B(text.Replace(">TR1.2<", ">TR1.0<", StringComparison.Ordinal)), "earchive"),
            e => e.StartsWith("[CustomizationIDCheck]", StringComparison.Ordinal));
        Assert.Contains(UblTrValidator.ValidateSchematron(B(Regex.Replace(text, "<cac:Person>.*?</cac:Person>", "", RegexOptions.Singleline)), "earchive"),
            e => e.StartsWith("[PartyIdentificationPartyNamePersonCheck]", StringComparison.Ordinal));
        Assert.Contains(UblTrValidator.ValidateSchematron(B(text.Replace(">" + No() + "<", ">TS" + No()[2..] + "X<", StringComparison.Ordinal)), "earchive"),
            e => e.StartsWith("[InvoiceIDCheck]", StringComparison.Ordinal));
        Assert.Contains(UblTrValidator.ValidateSchematron(B(text.Replace(">1310.00<", ">1310.001<", StringComparison.Ordinal)), "earchive"),
            e => e.StartsWith("[decimalCheck]", StringComparison.Ordinal));
        // XSD eleman sırası.
        var reordered = text.Replace("<cbc:CopyIndicator>false</cbc:CopyIndicator>", "", StringComparison.Ordinal)
            .Replace("<cbc:UBLVersionID>2.1</cbc:UBLVersionID>", "<cbc:UBLVersionID>2.1</cbc:UBLVersionID><cbc:CopyIndicator>false</cbc:CopyIndicator>", StringComparison.Ordinal);
        Assert.Contains(UblTrValidator.ValidateXsd(B(reordered)), e => e.StartsWith("Error", StringComparison.Ordinal));
    }
}
