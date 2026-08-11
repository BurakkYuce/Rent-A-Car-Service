// Mali tablolar: defter, kasa/banka, fatura(+satir), gider, donem kilidi.
// NOT: HasQueryFilter BURAYA YAZILMAZ — AppDbContext.OnModelCreating'deki merkezi
// dongu tum ITenantOwned entity'lere tenant filtresini otomatik uygular.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Configurations;

// ---- AccountLedgerEntry (tenant-owned; append-only + DB-immutable) ----
internal sealed class AccountLedgerEntryConfig : IEntityTypeConfiguration<AccountLedgerEntry>
{
    public void Configure(EntityTypeBuilder<AccountLedgerEntry> e)
    {
        e.ToTable("AccountLedgerEntries");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.AccountType).HasConversion<int>();
        e.Property(x => x.Direction).HasConversion<int>();
        e.Property(x => x.Description).HasMaxLength(512);
        e.Property(x => x.SourceType).IsRequired().HasMaxLength(32);
        e.Ignore(x => x.SignedBase);
        e.ComplexProperty(x => x.Amount, m =>
        {
            m.Property(p => p.Amount).HasColumnName("Amount_Value").HasColumnType("numeric(19,4)");
            m.Property(p => p.Currency).HasColumnName("Amount_Currency").HasMaxLength(3);
            m.Property(p => p.Rate).HasColumnName("Amount_Rate").HasColumnType("numeric(19,6)");
        });
        e.HasIndex(x => new { x.TenantId, x.AccountType, x.AccountRef });
        e.HasIndex(x => new { x.TenantId, x.SourceType, x.SourceId });
        // HGS yansıtma idempotency: aynı (cari,plaka,dönem) deterministik SourceId ile
        // tek kez yazılabilir. Dengeli çift (Borç/Alacak) Direction'la ayrışır → ikisi de
        // geçer; tekrar eden yansıtma çakışır. Yalnız 'Hgs' kaynak türü için (kısmi).
        e.HasIndex(x => new { x.TenantId, x.SourceType, x.SourceId, x.Direction })
            .IsUnique()
            .HasFilter("\"SourceType\" = 'Hgs'");
        // Cari↔cari virman idempotency: işlem anahtarı (SourceId) verilince çift-submit yutulur.
        // Dengeli çift (hedef Borç / kaynak Alacak) farklı AccountRef'le ayrışır → ikisi de geçer;
        // aynı anahtarla tekrar gönderim çakışır (kısmi, yalnız 'CariVirman'). NOT: kolon kümesi
        // Hgs index'inden (…SourceId,Direction) FARKLI olmalı (…SourceId,AccountRef) ki EF iki ayrı
        // kısmi index üretsin, birini diğerinin yerine düşürmesin.
        e.HasIndex(x => new { x.TenantId, x.SourceType, x.SourceId, x.AccountRef })
            .IsUnique()
            .HasFilter("\"SourceType\" = 'CariVirman'")
            .HasDatabaseName("IX_AccountLedgerEntries_CariVirman_Idem");
        // Kasa↔Banka virman idempotency: işlem anahtarı (SourceId) verilince çift-submit yutulur.
        //
        // FAZ-50 GENİŞLEMESİ — anahtara AccountRef EKLENDİ. Önceden iki bacak yalnız AccountType ile
        // ayrışıyordu (kaynak==hedef reddedildiği için tür farkı garantiydi). Artık Banka-A → Banka-B
        // virmanı MEŞRU: iki bacak da AccountType=Banka olur ve eski anahtar ikinci bacağı benzersizlik
        // ihlaliyle reddederdi. Ayrım artık AccountRef'ten gelir.
        //
        // İndeks migration'da ELLE kuruluyor çünkü NULLS NOT DISTINCT gerekiyor: AccountRef nullable ve
        // hesap seçilmeyen (legacy) virmanda iki bacak da NULL. PG varsayılanında NULL'lar farklı
        // sayıldığından aynı anahtarla ikinci gönderim ÇAKIŞMAZ ve mevcut çift-submit koruması
        // SESSİZCE kaybolurdu. (PG 15+ gerekir; yerel 15.x, CI postgres:16.)
        e.HasIndex(x => new { x.TenantId, x.SourceType, x.SourceId, x.AccountType, x.AccountRef })
            .IsUnique()
            .HasFilter("\"SourceType\" = 'Virman'")
            .HasDatabaseName("IX_AccountLedgerEntries_Virman_Idem");
        // Depozito idempotency (roadmap I3): aynı SourceId ile çift-submit yutulur. Dengeli çift
        // (Borç/Alacak) Direction'la ayrışır → ikisi de geçer; tekrar çakışır. Named overload ile
        // Hgs index'iyle (aynı kolon seti) ÇAKIŞMAYAN ayrı kısmi index üretilir.
        e.HasIndex(x => new { x.TenantId, x.SourceType, x.SourceId, x.Direction }, "IX_AccountLedgerEntries_Depozito_Idem")
            .IsUnique()
            .HasFilter("\"SourceType\" LIKE 'Depozito%'");
        // MTV ödeme idempotency (roadmap J1): SourceId=mtvId; çift-ödeme reddedilir. Ayrı named kısmi index.
        e.HasIndex(x => new { x.TenantId, x.SourceType, x.SourceId, x.Direction }, "IX_AccountLedgerEntries_MtvOdeme_Idem")
            .IsUnique()
            .HasFilter("\"SourceType\" = 'MtvOdeme'");
        // Muayene ödeme idempotency (roadmap J2): SourceId=inspectionId; çift-ödeme reddedilir.
        e.HasIndex(x => new { x.TenantId, x.SourceType, x.SourceId, x.Direction }, "IX_AccountLedgerEntries_MuayeneOdeme_Idem")
            .IsUnique()
            .HasFilter("\"SourceType\" = 'MuayeneOdeme'");
        // Ceza kalemi ödeme idempotency (FAZ-60): SourceId = PenaltyOdeme.Id; ödeme başına tam
        // bir borç + bir alacak. Aynı ödeme satırı iki kez postlanamaz.
        e.HasIndex(x => new { x.TenantId, x.SourceType, x.SourceId, x.Direction }, "IX_AccountLedgerEntries_CezaOdeme_Idem")
            .IsUnique()
            .HasFilter("\"SourceType\" = 'CezaOdeme'");
        // Sigorta ödeme idempotency (roadmap J3): SourceId=policyId; çift-ödeme reddedilir.
        e.HasIndex(x => new { x.TenantId, x.SourceType, x.SourceId, x.Direction }, "IX_AccountLedgerEntries_SigortaOdeme_Idem")
            .IsUnique()
            .HasFilter("\"SourceType\" = 'SigortaOdeme'");
        // Servis yansıtma/rücu idempotency (roadmap J4): SourceId=serviceId; çift-yansıtma reddedilir.
        e.HasIndex(x => new { x.TenantId, x.SourceType, x.SourceId, x.Direction }, "IX_AccountLedgerEntries_ServisYansitma_Idem")
            .IsUnique()
            .HasFilter("\"SourceType\" = 'ServisYansitma'");
        // NOT (PR-A): Dönem kapanış fişi için idempotency index YOK — kapanış tenant başına pg_advisory_xact_lock
        // ile SERİLEŞTİRİLİR (DonemKapanisRepository) ve güncel-bakiye-sıfırlama intrinsik idempotenttir; SourceId
        // taze GUID (meşru yeniden-kapatma engellenmesin — adversarial BULGU 2/3 düzeltmesi).
    }
}

