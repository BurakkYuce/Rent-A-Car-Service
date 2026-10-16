using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.EInvoicing;

/// <summary>
/// e-Belge üretimi için okunmuş veri. <paramref name="BuyerTckn"/> müşterinin ŞİFRELİ TCKN'sinin bellekte çözülmüş
/// hâlidir — hiçbir kayda/loga yazılmaz; yalnız XML'e girer (XML de saklanmaz, yalnız özeti).
/// </summary>
public sealed record EDocumentSource(
    Invoice Invoice, Customer Buyer, string? BuyerTckn, Domain.Entities.TenantSettings Settings, Guid Ettn, Invoice? RefundSource = null)
{
    public override string ToString() => $"EDocumentSource {{ Fatura = {Invoice.No}, Ettn = {Ettn} }}";
}

/// <summary>Alıcının belgedeki kimliği: VKN (10) ya da TCKN (11). <see cref="Known"/> = false → kimlik bilinmiyor
/// (yalnız e-Arşiv'de nihai tüketici yer tutucusu kullanılabilir).</summary>
public sealed record BuyerIdentity(string? Id, bool IsPerson)
{
    public bool Known => Id is not null;
    public override string ToString() => $"BuyerIdentity {{ Known = {Known}, IsPerson = {IsPerson} }}";
}

/// <summary>
/// Değişmez faturadan UBL-TR girdisi kurar — SAF. Tutarlar faturadan OLDUĞU GİBİ alınır; burada hiçbir tutar
/// yeniden hesaplanmaz ve yuvarlanmaz. Satır toplamları faturanın kayıtlı toplamlarını tutmuyorsa belge üretilmez.
/// </summary>
public static class EDocumentInputFactory
{
    /// <summary>Alıcı kimliği (kurumsal: vergi no; bireysel: çözülmüş TCKN). Biçimsiz değer "bilinmiyor" sayılır.</summary>
    public static BuyerIdentity Identify(Customer c, string? tckn)
    {
        if (c.Tip == CustomerType.Bireysel)
        {
            var id = Digits(tckn);
            return new BuyerIdentity(id is { Length: 11 } && id[0] != '0' ? id : null, true);
        }
        var taxNo = Digits(c.VergiNo);
        return taxNo switch
        {
            { Length: 10 } => new BuyerIdentity(taxNo, false),
            { Length: 11 } when taxNo[0] != '0' => new BuyerIdentity(taxNo, true),
            _ => new BuyerIdentity(null, false),
        };
    }

