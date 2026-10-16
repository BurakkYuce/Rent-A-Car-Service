using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using RentACar.Application.Common;

namespace RentACar.Application.EInvoicing;

/// <summary>
/// e-Belge verisi eksik/geçersiz — belge ÜRETİLMEZ. Mesaj kullanıcıya gösterilebilir ve KİŞİSEL VERİ İÇERMEZ
/// (TCKN/VKN değeri asla mesaja yazılmaz; yalnız hangi alanın sorunlu olduğu söylenir).
/// </summary>
public sealed class EDocumentDataException(string message) : ValidationException(message);

/// <summary>
/// UBL-TR 2.1 (e-Fatura paketi 1.2, <c>CustomizationID = TR1.2</c>) fatura XML'i üretir — SAF fonksiyon: girdi
/// <see cref="UblInvoiceInput"/>, çıktı UTF-8 bayt + toplamlar. DB, ağ, saat okumaz.
///
/// <para><b>Kapsam:</b> TEMELFATURA, TICARIFATURA ve EARSIVFATURA senaryoları; SATIS, IADE ve TEVKIFAT tipleri;
/// satır bazında KDV (oran başına belge kırılımı), %0 KDV için istisna kodu (351), satır tevkifatı, döviz + kur,
/// VKN (tüzel) ve TCKN (gerçek kişi) taraflar.</para>
///
/// <para><b>İmza:</b> belge İMZASIZ üretilir. UBL-TR şeması <c>ext:UBLExtensions</c>'ı zorunlu tutar ve içinde tam
/// bir eleman ister; oraya yer tutucu konur, mali mühürle imzalayan entegratör onu <c>ds:Signature</c> ile değiştirir.
/// <c>cac:Signature</c> (imzalayan taraf bilgisi) satıcıdan doldurulur.</para>
///
/// <para><b>Para:</b> tutarlar yeniden HESAPLANMAZ — satırlar faturadan olduğu gibi gelir, belge toplamları satırların
/// toplamıdır ve <see cref="UblTotals"/> olarak döner. Çağıran bunları faturanın kayıtlı toplamlarıyla karşılaştırır;
/// kuruştan hassas (2 haneyi aşan) bir tutar YUVARLANMAZ, belge reddedilir.</para>
///
/// <para>Eleman sırası UBL-TR XSD'sinin <c>xsd:sequence</c>'ına birebir uyar (testte GİB XSD'sine karşı doğrulanır).</para>
/// </summary>
public static class UblInvoiceBuilder
{
    public const string UblVersionId = "2.1";
    public const string CustomizationId = "TR1.2";

    /// <summary>GİB'in KDV vergi tipi kodu.</summary>
    public const string VatTaxTypeCode = "0015";

    /// <summary>%0 KDV'li satış için istisna kodu: 351 "KDV — İstisna Olmayan Diğer" (ISTISNA tipi gerektirmez).</summary>
    public const string ZeroVatExemptionCode = "351";

    /// <summary>TCKN'si bilinmeyen nihai tüketiciye e-Arşiv faturasında GİB'in kabul ettiği yer tutucu kimlik.</summary>
    public const string UnknownConsumerTckn = "11111111111";

    public static readonly XNamespace Inv = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
    public static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    public static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    public static readonly XNamespace Ext = "urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2";

    /// <summary>İmza yer tutucusunun ad alanı — entegratör bu elemanı imzayla değiştirir.</summary>
    public static readonly XNamespace SignaturePlaceholderNs = "urn:rentacar:ubl-tr:imza-yer-tutucu";

    /// <summary>GİB şematronundaki fatura numarası deseni (<c>InvoiceIDCheck</c>).</summary>
    private static readonly Regex InvoiceNoPattern = new("^[A-Z0-9]{3}20[0-9]{2}[0-9]{9}$", RegexOptions.CultureInvariant);
    private static readonly Regex CurrencyPattern = new("^[A-Z]{3}$", RegexOptions.CultureInvariant);
    private static readonly Regex WithholdingCodePattern = new("^[0-9]{3}$", RegexOptions.CultureInvariant);
    private static readonly CultureInfo Inv0 = CultureInfo.InvariantCulture;