// ---- CashTransaction (tenant-owned; tahsilat belgesi) ----
internal sealed class CashTransactionConfig : IEntityTypeConfiguration<CashTransaction>
{
    public void Configure(EntityTypeBuilder<CashTransaction> e)
    {
        e.ToTable("CashTransactions");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.No).IsRequired().HasMaxLength(32);
        e.Property(x => x.Tip).HasConversion<int>();
        e.Property(x => x.KarsiHesap).HasConversion<int>();
        e.Property(x => x.Aciklama).HasMaxLength(512);
        // FAZ-84: kanal SAF BİLGİ (Masaüstü/Mobil/Tablet) — nullable, DB-level default YOK (migration
        // trap'i: non-null + default 0/sabit değer geçmiş kayıtları YANLIŞ kanala düşürürdü). Geçmiş
        // kayıtlar NULL kalır; yeni kayıtlar CashService'te "Masaüstü" varsayılanı alır (C#-seviyesi).
        e.Property(x => x.Kanal).HasMaxLength(16);
        e.ComplexProperty(x => x.Amount, m =>
        {
            m.Property(p => p.Amount).HasColumnName("Amount_Value").HasColumnType("numeric(19,4)");
            m.Property(p => p.Currency).HasColumnName("Amount_Currency").HasMaxLength(3);
            m.Property(p => p.Rate).HasColumnName("Amount_Rate").HasColumnType("numeric(19,6)");
        });
        e.HasIndex(x => new { x.TenantId, x.No }).IsUnique();
        e.HasIndex(x => new { x.TenantId, x.CariId });
        // FAZ-50 — hangi spesifik kasa/banka hesabindan gectigi. FK YOK (bilincli): hesap tanimi
        // silinse bile mali belge okunabilir kalmali; silinen hesap raporda "hesap belirtilmemis"
        // kovasina duser (null-toleransli okuma karari).
        e.HasIndex(x => new { x.TenantId, x.HesapId });
        // FAZ-84 — Kanal filtresi (/kasa) — sık sorgu, düşük kardinalite; kolon eklemek ucuz.
        // HesapId'den AYRI kavram: biri "hangi spesifik kasa/banka hesabı", diğeri "hangi kanaldan
        // girildi" (Masaüstü/Mobil/Tablet) — karıştırılmaz.
        e.HasIndex(x => new { x.TenantId, x.Kanal });
        // Idempotency: bir işlemin EN FAZLA bir ters kaydı olabilir (yarış güvencesi).
        e.HasIndex(x => new { x.TenantId, x.TersAlinanId })
            .IsUnique()
            .HasFilter("\"TersAlinanId\" IS NOT NULL");
        // Toplu işlem idempotency: aynı IslemAnahtari iki kez yazılamaz (çift-submit batch'i geri alır).
        e.HasIndex(x => new { x.TenantId, x.IslemAnahtari })
            .IsUnique()
            .HasFilter("\"IslemAnahtari\" IS NOT NULL");
    }
}

