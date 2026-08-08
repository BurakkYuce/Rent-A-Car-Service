using RentACar.Domain.Entities;

namespace RentACar.Application.Vehicles;

/// <summary>
/// FAZ-28 — detaylı araç listesi satırı: aracın kendi alanları + diğer tablolardan ÇÖZÜLEN bilgiler.
///
/// <para><b>Aktif kira bilgisi DEPOLANMAZ</b> (<see cref="AktifKiraMusteri"/>,
/// <see cref="AktifKiraBitis"/>): her istekte kira tablosundan CANLI çözülür. Araçta bir
/// "güncel kiracı" kolonu tutmak, kira iptal/uzatma/dönüş akışlarının hepsinde ayrıca güncellenmesi
/// gereken ikinci bir gerçeklik yaratırdı — biri unutulunca liste sessizce yalan söylerdi.
/// (Aracın üzerindeki <c>Kiralayan</c>/<c>KiraBitTar</c> alanları AYRI bir şeydir: elle girilen
/// anlık görüntü notlarıdır, kaynak değildir.)</para>
/// </summary>
public sealed record VehicleDetayRow(
    Vehicle Arac,
    string? KrediBanka,
    DateTimeOffset? MuayeneBitis,
    DateTimeOffset? KaskoBitis,
    DateTimeOffset? TrafikBitis,
    decimal? SatisHedefFiyat,
    DateTimeOffset? IhaleTarihi,
    string? IhaleFirmasi,
    DateTimeOffset? NoterSatisTarihi,
    string? AktifKiraMusteri,
    DateTimeOffset? AktifKiraBitis,
    string? AktifKiraSozlesmeNo);

/// <summary>
/// FAZ-11 — araç LİSTESİ satırının diğer tablolardan gelen ek bilgisi. Sayfalanmış listede
/// yalnız GÖRÜNEN araçlar için toplu çekilir (N+1 yok, tüm filo taranmaz).
///
/// <para>Neden ayrı bir tip: <c>SearchAsync</c> <c>PagedResult&lt;Vehicle&gt;</c> döndürüyor ve
/// 20'den fazla çağrı yeri var; imzayı değiştirmek yerine yanına çözülen bir sözlük konuyor.
/// Buradaki hiçbir alan araç kaydına YAZILMAZ — her istekte canlı okunur.</para>
/// </summary>
/// <param name="AktifKiraSozlesmeNo">Aracın AÇIK kira sözleşmesinin numarası (yoksa null).</param>
/// <param name="AcikServis">Açık (Acik/Serviste) servis kaydı var mı.</param>
/// <param name="AcikBaf">Açık BAF personel tahsisi var mı.</param>
/// <param name="SatisVar">Kaydedilmiş bir araç satışı var mı (satış süreci başlamış/bitmiş).</param>
/// <param name="KaskoBitis">Yürürlükteki KASKO poliçesinin bitişi (araç başına en geç biten).</param>
/// <param name="KaskoPrim">Aynı kasko poliçesinin primi. <b>NOT:</b> poliçede "kasko BEDELİ"
/// (sigorta değeri) kolonu YOKTUR; sigorta değeri araç kartındaki <c>TsbKaskoDegeri</c> alanıdır.</param>
/// <param name="KrediKurulusu">Aracı finanse eden kredi kuruluşu (en yeni kredi kaydının bankası).</param>
/// <param name="KrediSonTarih">Kredinin son taksit tarihi = başlangıç + taksit sayısı (ay).</param>
public sealed record VehicleListeEk(
    string? AktifKiraSozlesmeNo,
    bool AcikServis,
    bool AcikBaf,
    bool SatisVar,
    DateTimeOffset? KaskoBitis,
    decimal? KaskoPrim,
    string? KrediKurulusu,
    DateTimeOffset? KrediSonTarih);

/// <summary>FAZ-28 detaylı liste filtresi.</summary>
public sealed class VehicleDetayFilter
{
    /// <summary>Plaka / marka / model / belge no / ruhsat sahibi içinde geçen metin.</summary>
    public string? Ara { get; set; }
    /// <summary>Şube (tam eşleşme) — "Ofis" filtresi.</summary>
    public string? Sube { get; set; }
    public Domain.Enums.VehicleStatus? Durum { get; set; }
    public int EnFazla { get; set; } = 1000;
}
