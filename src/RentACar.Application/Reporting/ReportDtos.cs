using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Reporting;

/// <summary>
/// Düz defter satırı (repo'dan toplama için). Base = Amount×Rate (yerel para) repo'da hesaplanır.
/// </summary>
public sealed record LedgerRowDto(
    DateTimeOffset Tarih, LedgerAccountType AccountType, LedgerDirection Direction,
    string SourceType, string? Aciklama, decimal Base);

/// <summary>Defter satırı (yürüyen bakiyeli) — kasa/banka defteri görünümü.</summary>
public sealed record LedgerLineDto(
    DateTimeOffset Tarih, string SourceType, string? Aciklama,
    decimal Borc, decimal Alacak, decimal YuruyenBakiye);

/// <summary>Kasa & banka giriş/çıkış/bakiye özeti (yerel para, base).</summary>
public sealed record CashboxSummaryDto(
    decimal KasaGiris, decimal KasaCikis, decimal KasaBakiye,
    decimal BankaGiris, decimal BankaCikis, decimal BankaBakiye);

/// <summary>Cari defter satırı (bakiye/yaşlandırma için) — ad çözümlenmiş, base tutar.</summary>
public sealed record CariLedgerRowDto(
    Guid CariId, string Ad, LedgerDirection Direction, decimal Base, DateTimeOffset Tarih);

/// <summary>Cari bakiye satırı. Pozitif = müşteri borçlu (alacağımız).</summary>
public sealed record CariBalanceDto(Guid CariId, string Ad, decimal Bakiye);

/// <summary>
/// Cari borç yaşlandırma (v1: BRÜT borç — tahsilat FIFO mahsubu yok). Borç (Debit) satırları
/// yaşa göre kovalanır. Toplam = kovaların toplamı (net bakiye DEĞİL).
/// </summary>
public sealed record AgingRowDto(
    Guid CariId, string Ad, decimal B0_30, decimal B31_60, decimal B61_90, decimal B90Plus, decimal Toplam);

/// <summary>SourceType kırılım kalemi (gelir/gider drill-down).</summary>
public sealed record GelirGiderKalemDto(string SourceType, decimal Tutar);

/// <summary>Dönem gelir-gider özeti + KDV + net kâr + kaynak kırılımı (base para).</summary>
public sealed record GelirGiderDto(
    decimal GelirToplam, decimal GiderToplam,
    decimal KdvTahsil, decimal KdvIndirilecek, decimal NetKar,
    IReadOnlyList<GelirGiderKalemDto> GelirKirilim,
    IReadOnlyList<GelirGiderKalemDto> GiderKirilim);