// ---- Invoice (tenant-owned; append-only + DB-immutable) ----
internal sealed class InvoiceConfig : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> e)
    {
        e.ToTable("Invoices");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.No).IsRequired().HasMaxLength(32);
        e.Property(x => x.Durum).HasConversion<int>();
        e.Property(x => x.NetTutar).HasColumnType("numeric(19,4)");
        e.Property(x => x.KdvTutar).HasColumnType("numeric(19,4)");
        e.Property(x => x.GenelToplam).HasColumnType("numeric(19,4)");
        e.Property(x => x.Currency).HasMaxLength(3);
        e.Property(x => x.Kur).HasColumnType("numeric(19,6)");
        e.Property(x => x.EFaturaEttn).HasMaxLength(64);
        // Vergi/belge alanları (parite #8; bilgi amaçlı, postlamaya yansımaz)
        e.Property(x => x.Otv).HasColumnType("numeric(19,4)");
        e.Property(x => x.TevkifatOran).HasColumnType("numeric(9,4)");
        e.Property(x => x.TevkifatTutar).HasColumnType("numeric(19,4)");
        e.Property(x => x.DamgaVergisi).HasColumnType("numeric(19,4)");
        // Bilgi/belge alanları (FAZ-51; bilgi amaçlı, postlamaya yansımaz)
        e.Property(x => x.IslemSube).HasMaxLength(128);
        e.Property(x => x.EvrakNo).HasMaxLength(64);
        e.Property(x => x.FaturaOzelKod).HasMaxLength(64);
        e.Property(x => x.OdemeTuru).HasMaxLength(32);
        e.Property(x => x.GonderimSekli).HasMaxLength(32);
        e.Property(x => x.KdvSifirSebep).HasMaxLength(128);
        e.HasIndex(x => new { x.TenantId, x.No }).IsUnique();
        e.HasIndex(x => new { x.TenantId, x.CariId });
        // İdempotency: bir kira EN ÇOK bir kez faturalanır (RentalId dolu olduğunda benzersiz).
        e.HasIndex(x => new { x.TenantId, x.RentalId })
            .IsUnique()
            .HasFilter("\"RentalId\" IS NOT NULL");
        // İade idempotency: bir kaynak fatura EN ÇOK bir kez iade edilir (iade.RentalId=null,
        // bu yüzden yukarıdaki kira-index'ine çarpmaz).
        e.HasIndex(x => new { x.TenantId, x.KaynakFaturaId })
            .IsUnique()
            .HasFilter("\"KaynakFaturaId\" IS NOT NULL");
        // Fark faturası idempotency (adversarial Kritik-1 + V6): bir kira için AYNI sıra no'ya ancak TEK fark
        // (eşzamanlı/çift istek aynı sırayı hesaplar → UniqueViolation → yutulur). Yeni ek bedel / fark-iadesi
        // sonrası yeniden kesim yeni sıra alır → serbest. KaynakKiraId lookup'ı da bu index'ten karşılanır.
        e.HasIndex(x => new { x.TenantId, x.KaynakKiraId, x.KaynakKiraFarkSira })
            .IsUnique()
            .HasFilter("\"KaynakKiraId\" IS NOT NULL");
        e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.InvoiceId);
    }
}

