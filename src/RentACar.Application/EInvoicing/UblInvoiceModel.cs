namespace RentACar.Application.EInvoicing;

/// <summary>UBL-TR senaryosu (<c>cbc:ProfileID</c>).</summary>
public enum UblProfile
{
    /// <summary>TEMELFATURA — alıcı kabul/ret yanıtı vermez.</summary>
    TemelFatura = 0,
    /// <summary>TICARIFATURA — alıcı 8 gün içinde kabul/ret yanıtı verebilir. İADE tipi bu senaryoda KULLANILAMAZ.</summary>
    TicariFatura = 1,
    /// <summary>EARSIVFATURA — alıcı e-Fatura kullanıcısı değilse.</summary>
    EArsivFatura = 2
}

/// <summary>UBL-TR fatura tipi (<c>cbc:InvoiceTypeCode</c>).</summary>
public enum UblInvoiceType
{
    Satis = 0,
    Iade = 1,
    /// <summary>KDV tevkifatlı satış — satırlarda tevkifat bilgisi zorunlu.</summary>
    Tevkifat = 2
}

/// <summary>
/// Belgedeki bir taraf (satıcı ya da alıcı). <paramref name="VknTckn"/> 10 haneli ise VKN (tüzel kişi; unvan
/// zorunlu), 11 haneli ise TCKN (gerçek kişi; ad + soyad zorunlu). KİŞİSEL VERİ: ToString maskelenmiştir.
/// </summary>
public sealed record UblParty(
    string VknTckn,
    string? Unvan,
    string? Ad,
    string? Soyad,
    string? VergiDairesi,
    string? Adres,
    string? Ilce,
    string? Il,
    string? Ulke,
    string? Telefon = null,
    string? Eposta = null)
{
    public override string ToString() => $"UblParty {{ Unvan = {Unvan}, Il = {Il} }}";
}

/// <summary>Satır KDV tevkifatı. <paramref name="Kod"/> GİB tevkifat kodu (ör. 624), <paramref name="Oran"/> yüzde
/// (ör. 20 = 2/10), <paramref name="Tutar"/> tevkif edilen KDV.</summary>
public sealed record UblWithholding(string Kod, decimal Oran, decimal Tutar);

/// <summary>
/// Fatura satırı — tutarlar FATURADAN olduğu gibi gelir, burada yeniden HESAPLANMAZ (yalnız biçim/tutarlılık
/// doğrulanır). <paramref name="KdvOrani"/> kesir (0.20 = %20).
/// </summary>
public sealed record UblLine(
    string Ad,
    decimal Miktar,
    decimal BirimFiyat,
    decimal Tutar,
    decimal KdvOrani,
    decimal KdvTutar,
    string BirimKodu = "C62",
    UblWithholding? Tevkifat = null);

/// <summary>İADE faturasında iade edilen faturanın numarası ve tarihi (<c>cac:BillingReference</c>).</summary>
public sealed record UblBillingReference(string No, DateOnly Tarih);

/// <summary>
/// UBL-TR belge girdisi. Saf veri — üretici (<see cref="UblInvoiceBuilder"/>) yalnız bunu okur, DB/ağ görmez.
/// </summary>
/// <param name="Ettn">Belge UUID'si.</param>
/// <param name="No">16 haneli GİB fatura numarası (seri(3) + yıl(4) + sıra(9)).</param>
/// <param name="Tarih">Düzenleme günü (İstanbul takvim günü).</param>
/// <param name="Saat">Düzenleme saati (e-Arşiv'de zorunlu; her zaman yazılır).</param>
/// <param name="ParaBirimi">ISO 4217 (TRY, EUR, USD…).</param>
/// <param name="Kur">TRY dışı belgede zorunlu: 1 birim = <paramref name="Kur"/> TL.</param>
/// <param name="KdvIstisnaSebebi">%0 KDV'li satır varsa <c>cbc:TaxExemptionReason</c> metni (boşsa genel açıklama).</param>
/// <param name="EArsivGonderimSekli">e-Arşiv teslim şekli: <c>ELEKTRONIK</c> (varsayılan) ya da <c>KAGIT</c>.</param>
public sealed record UblInvoiceInput(
    Guid Ettn,
    string No,
    DateOnly Tarih,
    TimeOnly Saat,
    UblProfile Profil,
    UblInvoiceType Tip,
    string ParaBirimi,
    decimal? Kur,
    UblParty Satici,
    UblParty Alici,
    IReadOnlyList<UblLine> Satirlar,
    UblBillingReference? IadeReferansi = null,
    IReadOnlyList<string>? Notlar = null,
    string? KdvIstisnaSebebi = null,
    string EArsivGonderimSekli = "ELEKTRONIK");

/// <summary>Üreticinin hesapladığı belge toplamları — çağıran bunları faturanın KAYITLI toplamlarıyla karşılaştırır
/// (e-belge defterden farklı tutar taşıyamaz).</summary>
public sealed record UblTotals(decimal Net, decimal Kdv, decimal KdvDahil, decimal Tevkifat, decimal Odenecek);

/// <summary>Üretilmiş belge: UTF-8 XML baytları + toplamlar.</summary>
public sealed record UblDocument(byte[] Xml, UblTotals Toplamlar)
{
    public string XmlText => System.Text.Encoding.UTF8.GetString(Xml);
}
