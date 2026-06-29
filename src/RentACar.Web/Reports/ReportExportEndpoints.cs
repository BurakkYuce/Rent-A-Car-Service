using RentACar.Application.Authorization;
using RentACar.Application.Reporting;
using RentACar.Domain.Enums;
using RentACar.Web.Identity;

namespace RentACar.Web.Reports;

/// <summary>Rapor export uçları (roadmap B1): GET /raporlar/export/{rapor}?format=excel|csv&amp;from=&amp;to=&amp;asOf=&amp;gun=&amp;hesap=.
/// Salt-okur → ViewReports. Veri ReportService'ten, byte[] ReportExportService'ten.</summary>
public static class ReportExportEndpoints
{
    private sealed record Table(string Sheet, IReadOnlyList<string> Headers, IReadOnlyList<object?[]> Rows);

    public static IEndpointRouteBuilder MapReportExportEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/raporlar/export").RequirePermission(Permission.ViewReports);

        grp.MapGet("/{rapor}", async (string rapor, HttpRequest req, ReportService rs, ReportExportService ex) =>
        {
            var from = FormParse.Date(req.Query["from"].ToString());
            var to = FormParse.Date(req.Query["to"].ToString());
            var asOf = FormParse.Date(req.Query["asOf"].ToString()) ?? DateTimeOffset.UtcNow;
            var gun = FormParse.Date(req.Query["gun"].ToString()) ?? DateTimeOffset.UtcNow;
            var hesap = string.Equals(req.Query["hesap"].ToString(), "Banka", StringComparison.OrdinalIgnoreCase)
                ? LedgerAccountType.Banka : LedgerAccountType.Kasa;
            string? sube = NullIfEmpty(req.Query["sube"].ToString());
            string? grup = NullIfEmpty(req.Query["grup"].ToString());
            string? plaka = NullIfEmpty(req.Query["plaka"].ToString());

            Table? t = rapor switch
            {
                "karlilik" => Karlilik(await rs.GetKarlilikAsync(from, to, sube, grup, plaka)),
                "gelir-gider" => GelirGider(await rs.GetGelirGiderAsync(from, to)),
                "kasa-banka" => KasaBanka(hesap, await rs.GetAccountLedgerAsync(hesap, from, to)),
                "cari-bakiye" => CariBakiye(await rs.GetCariBalancesAsync()),
                "yaslandirma" => Aging(await rs.GetAgingAsync(asOf)),
                "doluluk" => Doluluk(await rs.GetDolulukAsync(from ?? gun.AddMonths(-1), to ?? gun)),
                "filo" => Filo(await rs.GetFleetUtilizationAsync()),
                "servis-ozet" => Servis(await rs.GetServiceCostSummaryAsync(from, to)),
                "periyodik-servis" => PeriyodikServis(await rs.GetPeriyodikServisAsync()),
                "km-detay" => KmDetay(await rs.GetKmDetayAsync(from, to)),
                "rezervasyon-kaynak" => RezKaynak(await rs.GetRezervasyonKaynakAsync(from, to)),
                "fatura-donem" => FaturaDonem(await rs.GetFaturaDonemAsync(from, to)),
                "arac-durum-takip" => AracDurumTakip(await rs.GetAracDurumTakipAsync(from, to)),
                "gunluk" => Gunluk(await rs.GetGunlukFaaliyetAsync(gun)),
                "kdv-listesi" => Kdv(await rs.GetKdvListesiAsync(from, to)),
                "ek-hizmet" => EkHizmet(await rs.GetEkHizmetRaporuAsync(from, to)),
                "tahsilat-fatura" => TahsilatFatura(await rs.GetTahsilatFaturaAsync(from, to)),
                _ => null
            };
            if (t is null) return Results.NotFound($"Bilinmeyen rapor: {rapor}");

            var excel = !string.Equals(req.Query["format"].ToString(), "csv", StringComparison.OrdinalIgnoreCase);
            var bytes = excel ? ex.Xlsx(t.Sheet, t.Headers, t.Rows) : ex.Csv(t.Headers, t.Rows);
            var ct = excel ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" : "text/csv";
            return Results.File(bytes, ct, $"{rapor}.{(excel ? "xlsx" : "csv")}");
        });