internal sealed class InvoiceLineConfig : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> e)
    {
        e.ToTable("InvoiceLines");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Aciklama).HasMaxLength(512);
        e.Property(x => x.Miktar).HasColumnType("numeric(19,4)");
        e.Property(x => x.BirimNetFiyat).HasColumnType("numeric(19,4)");
        e.Property(x => x.KdvOrani).HasColumnType("numeric(9,4)");
        e.Property(x => x.SatirNet).HasColumnType("numeric(19,4)");
        e.Property(x => x.SatirKdv).HasColumnType("numeric(19,4)");
        e.Property(x => x.SatirToplam).HasColumnType("numeric(19,4)");
        e.HasIndex(x => new { x.TenantId, x.InvoiceId });
    }
}

// ---- Expense / Gider (tenant-owned; append-only + DB-immutable) ----
internal sealed class ExpenseConfig : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> e)
    {
        e.ToTable("Expenses");
        e.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.TenantId, x.SubeId }).HasPrincipalKey(b => new { b.TenantId, b.Id }).OnDelete(DeleteBehavior.Restrict); // roadmap F1 (composite tenant-FK; çapraz-tenant referans imkansız)
        e.HasIndex(x => x.SubeId);
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.No).IsRequired().HasMaxLength(32);
        e.Property(x => x.Tip).HasConversion<int>();
        e.Property(x => x.OdemeYontemi).HasConversion<int>();
        e.Property(x => x.KasaBankaHesap).HasConversion<int>();
        e.Property(x => x.Sube).HasMaxLength(64);
        e.Property(x => x.EvrakNo).HasMaxLength(64);
        e.Property(x => x.NetTutar).HasColumnType("numeric(19,4)");
        e.Property(x => x.KdvOrani).HasColumnType("numeric(9,4)");
        e.Property(x => x.KdvTutar).HasColumnType("numeric(19,4)");
        e.Property(x => x.GenelToplam).HasColumnType("numeric(19,4)");
        e.Property(x => x.Currency).HasMaxLength(3);
        e.Property(x => x.Kur).HasColumnType("numeric(19,6)");
        e.Property(x => x.Aciklama).HasMaxLength(512);
        // FAZ-29: hesap bağı composite tenant-FK (çapraz-tenant referans imkânsız); Restrict —
        // kullanılan bir hesabın silinmesi mali belgeyi öksüz bırakmamalı.
        e.HasOne<FinancialAccount>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.FinansalHesapId })
            .HasPrincipalKey(a => new { a.TenantId, a.Id }).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.TenantId, x.No }).IsUnique();
        e.HasIndex(x => new { x.TenantId, x.VehicleId });
        // Toplu gider idempotency: aynı IslemAnahtari iki kez yazılamaz (çift-submit batch'i geri alır).
        e.HasIndex(x => new { x.TenantId, x.IslemAnahtari })
            .IsUnique()
            .HasFilter("\"IslemAnahtari\" IS NOT NULL");
    }
}

// ---- DonemKilidi / Dönem kapanışı (tenant-owned; tenant başına TEK satır, roadmap D2) ----
internal sealed class DonemKilidiConfig : IEntityTypeConfiguration<DonemKilidi>
{
    public void Configure(EntityTypeBuilder<DonemKilidi> e)
    {
        e.ToTable("DonemKilitleri");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.HasIndex(x => x.TenantId).IsUnique();
    }
}

