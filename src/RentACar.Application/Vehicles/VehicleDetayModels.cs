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