        return app;
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static Table KV(string sheet, params (string K, object? V)[] kv)
        => new(sheet, new[] { "Metrik", "Değer" }, kv.Select(x => new object?[] { x.K, x.V }).ToList());

    private static Table Karlilik(KarlilikDto d)
    {
        var rows = d.Satirlar.Select(s => new object?[] { s.Plaka, s.Sube, s.Grup, s.Gelir, s.Gider, s.NetKar }).ToList();
        rows.Add(new object?[] { "TOPLAM", null, null, d.ToplamGelir, d.ToplamGider, d.ToplamNetKar });
        return new Table("Kârlılık", new[] { "Plaka", "Şube", "Grup", "Gelir", "Gider", "Net Kâr" }, rows);
    }

    private static Table GelirGider(GelirGiderDto d)
    {
        var rows = new List<object?[]>
        {
            new object?[] { "Gelir Toplam", d.GelirToplam },
            new object?[] { "Gider Toplam", d.GiderToplam },
            new object?[] { "KDV Tahsil", d.KdvTahsil },
            new object?[] { "KDV İndirilecek", d.KdvIndirilecek },
            new object?[] { "Net Kâr", d.NetKar }
        };
        foreach (var k in d.GelirKirilim) rows.Add(new object?[] { $"Gelir: {k.SourceType}", k.Tutar });
        foreach (var k in d.GiderKirilim) rows.Add(new object?[] { $"Gider: {k.SourceType}", k.Tutar });
        return new Table("Gelir-Gider", new[] { "Kalem", "Tutar" }, rows);
    }

    private static Table KasaBanka(LedgerAccountType hesap, IReadOnlyList<LedgerLineDto> lines)
        => new($"{hesap} Defteri", new[] { "Tarih", "Kaynak", "Açıklama", "Borç", "Alacak", "Yürüyen Bakiye" },
            lines.Select(l => new object?[] { l.Tarih, l.SourceType, l.Aciklama, l.Borc, l.Alacak, l.YuruyenBakiye }).ToList());

    private static Table CariBakiye(IReadOnlyList<CariBalanceDto> rows)
        => new("Cari Bakiye", new[] { "Cari", "Bakiye" },
            rows.Select(c => new object?[] { c.Ad, c.Bakiye }).ToList());

    private static Table Aging(IReadOnlyList<AgingRowDto> rows)
        => new("Yaşlandırma", new[] { "Cari", "0-30", "31-60", "61-90", "90+", "Toplam" },
            rows.Select(a => new object?[] { a.Ad, a.B0_30, a.B31_60, a.B61_90, a.B90Plus, a.Toplam }).ToList());

    private static Table Doluluk(DolulukDto d)
        => KV("Doluluk", ("Araç Sayısı", d.AracSayisi), ("Dönem Gün", d.DonemGun),
            ("Araç-Gün Kapasite", d.AracGun), ("Kira-Gün", d.KiraGun), ("Doluluk %", d.DolulukYuzde));

    private static Table Filo(FleetUtilizationDto d)
        => KV("Filo", ("Toplam", d.Toplam), ("Stokta", d.Stokta), ("Müsait", d.Musait), ("Kirada", d.Kirada),
            ("Serviste", d.Serviste), ("Pasif", d.Pasif), ("Satıldı", d.Satildi), ("Aktif Kira", d.AktifKira));

    private static Table Servis(IReadOnlyList<ServiceCostSummaryDto> rows)
        => new("Servis Özet", new[] { "Plaka", "Tip", "Toplam", "Adet" },
            rows.Select(s => new object?[] { s.Plaka, s.Tip.ToString(), s.Toplam, s.Adet }).ToList());