// ---- GelenEFatura / Gelen (satın-alma) e-Fatura triage kutusu (tenant-owned; deftere postalamaz) ----
internal sealed class GelenEFaturaConfig : IEntityTypeConfiguration<GelenEFatura>
{
    public void Configure(EntityTypeBuilder<GelenEFatura> e)
    {
        e.ToTable("GelenEFaturalar");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Ettn).IsRequired().HasMaxLength(64);
        e.Property(x => x.GonderenVkn).IsRequired().HasMaxLength(16);
        e.Property(x => x.GonderenUnvan).IsRequired().HasMaxLength(256);
        e.Property(x => x.Currency).HasMaxLength(3);
        e.Property(x => x.RedNedeni).HasMaxLength(512);
        e.Property(x => x.Aciklama).HasMaxLength(512);
        e.Property(x => x.NetTutar).HasColumnType("numeric(19,4)");
        e.Property(x => x.KdvTutar).HasColumnType("numeric(19,4)");
        e.Property(x => x.GenelToplam).HasColumnType("numeric(19,4)");
        // FAZ-55 (a): KDV oran kırılımı — hepsi nullable (null = "girilmemiş", 0 = "gerçekten sıfır").
        e.Property(x => x.Kdv20Matrah).HasColumnType("numeric(19,4)");
        e.Property(x => x.Kdv20).HasColumnType("numeric(19,4)");
        e.Property(x => x.Kdv10Matrah).HasColumnType("numeric(19,4)");
        e.Property(x => x.Kdv10).HasColumnType("numeric(19,4)");
        e.Property(x => x.Kdv1Matrah).HasColumnType("numeric(19,4)");
        e.Property(x => x.Kdv1).HasColumnType("numeric(19,4)");
        e.Property(x => x.Kdv0Matrah).HasColumnType("numeric(19,4)");
        e.Property(x => x.GiderTipi).HasConversion<int>();
        e.HasIndex(x => new { x.TenantId, x.Ettn }).IsUnique();
        // Plaka filtresi + araç drill-down (Expense'teki (TenantId, VehicleId) deseniyle aynı).
        e.HasIndex(x => new { x.TenantId, x.VehicleId });
    }
}

// ---- DepozitoIrat (FAZ 1.2; mali iz — immutability trigger migration'da) ----
internal sealed class DepozitoIratConfig : IEntityTypeConfiguration<DepozitoIrat>
{
    public void Configure(EntityTypeBuilder<DepozitoIrat> e)
    {
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Tutar).HasColumnType("numeric(19,4)");
        e.Property(x => x.Kur).HasColumnType("numeric(19,6)");
        e.Property(x => x.Currency).HasMaxLength(3);
        e.Property(x => x.Aciklama).HasMaxLength(512);
        e.HasIndex(x => new { x.TenantId, x.CariId });
        e.HasIndex(x => new { x.TenantId, x.RentalId }); // karne/karlilik atıf sorgusu
    }
}

// ---- DisHizmetAlimi (FAZ 4.3 — B2B dış hizmet; MALİ İZ: değişmez, düzeltme ters kayıtla) ----
internal sealed class DisHizmetAlimiConfig : IEntityTypeConfiguration<DisHizmetAlimi>
{
    public void Configure(EntityTypeBuilder<DisHizmetAlimi> e)
    {
        e.ToTable("DisHizmetAlimlari");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.No).IsRequired().HasMaxLength(32);
        e.Property(x => x.AlinanHizmet).IsRequired().HasMaxLength(256);
        e.Property(x => x.HizmetAlinanFirma).HasMaxLength(256);
        e.Property(x => x.HizmetBedeli).HasColumnType("numeric(19,4)");
        e.Property(x => x.TedarikciKomisyonOran).HasColumnType("numeric(9,4)");
        e.Property(x => x.BayiKomisyonOran).HasColumnType("numeric(9,4)");
        e.Property(x => x.VerilecekKomisyonTutar).HasColumnType("numeric(19,4)");
        e.Property(x => x.KomisyonFaturaNo).HasMaxLength(64);
        e.Property(x => x.BayiFaturaNo).HasMaxLength(64);
        e.Property(x => x.IndirimTuru).HasMaxLength(64);
        e.Property(x => x.Currency).HasMaxLength(3);
        e.Property(x => x.Kur).HasColumnType("numeric(19,6)");
        e.Property(x => x.Aciklama).HasMaxLength(512);
        e.Property(x => x.Durum).HasConversion<int>();
        e.HasIndex(x => new { x.TenantId, x.No }).IsUnique();
        e.HasIndex(x => new { x.TenantId, x.RentalId });
        e.HasIndex(x => new { x.TenantId, x.IslemAnahtari }).IsUnique()
            .HasFilter("\"IslemAnahtari\" IS NOT NULL"); // çift-submit çiti (kısmi unique)
    }
}