    /// <summary>
    /// UBL girdisi. <paramref name="profile"/> alıcı sorgusundan gelir (e-Fatura kullanıcısı → TEMEL/TICARI, değilse
    /// EARSIVFATURA). Veri eksikse <see cref="EDocumentDataException"/> (kişisel veri içermeyen mesaj).
    /// </summary>
    public static UblInvoiceInput Create(EDocumentSource src, UblProfile profile, TimeZoneInfo tz)
    {
        var inv = src.Invoice;
        if (inv.Durum == InvoiceStatus.Iptal) throw new EDocumentDataException("İptal edilmiş fatura için e-belge üretilmez.");
        if (inv.TevkifatOran is > 0m || inv.TevkifatTutar is > 0m)
            throw new EDocumentDataException(
                "Tevkifatlı fatura için e-belge henüz otomatik üretilmiyor: defter kaydı tevkifatı yansıtmıyor, " +
                "e-belgedeki ödenecek tutar defterden sapardı (ayrı para işi).");
        if (inv.Otv is > 0m)
            throw new EDocumentDataException("ÖTV'li fatura için e-belge henüz otomatik üretilmiyor (ÖTV vergi satırı desteklenmiyor).");
        if (inv.Lines.Count == 0) throw new EDocumentDataException("Faturada satır yok.");

        // Tutar kilidi: e-belge defterdeki faturadan FARKLI tutar taşıyamaz.
        var net = inv.Lines.Sum(l => l.SatirNet);
        var vat = inv.Lines.Sum(l => l.SatirKdv);
        if (net != inv.NetTutar || vat != inv.KdvTutar || inv.NetTutar + inv.KdvTutar != inv.GenelToplam)
            throw new EDocumentDataException(
                "Fatura satırlarının toplamı fatura toplamıyla tutmuyor; e-belge üretilmedi (tutarlar değiştirilmez).");

        var local = TimeZoneInfo.ConvertTime(inv.Tarih, tz);
        var type = inv.IadeMi ? UblInvoiceType.Iade : UblInvoiceType.Satis;
        var currency = (inv.Currency ?? "TRY").Trim().ToUpperInvariant();

        return new UblInvoiceInput(
            Ettn: src.Ettn,
            No: inv.No,
            Tarih: DateOnly.FromDateTime(local.DateTime),
            Saat: new TimeOnly(local.Hour, local.Minute, local.Second),
            Profil: profile,
            Tip: type,
            ParaBirimi: currency,
            Kur: currency == "TRY" ? null : inv.Kur,
            Satici: Seller(src.Settings),
            Alici: Buyer(src.Buyer, src.BuyerTckn, profile),
            Satirlar: [.. inv.Lines.Select(l => new UblLine(
                Ad: l.Aciklama, Miktar: l.Miktar, BirimFiyat: l.BirimNetFiyat,
                Tutar: l.SatirNet, KdvOrani: l.KdvOrani, KdvTutar: l.SatirKdv))],
            IadeReferansi: type == UblInvoiceType.Iade && src.RefundSource is { } s
                ? new UblBillingReference(s.No, DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(s.Tarih, tz).DateTime))
                : null,
            KdvIstisnaSebebi: inv.KdvSifirSebep);
    }

    public static UblParty Seller(Domain.Entities.TenantSettings s)
        => new(
            VknTckn: Digits(s.FirmaVergiNo) ?? string.Empty,
            Unvan: s.FirmaUnvan,
            Ad: null, Soyad: null,
            VergiDairesi: s.FirmaVergiDairesi,
            Adres: s.FirmaAdres,
            Ilce: s.FirmaIlce,
            Il: s.FirmaIl,
            Ulke: null,
            Telefon: s.FirmaTel,
            Eposta: s.FirmaEmail);

    public static UblParty Buyer(Customer c, string? tckn, UblProfile profile)
    {
        var identity = Identify(c, tckn);
        string id;
        if (identity.Id is { } known) id = known;
        else if (c.Tip == CustomerType.Bireysel && profile == UblProfile.EArsivFatura)
            id = UblInvoiceBuilder.UnknownConsumerTckn; // GİB: TCKN'si bilinmeyen nihai tüketici
        else
            throw new EDocumentDataException(c.Tip == CustomerType.Bireysel
                ? "Alıcının TC kimlik numarası kayıtlı değil ya da geçersiz."
                : "Alıcının vergi numarası (10 hane VKN ya da 11 hane TCKN) kayıtlı değil ya da geçersiz.");

        var person = id.Length == 11;
        var street = c.FaturaAdresFarkli && !string.IsNullOrWhiteSpace(c.FaturaAdresi) ? c.FaturaAdresi : c.Adres;
        var title = string.IsNullOrWhiteSpace(c.FaturaUnvan) ? c.Unvan : c.FaturaUnvan;
        return new UblParty(
            VknTckn: id,
            Unvan: c.Tip == CustomerType.Bireysel ? null : title,
            Ad: person ? c.Ad : null,
            Soyad: person ? c.Soyad : null,
            VergiDairesi: c.Tip == CustomerType.Bireysel ? null : c.VergiDairesi,
            Adres: street,
            Ilce: c.Ilce,
            Il: c.Il,
            Ulke: c.Ulke,
            Telefon: c.CepTel,
            Eposta: c.Email);
    }

    private static string? Digits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var d = new string([.. value.Where(char.IsAsciiDigit)]);
        return d.Length == 0 ? null : d;
    }
}