    private static Table PeriyodikServis(IReadOnlyList<PeriyodikServisRow> rows)
        => new("Periyodik Servis", new[] { "Plaka", "Güncel KM", "Sonraki Bakım KM", "Kalan KM" },
            rows.Select(r => new object?[] { r.Plaka, r.GuncelKm, r.SonrakiBakimKm, r.KalanKm }).ToList());

    private static Table KmDetay(IReadOnlyList<KmDetayRow> rows)
        => new("KM Detay", new[] { "Sözleşme", "Plaka", "Çıkış KM", "Dönüş KM", "Katedilen", "Limit", "Fazla KM", "Fazla Bedel" },
            rows.Select(r => new object?[] { r.SozlesmeNo, r.Plaka, r.CikisKm, r.DonusKm, r.KatedilenKm, r.KmLimit, r.FazlaKm, r.FazlaKmBedeli }).ToList());

    private static Table RezKaynak(IReadOnlyList<RezervasyonKaynakRow> rows)
        => new("Rezervasyon Kaynak", new[] { "Kaynak", "Adet", "Toplam Gün", "Toplam Ciro" },
            rows.Select(r => new object?[] { r.Kaynak, r.Adet, r.ToplamGun, r.ToplamCiro }).ToList());

    private static Table FaturaDonem(IReadOnlyList<FaturaDonemRow> rows)
        => new("Fatura Dönem", new[] { "No", "Tarih", "Vade", "Cari", "Toplam", "Durum", "İade" },
            rows.Select(r => new object?[] { r.No, r.Tarih, r.VadeTarihi, r.Cari, r.GenelToplam, r.Durum, r.IadeMi ? "Evet" : "Hayır" }).ToList());

    private static Table AracDurumTakip(IReadOnlyList<AracDurumTakipRow> rows)
        => new("Araç Durum Takip", new[] { "Gün", "Toplam", "Dolu", "Bakım", "Boş" },
            rows.Select(r => new object?[] { r.Gun.ToString("yyyy-MM-dd"), r.ToplamArac, r.Dolu, r.Bakim, r.Bos }).ToList());

    private static Table Gunluk(GunlukFaaliyetDto d)
        => KV("Günlük Faaliyet", ("Yeni Rezervasyon", d.YeniRezervasyon), ("Yeni Kira", d.YeniKira),
            ("Çıkış", d.Cikis), ("Dönüş", d.Donus), ("Tahsilat Adet", d.TahsilatAdet),
            ("Tahsilat Tutar", d.TahsilatTutar), ("Fatura Adet", d.FaturaAdet), ("Fatura Tutar", d.FaturaTutar));

    private static Table Kdv(KdvListesiDto d)
    {
        var rows = d.Satirlar.Select(s => new object?[] { s.Oran, s.Net, s.Kdv, s.Brut, s.FaturaAdet }).ToList();
        rows.Add(new object?[] { "TOPLAM", d.ToplamNet, d.ToplamKdv, d.ToplamBrut, d.FaturaAdet });
        return new Table("KDV Listesi", new[] { "Oran", "Net", "KDV", "Brüt", "Fatura Adet" }, rows);
    }

    private static Table EkHizmet(EkHizmetRaporDto d)
    {
        var rows = d.Satirlar.Select(s => new object?[] { s.Ad, s.ToplamMiktar, s.Net, s.Kdv, s.Brut, s.KiraAdet }).ToList();
        rows.Add(new object?[] { "TOPLAM", null, d.ToplamNet, d.ToplamKdv, d.ToplamBrut, d.KiraAdet });
        return new Table("Ek Hizmet", new[] { "Ad", "Miktar", "Net", "KDV", "Brüt", "Kira Adet" }, rows);
    }

    private static Table TahsilatFatura(TahsilatFaturaDto d)
        => KV("Tahsilat-Fatura", ("Fatura Adet", d.FaturaAdet), ("Fatura Toplam", d.FaturaToplam),
            ("Tahsilat Adet", d.TahsilatAdet), ("Tahsilat Toplam", d.TahsilatToplam), ("Fark", d.Fark));
}