// ---- CariVirmanBilgi (FAZ-59 — virman künyesi; PARA TAŞIMAZ, mali belge DEĞİL) ----
internal sealed class CariVirmanBilgiConfig : IEntityTypeConfiguration<CariVirmanBilgi>
{
    public void Configure(EntityTypeBuilder<CariVirmanBilgi> e)
    {
        e.ToTable("CariVirmanBilgileri");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();   // defterdeki SourceId ile AYNI
        e.Property(x => x.MakbuzNo).HasMaxLength(32);
        e.Property(x => x.Sube).HasMaxLength(128);
        e.Property(x => x.IslemYapan).HasMaxLength(128);
        e.Property(x => x.Aciklama).HasMaxLength(512);
        e.HasIndex(x => new { x.TenantId, x.Tarih });
        e.HasIndex(x => new { x.TenantId, x.KaynakCariId });
        e.HasIndex(x => new { x.TenantId, x.HedefCariId });
    }
}

// ---- KasaVirmanBilgi (FAZ-50 — kasa/banka virman künyesi; PARA TAŞIMAZ, mali belge DEĞİL) ----
internal sealed class KasaVirmanBilgiConfig : IEntityTypeConfiguration<KasaVirmanBilgi>
{
    public void Configure(EntityTypeBuilder<KasaVirmanBilgi> e)
    {
        e.ToTable("KasaVirmanBilgileri");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();   // defterdeki SourceId ile AYNI
        e.Property(x => x.KaynakTur).HasConversion<int>();
        e.Property(x => x.HedefTur).HasConversion<int>();
        e.Property(x => x.MakbuzNo).HasMaxLength(32);
        e.Property(x => x.Sube).HasMaxLength(128);
        e.Property(x => x.IslemYapan).HasMaxLength(128);
        e.Property(x => x.Aciklama).HasMaxLength(512);
        e.HasIndex(x => new { x.TenantId, x.Tarih });
        e.HasIndex(x => new { x.TenantId, x.KaynakHesapId });
        e.HasIndex(x => new { x.TenantId, x.HedefHesapId });
    }
}

// ---- KapatmaTahsis (FAZ-29 — hangi tahsilat hangi borç kalemini kapattı; PARA POSTLAMAZ) ----
internal sealed class KapatmaTahsisConfig : IEntityTypeConfiguration<KapatmaTahsis>
{
    public void Configure(EntityTypeBuilder<KapatmaTahsis> e)
    {
        e.ToTable("KapatmaTahsisleri");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.KapatilanBaz).HasColumnType("numeric(19,4)");

        // Composite tenant-FK'ler: çapraz-tenant tahsis yapısal olarak imkânsız.
        // Cascade DEĞİL, Restrict: tahsis mali izdir, sessizce silinmemeli.
        e.HasOne<AccountLedgerEntry>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.LedgerEntryId })
            .HasPrincipalKey(l => new { l.TenantId, l.Id }).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<CashTransaction>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.CashTransactionId })
            .HasPrincipalKey(c => new { c.TenantId, c.Id }).OnDelete(DeleteBehavior.Restrict);

        // Kapatılmış kalemleri tek sorguda toplamak için (ekran + servis çiti aynı indeksi kullanır).
        e.HasIndex(x => new { x.TenantId, x.LedgerEntryId });
        e.HasIndex(x => new { x.TenantId, x.CariId });
        e.HasIndex(x => new { x.TenantId, x.CashTransactionId });
    }
}