    public static string ProfileId(UblProfile profile) => profile switch
    {
        UblProfile.TemelFatura => "TEMELFATURA",
        UblProfile.TicariFatura => "TICARIFATURA",
        UblProfile.EArsivFatura => "EARSIVFATURA",
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null)
    };

    public static string TypeCode(UblInvoiceType type) => type switch
    {
        UblInvoiceType.Satis => "SATIS",
        UblInvoiceType.Iade => "IADE",
        UblInvoiceType.Tevkifat => "TEVKIFAT",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    /// <summary>Belgeyi üretir. Veri eksik/geçersizse <see cref="EDocumentDataException"/> (kişisel veri içermez).</summary>
    public static UblDocument Build(UblInvoiceInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var currency = Validate(input);
        var totals = Totals(input);

        var root = new XElement(Inv + "Invoice",
            new XAttribute(XNamespace.Xmlns + "cac", Cac.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "cbc", Cbc.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "ext", Ext.NamespaceName),
            Extensions(),
            Basic("UBLVersionID", UblVersionId),
            Basic("CustomizationID", CustomizationId),
            Basic("ProfileID", ProfileId(input.Profil)),
            Basic("ID", input.No),
            Basic("CopyIndicator", "false"),
            Basic("UUID", input.Ettn.ToString("D").ToUpperInvariant()),
            Basic("IssueDate", Date(input.Tarih)),
            Basic("IssueTime", input.Saat.ToString("HH':'mm':'ss", Inv0)),
            Basic("InvoiceTypeCode", TypeCode(input.Tip)),
            (input.Notlar ?? []).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => Basic("Note", Clean(n))),
            Basic("DocumentCurrencyCode", currency),
            Basic("LineCountNumeric", input.Satirlar.Count.ToString(Inv0)),
            BillingReference(input),
            SendTypeReference(input),
            Signature(input.Satici, input.No),
            new XElement(Cac + "AccountingSupplierParty", Party(input.Satici)),
            new XElement(Cac + "AccountingCustomerParty", Party(input.Alici)),
            ExchangeRate(input, currency),
            DocumentTaxTotal(input, currency, totals),
            DocumentWithholding(input, currency, totals),
            new XElement(Cac + "LegalMonetaryTotal",
                Amount("LineExtensionAmount", totals.Net, currency),
                Amount("TaxExclusiveAmount", totals.Net, currency),
                Amount("TaxInclusiveAmount", totals.KdvDahil, currency),
                Amount("AllowanceTotalAmount", 0m, currency),
                Amount("PayableAmount", totals.Odenecek, currency)),
            input.Satirlar.Select((l, i) => Line(l, i + 1, currency, input.KdvIstisnaSebebi)));

        var document = new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
        return new UblDocument(Serialize(document), totals);
    }

    // ------------------------------------------------------------------ doğrulama

    private static string Validate(UblInvoiceInput input)
    {
        if (input.Ettn == Guid.Empty) throw new EDocumentDataException("ETTN boş olamaz.");
        if (string.IsNullOrWhiteSpace(input.No) || !InvoiceNoPattern.IsMatch(input.No))
            throw new EDocumentDataException(
                "Fatura numarası GİB biçiminde değil (16 karakter: 3 karakter seri + 4 hane yıl + 9 hane sıra).");
        if (int.Parse(input.No.AsSpan(3, 4), Inv0) != input.Tarih.Year)
            throw new EDocumentDataException("Fatura numarasındaki yıl, düzenleme tarihinin yılıyla aynı olmalıdır.");

        var currency = (input.ParaBirimi ?? string.Empty).Trim().ToUpperInvariant();
        if (!CurrencyPattern.IsMatch(currency)) throw new EDocumentDataException("Para birimi 3 harfli ISO kodu olmalıdır.");
        if (currency != "TRY")
        {
            if (input.Kur is not > 0m) throw new EDocumentDataException("Döviz faturasında kur zorunludur ve pozitif olmalıdır.");
            if (decimal.Round(input.Kur.Value, 6) != input.Kur.Value)
                throw new EDocumentDataException("Kur en fazla 6 ondalık hane içerebilir.");
        }

        ValidateParty(input.Satici, "Satıcı");
        ValidateParty(input.Alici, "Alıcı");

        if (input.Satirlar is null || input.Satirlar.Count == 0) throw new EDocumentDataException("Faturada en az bir satır olmalıdır.");
        for (var i = 0; i < input.Satirlar.Count; i++) ValidateLine(input.Satirlar[i], i + 1, input.Tip);
        if (input.Tip == UblInvoiceType.Tevkifat && input.Satirlar.All(l => l.Tevkifat is null))
            throw new EDocumentDataException("TEVKIFAT tipli faturada en az bir satırda tevkifat bilgisi olmalıdır.");

        if (input.Tip == UblInvoiceType.Iade)
        {
            if (input.Profil == UblProfile.TicariFatura)
                throw new EDocumentDataException("İADE tipli fatura TICARIFATURA senaryosunda düzenlenemez (TEMELFATURA ya da e-Arşiv).");
            if (input.IadeReferansi is null || !InvoiceNoPattern.IsMatch(input.IadeReferansi.No ?? string.Empty))
                throw new EDocumentDataException("İADE faturasında iade edilen faturanın 16 haneli numarası zorunludur.");
        }
        if (input.Profil == UblProfile.EArsivFatura && input.EArsivGonderimSekli is not ("ELEKTRONIK" or "KAGIT"))
            throw new EDocumentDataException("e-Arşiv gönderim şekli ELEKTRONIK ya da KAGIT olmalıdır.");
        return currency;
    }

    private static void ValidateParty(UblParty p, string role)
    {
        if (p is null) throw new EDocumentDataException($"{role} bilgisi eksik.");
        var id = p.VknTckn ?? string.Empty;
        if (!id.All(char.IsAsciiDigit) || id.Length is not (10 or 11))
            throw new EDocumentDataException($"{role}: VKN 10, TCKN 11 haneli olmalıdır.");
        if (id.Length == 11 && id[0] == '0') throw new EDocumentDataException($"{role}: TCKN 0 ile başlayamaz.");
        if (id.Length == 10 && string.IsNullOrWhiteSpace(p.Unvan))
            throw new EDocumentDataException($"{role}: VKN'li tarafta unvan zorunludur.");
        if (id.Length == 11 && (string.IsNullOrWhiteSpace(p.Ad) || string.IsNullOrWhiteSpace(p.Soyad)))
            throw new EDocumentDataException($"{role}: TCKN'li tarafta ad ve soyad zorunludur.");
        if (string.IsNullOrWhiteSpace(p.Il) || string.IsNullOrWhiteSpace(p.Ilce))
            throw new EDocumentDataException($"{role}: adreste il ve ilçe zorunludur.");
    }

    private static void ValidateLine(UblLine l, int no, UblInvoiceType type)
    {
        if (string.IsNullOrWhiteSpace(l.Ad)) throw new EDocumentDataException($"Satır {no}: açıklama boş olamaz.");
        if (l.Miktar <= 0m) throw new EDocumentDataException($"Satır {no}: miktar pozitif olmalıdır.");
        if (l.Tutar < 0m || l.KdvTutar < 0m || l.BirimFiyat < 0m)
            throw new EDocumentDataException($"Satır {no}: tutarlar negatif olamaz.");
        if (l.KdvOrani is < 0m or > 1m) throw new EDocumentDataException($"Satır {no}: KDV oranı kesir olmalıdır (0,20 = %20).");
        RequireCents(l.Tutar, $"Satır {no}: tutar");
        RequireCents(l.KdvTutar, $"Satır {no}: KDV tutarı");
        if (l.KdvOrani == 0m && l.KdvTutar != 0m) throw new EDocumentDataException($"Satır {no}: %0 KDV'li satırda KDV tutarı 0 olmalıdır.");
        if (string.IsNullOrWhiteSpace(l.BirimKodu)) throw new EDocumentDataException($"Satır {no}: birim kodu boş olamaz.");
        if (l.Tevkifat is { } w)
        {
            if (type != UblInvoiceType.Tevkifat)
                throw new EDocumentDataException($"Satır {no}: tevkifat yalnız TEVKIFAT tipli faturada kullanılabilir.");
            if (!WithholdingCodePattern.IsMatch(w.Kod ?? string.Empty))
                throw new EDocumentDataException($"Satır {no}: tevkifat kodu 3 haneli olmalıdır.");
            if (w.Oran is <= 0m or > 100m) throw new EDocumentDataException($"Satır {no}: tevkifat oranı 0-100 arasında olmalıdır.");
            RequireCents(w.Tutar, $"Satır {no}: tevkifat tutarı");
            if (w.Tutar < 0m || w.Tutar > l.KdvTutar)
                throw new EDocumentDataException($"Satır {no}: tevkifat tutarı KDV tutarını aşamaz.");
        }
    }

    private static void RequireCents(decimal value, string what)
    {
        if (decimal.Round(value, 2) != value)
            throw new EDocumentDataException($"{what} kuruştan hassas olamaz (en fazla 2 ondalık hane).");
    }

    private static UblTotals Totals(UblInvoiceInput input)
    {
        var net = input.Satirlar.Sum(l => l.Tutar);
        var vat = input.Satirlar.Sum(l => l.KdvTutar);
        var withheld = input.Satirlar.Sum(l => l.Tevkifat?.Tutar ?? 0m);
        return new UblTotals(net, vat, net + vat, withheld, net + vat - withheld);
    }

    // ------------------------------------------------------------------ bölümler

    private static XElement Extensions()
        => new(Ext + "UBLExtensions",
            new XElement(Ext + "UBLExtension",
                new XElement(Ext + "ExtensionContent",
                    new XElement(SignaturePlaceholderNs + "ImzaYerTutucu",
                        new XAttribute(XNamespace.Xmlns + "rcimza", SignaturePlaceholderNs.NamespaceName)))));

    private static XElement? BillingReference(UblInvoiceInput input)
        => input.Tip != UblInvoiceType.Iade || input.IadeReferansi is not { } r
            ? null
            : new XElement(Cac + "BillingReference",
                new XElement(Cac + "InvoiceDocumentReference",
                    Basic("ID", r.No),
                    Basic("IssueDate", Date(r.Tarih)),
                    Basic("DocumentTypeCode", "IADE")));

    /// <summary>e-Arşiv teslim şekli (ELEKTRONIK/KAGIT) — <c>DocumentTypeCode = SEND_TYPE</c> referansı.</summary>
    private static XElement? SendTypeReference(UblInvoiceInput input)
        => input.Profil != UblProfile.EArsivFatura
            ? null
            : new XElement(Cac + "AdditionalDocumentReference",
                Basic("ID", input.EArsivGonderimSekli),
                Basic("IssueDate", Date(input.Tarih)),
                Basic("DocumentTypeCode", "SEND_TYPE"));

    private static XElement Signature(UblParty seller, string invoiceNo)
        => new(Cac + "Signature",
            new XElement(Cbc + "ID", new XAttribute("schemeID", "VKN_TCKN"), seller.VknTckn),
            new XElement(Cac + "SignatoryParty",
                Identification(seller.VknTckn),
                Address(seller)),
            new XElement(Cac + "DigitalSignatureAttachment",
                new XElement(Cac + "ExternalReference", Basic("URI", "#Signature_" + invoiceNo))));

    private static XElement Party(UblParty p)
    {
        var person = p.VknTckn.Length == 11;
        var name = Clean(p.Unvan);
        return new XElement(Cac + "Party",
            Identification(p.VknTckn),
            string.IsNullOrWhiteSpace(name) ? null : new XElement(Cac + "PartyName", Basic("Name", name)),
            Address(p),
            string.IsNullOrWhiteSpace(p.VergiDairesi) ? null
                : new XElement(Cac + "PartyTaxScheme", new XElement(Cac + "TaxScheme", Basic("Name", Clean(p.VergiDairesi)))),
            Contact(p),
            person
                ? new XElement(Cac + "Person", Basic("FirstName", Clean(p.Ad)), Basic("FamilyName", Clean(p.Soyad)))
                : null);
    }

    private static XElement Identification(string vknTckn)
        => new(Cac + "PartyIdentification",
            new XElement(Cbc + "ID", new XAttribute("schemeID", vknTckn.Length == 11 ? "TCKN" : "VKN"), vknTckn));

    private static XElement Address(UblParty p)
    {
        var (countryCode, countryName) = Country(p.Ulke);
        return new XElement(Cac + "PostalAddress",
            string.IsNullOrWhiteSpace(p.Adres) ? null : Basic("StreetName", Clean(p.Adres)),
            Basic("CitySubdivisionName", Clean(p.Ilce)),
            Basic("CityName", Clean(p.Il)),
            new XElement(Cac + "Country",
                countryCode is null ? null : Basic("IdentificationCode", countryCode),
                Basic("Name", countryName)));
    }

    private static XElement? Contact(UblParty p)
    {
        var phone = Clean(p.Telefon);
        var mail = Clean(p.Eposta);
        if (string.IsNullOrWhiteSpace(phone) && string.IsNullOrWhiteSpace(mail)) return null;
        return new XElement(Cac + "Contact",
            string.IsNullOrWhiteSpace(phone) ? null : Basic("Telephone", phone),
            string.IsNullOrWhiteSpace(mail) ? null : Basic("ElectronicMail", mail));
    }

    /// <summary>Ülke boşsa ya da Türkiye'nin yazımlarından biriyse TR; aksi halde yalnız ad (kod uydurulmaz).</summary>
    private static (string? Code, string Name) Country(string? value)
    {
        var v = Clean(value);
        if (string.IsNullOrWhiteSpace(v)) return ("TR", "Türkiye");
        var key = v.Trim().ToUpperInvariant();
        return key is "TR" or "TÜRKİYE" or "TÜRKIYE" or "TURKIYE" or "TURKEY" or "TÜRKIYE CUMHURIYETI" or "TÜRKİYE CUMHURİYETİ"
            ? ("TR", "Türkiye")
            : (null, v.Trim());
    }

    private static XElement? ExchangeRate(UblInvoiceInput input, string currency)
        => currency == "TRY"
            ? null
            : new XElement(Cac + "PricingExchangeRate",
                Basic("SourceCurrencyCode", currency),
                Basic("TargetCurrencyCode", "TRY"),
                Basic("CalculationRate", input.Kur!.Value.ToString("0.######", Inv0)),
                Basic("Date", Date(input.Tarih)));

    private static XElement DocumentTaxTotal(UblInvoiceInput input, string currency, UblTotals totals)
        => new(Cac + "TaxTotal",
            Amount("TaxAmount", totals.Kdv, currency),
            input.Satirlar
                .GroupBy(l => l.KdvOrani)
                .OrderByDescending(g => g.Key)
                .Select(g => TaxSubtotal(g.Sum(l => l.Tutar), g.Sum(l => l.KdvTutar), g.Key, currency, input.KdvIstisnaSebebi)));

    private static XElement TaxSubtotal(decimal taxable, decimal tax, decimal rate, string currency, string? exemptionReason)
        => new(Cac + "TaxSubtotal",
            Amount("TaxableAmount", taxable, currency),
            Amount("TaxAmount", tax, currency),
            Basic("Percent", Percent(rate)),
            new XElement(Cac + "TaxCategory",
                rate == 0m ? Basic("TaxExemptionReasonCode", ZeroVatExemptionCode) : null,
                rate == 0m ? Basic("TaxExemptionReason", Clean(exemptionReason) is { Length: > 0 } r ? r : "KDV oranı %0 (istisna olmayan diğer)") : null,
                new XElement(Cac + "TaxScheme", Basic("Name", "KDV"), Basic("TaxTypeCode", VatTaxTypeCode))));

    private static XElement? DocumentWithholding(UblInvoiceInput input, string currency, UblTotals totals)
    {
        var lines = input.Satirlar.Where(l => l.Tevkifat is not null).ToList();
        if (lines.Count == 0) return null;
        return new XElement(Cac + "WithholdingTaxTotal",
            Amount("TaxAmount", totals.Tevkifat, currency),
            lines.GroupBy(l => (l.Tevkifat!.Kod, l.Tevkifat.Oran))
                .OrderBy(g => g.Key.Kod, StringComparer.Ordinal)
                .Select(g => WithholdingSubtotal(g.Sum(l => l.KdvTutar), g.Sum(l => l.Tevkifat!.Tutar), g.Key.Kod, g.Key.Oran, currency)));
    }

    private static XElement WithholdingSubtotal(decimal vat, decimal withheld, string code, decimal percent, string currency)
        => new(Cac + "TaxSubtotal",
            Amount("TaxableAmount", vat, currency),
            Amount("TaxAmount", withheld, currency),
            Basic("Percent", percent.ToString("0.####", Inv0)),
            new XElement(Cac + "TaxCategory",
                new XElement(Cac + "TaxScheme", Basic("Name", "KDV TEVKİFAT"), Basic("TaxTypeCode", code))));

    private static XElement Line(UblLine l, int no, string currency, string? exemptionReason)
        => new(Cac + "InvoiceLine",
            Basic("ID", no.ToString(Inv0)),
            new XElement(Cbc + "InvoicedQuantity", new XAttribute("unitCode", l.BirimKodu), l.Miktar.ToString("0.####", Inv0)),
            Amount("LineExtensionAmount", l.Tutar, currency),
            new XElement(Cac + "TaxTotal",
                Amount("TaxAmount", l.KdvTutar, currency),
                TaxSubtotal(l.Tutar, l.KdvTutar, l.KdvOrani, currency, exemptionReason)),
            l.Tevkifat is { } w
                ? new XElement(Cac + "WithholdingTaxTotal",
                    Amount("TaxAmount", w.Tutar, currency),
                    WithholdingSubtotal(l.KdvTutar, w.Tutar, w.Kod, w.Oran, currency))
                : null,
            new XElement(Cac + "Item", Basic("Name", Clean(l.Ad))),
            new XElement(Cac + "Price", Amount("PriceAmount", l.BirimFiyat, currency, "0.00######")));

    // ------------------------------------------------------------------ yardımcılar

    private static XElement Basic(string name, string? value) => new(Cbc + name, value ?? string.Empty);

    private static XElement Amount(string name, decimal value, string currency, string format = "0.00")
        => new(Cbc + name, new XAttribute("currencyID", currency), value.ToString(format, Inv0));

    private static string Date(DateOnly d) => d.ToString("yyyy'-'MM'-'dd", Inv0);

    /// <summary>Oran kesirden yüzdeye (0.20 → "20", 0.085 → "8.5").</summary>
    private static string Percent(decimal rate) => (rate * 100m).ToString("0.####", Inv0);

    /// <summary>XML'de geçersiz kontrol karakterlerini atar ve kırpar (serbest metin alanları kullanıcı girdisidir).</summary>
    private static string? Clean(string? value)
    {
        if (value is null) return null;
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                sb.Append(c).Append(value[++i]); // geçerli vekil çift (ör. emoji) korunur
                continue;
            }
            if (c is '\t' or '\n' or '\r' || (!char.IsControl(c) && XmlConvert.IsXmlChar(c)))
                sb.Append(c);
        }
        return sb.ToString().Trim();
    }

    private static byte[] Serialize(XDocument document)
    {
        using var ms = new MemoryStream();
        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, OmitXmlDeclaration = false };
        using (var writer = XmlWriter.Create(ms, settings)) document.Save(writer);
        return ms.ToArray();
    }
}
