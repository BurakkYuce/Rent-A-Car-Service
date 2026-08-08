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

        grp.MapGet("/{rapor}", async (string rapor, HttpRequest req, ReportService rs, ReportExportService ex, PdfExportService pdf) =>
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
                "karlilik-grup" => KarlilikOzet(await rs.GetKarlilikOzetAsync("grup", from, to)),
                "karlilik-sube" => KarlilikOzet(await rs.GetKarlilikOzetAsync("sube", from, to)),
                "karlilik-segment" => KarlilikOzet(await rs.GetKarlilikOzetAsync("segment", from, to)),
                "gelir-gider" => GelirGider(await rs.GetGelirGiderAsync(from, to)),
                "kasa-banka" => KasaBanka(hesap, await rs.GetAccountLedgerAsync(hesap, from, to)),
                // FAZ-62: ekrandaki filtre export'a AYNEN taşınır (gördüğün = indirdiğin).
                "cari-bakiye" => CariBakiye(await rs.GetCariBalancesAsync(new CariBakiyeFilter
                {
                    Ara = NullIfEmpty(req.Query["ara"].ToString()),
                    OzelKod = NullIfEmpty(req.Query["ozelKod"].ToString()),
                    Sinif = NullIfEmpty(req.Query["sinif"].ToString()),
                    Doviz = NullIfEmpty(req.Query["doviz"].ToString()),
                    Kurumsal = req.Query["tip"].ToString() switch
                    { "kurumsal" => true, "bireysel" => false, _ => (bool?)null },
                    BakiyeTuru = NullIfEmpty(req.Query["bakiye"].ToString()),
                    MinTutar = FormParse.Dec(req.Query["min"].ToString())
                })),
                // FAZ-27: hacim pivotu — ekrandaki seçim export'a AYNEN taşınır.
                "karsilastirmali-analiz" => KarsilastirmaliAnaliz(await rs.GetKarsilastirmaliAnalizAsync(
                    new RentACar.Application.Reporting.KarsilastirmaliAnalizFilter
                    {
                        Tablo = NullIfEmpty(req.Query["tablo"].ToString()) ?? "Kira",
                        VeriTuru = NullIfEmpty(req.Query["veri"].ToString()) ?? "Adet",
                        Kirilim = NullIfEmpty(req.Query["kirilim"].ToString()) ?? "AracGrubu",
                        Ofis = NullIfEmpty(req.Query["ofis"].ToString()),
                        Bas = from,
                        Bit = to
                    })),
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
                // Araç karnesi (vehicleId zorunlu; bulunamayan/başka-tenant araç → null → 404) + filo analiz.
                "arac-karne" => ToTable(KarneExportKatalog.AracKarne(await rs.GetAracKarneAsync(
                    Guid.TryParse(req.Query["vehicleId"].ToString(), out var vid) ? vid : Guid.Empty, from, to))),
                "filo-analiz" => ToTable(KarneExportKatalog.FiloAnaliz(await rs.GetFiloAnalizAsync(
                    from, to, NullIfEmpty(req.Query["siralama"].ToString())))),
                _ => null
            };
            if (t is null) return Results.NotFound($"Bilinmeyen rapor veya kayıt: {rapor}");

            // ?format=excel(default)|csv|pdf — PDF, liste export'larıyla AYNI generic tablo renderer'ı.
            var fmt = req.Query["format"].ToString().Trim().ToLowerInvariant();
            return fmt switch
            {
                "csv" => Results.File(ex.Csv(t.Headers, t.Rows), "text/csv", $"{rapor}.csv"),
                "pdf" => Results.File(pdf.Table(t.Sheet, t.Headers, t.Rows), "application/pdf", $"{rapor}.pdf"),
                _ => Results.File(ex.Xlsx(t.Sheet, t.Headers, t.Rows),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{rapor}.xlsx")
            };
        });

        return app;
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    /// <summary>Test-edilebilir katalog (KarneExportKatalog) çıktısını yerel Table'a çevirir.</summary>
    private static Table? ToTable(ExportTable? e) => e is null ? null : new(e.Sheet, e.Headers, e.Rows);

    private static Table KV(string sheet, params (string K, object? V)[] kv)
        => new(sheet, new[] { "Metrik", "Değer" }, kv.Select(x => new object?[] { x.K, x.V }).ToList());

    private static Table Karlilik(KarlilikDto d)
    {
        var rows = d.Satirlar.Select(s => new object?[] { s.Plaka, s.Sube, s.Grup, s.Gelir, s.Gider, s.NetKar }).ToList();
        rows.Add(new object?[] { "TOPLAM", null, null, d.ToplamGelir, d.ToplamGider, d.ToplamNetKar });
        return new Table("Kârlılık", new[] { "Plaka", "Şube", "Grup", "Gelir", "Gider", "Net Kâr" }, rows);
    }

    private static Table KarlilikOzet(KarlilikOzetDto d)
    {
        var rows = d.Satirlar.Select(s => new object?[] { s.Boyut, s.AracAdet, s.Gelir, s.Gider, s.NetKar }).ToList();
        rows.Add(new object?[] { "TOPLAM", null, d.ToplamGelir, d.ToplamGider, d.ToplamNetKar });
        return new Table($"Kârlılık ({d.BoyutAdi})", new[] { d.BoyutAdi, "Araç Adet", "Gelir", "Gider", "Net Kâr" }, rows);
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
        => new("Cari Bakiye",
            new[] { "Cari", "Telefon", "Mail Adresi", "Banka", "Döviz", "Borç", "Alacak", "Bakiye" },
            rows.Select(c => new object?[]
            { c.Ad, c.Telefon, c.Email, c.Banka, c.Doviz, c.ToplamBorc, c.ToplamAlacak, c.Bakiye }).ToList());

    /// <summary>FAZ-27 — pivot: ilk kolon kırılım, sonra aylar, en sonda toplam. Ay kolonları
    /// VERİDEN değil pencereden gelir; boş ay da kolon olarak yazılır (ekranla aynı küme).</summary>
    private static Table KarsilastirmaliAnaliz(RentACar.Application.Reporting.KarsilastirmaliAnalizDto d)
    {
        var basliklar = new List<string> { d.Kirilim };
        basliklar.AddRange(d.AyAnahtarlari);
        basliklar.Add("Toplam");

        var satirlar = d.Satirlar.Select(s =>
        {
            var h = new List<object?> { s.Kirilim };
            h.AddRange(d.AyAnahtarlari.Select(a => (object?)s.Ay(a)));
            h.Add(s.Toplam);
            return h.ToArray();
        }).ToList();

        if (d.Satirlar.Count > 0)
        {
            var toplam = new List<object?> { "Toplam" };
            toplam.AddRange(d.AyAnahtarlari.Select(a => (object?)d.AyToplami(a)));
            toplam.Add(d.GenelToplam);
            satirlar.Add(toplam.ToArray());
        }

        return new Table($"Karşılaştırmalı Analiz ({d.Tablo} / {d.VeriTuru})", basliklar.ToArray(), satirlar);
    }

    private static Table Aging(IReadOnlyList<AgingRowDto> rows)
        => new("Yaşlandırma", new[] { "Cari", "0-30", "31-60", "61-90", "90+", "Toplam" },
            rows.Select(a => new object?[] { a.Ad, a.B0_30, a.B31_60, a.B61_90, a.B90Plus, a.Toplam }).ToList());

    private static Table Doluluk(DolulukDto d)
        => KV("Doluluk", ("Araç Sayısı", d.AracSayisi), ("Dönem Gün", d.DonemGun),
            ("Araç-Gün Kapasite", d.AracGun), ("Kira-Gün", d.KiraGun), ("Doluluk %", d.DolulukYuzde));

    private static Table Filo(FleetUtilizationDto d)
        => KV("Filo", ("Toplam", d.Toplam), ("Müsait", d.Musait), ("Kirada", d.Kirada),
            ("Serviste", d.Serviste), ("Pasif", d.Pasif), ("Satıldı", d.Satildi), ("Aktif Kira", d.AktifKira));

    private static Table Servis(IReadOnlyList<ServiceCostSummaryDto> rows)
        => new("Servis Özet", new[] { "Plaka", "Tip", "Toplam", "Adet" },
            rows.Select(s => new object?[] { s.Plaka, s.Tip.ToString(), s.Toplam, s.Adet }).ToList());

    private static Table PeriyodikServis(IReadOnlyList<PeriyodikServisRow> rows)
        => new("Periyodik Servis", new[] { "Plaka", "Güncel KM", "Sonraki Bakım KM", "Kalan KM", "Kaynak" },
            rows.Select(r => new object?[] { r.Plaka, r.GuncelKm, r.SonrakiBakimKm, r.KalanKm, r.Kaynak }).ToList());

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
