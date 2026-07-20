# Export Paritesi — TürevRent Excel/PDF çıktıları vs RentACar (klon)

> Salt-okunur canlı tarama (2026-07-08, 6 paralel agent, curl+çerez). Yalnız **yapı**: ekran adı + buton varlığı +
> grid sütun başlıkları. Müşteri verisi taranmadı/kaydedilmedi. Ham HTML repo-dışı (`/tmp`, `~/Desktop/turev-deneme`).

> **GÜNCELLEME (2026-07-20, PR-B):** Aşağıdaki tablo BAYAT — "EKSİK export" denenlerin çoğu tarama tarihinden sonra
> bağlandı. Repo GERÇEĞİ: liste export'ları ~17 + rapor export'ları ~22 + personel (PII-gate) = ~40 uç bağlı
> (araclar, cariler, faturalar, cezalar, giderler, nakit-islemler, arac-satislari/-siparisleri/-kredileri, baflar,
> kiralar, rezervasyonlar, lokasyonlar, drop, filo-kiralama, vade + tüm raporlar). **Banka işlem/defter** zaten
> `/raporlar/export/kasa-banka` (hesap-seçmeli yürüyen bakiye) ile karşılanıyor. PR-B ile eklendi: **cari ekstre**
> (`/listeler/export/cari-ekstre?cariId=` — satır-detay + yürüyen bakiye) + **fatura sütun-derinliği** (6→13:
> +cari/vade/para/kur/tür/damga/e-fatura). **Hâlâ gerçekten eksik:** HGS geçiş listesi (ham veri `IHgsService`
> STUB — kimlik gelince) ve müşteri CRM/ciro (Application servisi henüz yok) → kimlik/servis-derinliği bekliyor.

## Özet
- **TürevRent'te Excel export: ~65 ekran** (152 sayfanın liste/rapor olanları; form/master/takvim/grafik'te export yok).
- **RentACar'da export: 3 liste + ~20 rapor = ~23** (`/listeler/export/{araclar,cariler,faturalar}` + `/raporlar/export/*`).
- **PDF/Yazdır (TürevRent):** neredeyse hiç yok — tüm rapor çıktısı **Excel**. Tek PDF: `fatura.aspx → Fatura_Cikti.Aspx`
  (fatura çıktısı) + `kira_listesi` "Yazdır" (HTML). Sözleşme çıktısı Kiralama'dan ayrı. **Bizde sözleşme + fatura PDF
  ZATEN VAR** → PDF tarafında açık YOK; asıl fark **Excel export genişliği + sütun derinliği**.
- **Export mekanizması (TürevRent):** hepsi DevExpress ASPxGridView "Excel'e Aktar" postback'i (viewstate+filtre).
  Bizde: ClosedXML ile GET `/…/export?format=excel`. Fonksiyonel eşdeğer; onların gridi çok daha zengin.

## Kategori bazlı karşılaştırma

### Filo / Araç
| TürevRent ekranı | Excel | Bizde | Durum |
|---|---|---|---|
| arac_listesi (ana filo, ~11 sütun) | E | araclar (6 sütun) | VAR ama sütun sığ |
| detayli_arac_listesi (**67 sütun**: şasi/motor/alım-satış/kasko/HGS/maliyet…) | E | — | **EKSİK (derin)** |
| bos_arac_listesi / bos_arac_raporu (boş/müsait araç) | E | availability var, export yok | **EKSİK export** |
| bos_km_detay | E | km-detay (kısmi) | VAR (kısmi) |
| arac_durum_takip | E | arac-durum-takip | VAR |
| arac_gunluk_durum | E | gunluk (kısmi) | VAR (kısmi) |
| arac_gelir_gider_tablosu (**5 ayrı Excel**: araç/detay/grup/hizmet/şube P&L) | E×5 | gelir-gider (tek, sığ) | **EKSİK (zengin P&L)** |
| arac_satis_ara / arac_satis_bedeli (satış + kâr-zarar 4-grid) | E | araç satış VAR, export yok | **EKSİK export** |
| arac_siparis_listesi / _detay_listesi (araç sipariş dosyası) | E | AracSiparis VAR, export yok | **EKSİK export** |
| arac_kredi_listesi / kredi_takip_listesi (araç finansman/taksit) | E | AracKredi VAR, export yok | **EKSİK export** |
| sigorta_muayene (sigorta/muayene vade takibi) | E | vade panosu VAR, export yok | **EKSİK export** |
| sigorta_gider_ara | E | gider var | **EKSİK export** |
| broker_musaitlik_listesi / musait_arac_listesi (broker fiyat/müsaitlik) | E | — | **EKSİK (broker)** |
| arac_grubu (master + Excel) | E | grup raporu | VAR (kısmi) |

### Cari / CRM / Personel
| TürevRent ekranı | Excel | Bizde | Durum |
|---|---|---|---|
| musteri_listesi / musteri_genel_liste (TC/Pasaport/adres, ~15 sütun) | E | cariler (4 sütun) | VAR ama çok sığ |
| musteri_crm (müşteri ciro/CRM analiz) | E | CRM liste var, export yok | **EKSİK export** |
| genel_borc_alacak (borç/alacak + mutabakat) | E | cari-bakiye/yaslandirma (kısmi) | VAR (kısmi) |
| extre_ozeti / hesap_extresi (cari ekstre) | E | ekstre ekranı var, export yok | **EKSİK export** |
| anket_listesi / sikayet_listesi (anket/şikayet — CRM) | E | — | **EKSİK (feature)** |
| musterigelenmesajlar (gelen mesaj kutusu) | E | — | **EKSİK (feature)** |
| rezsartlar (rez şart/talep takibi) | E | — | **EKSİK** |
| personel_listesi (TC/tablet) | E | Personel var, export yok | **EKSİK export** |

### Finans (defter / fatura / ceza-HGS-hukuk)
| TürevRent ekranı | Excel | Bizde | Durum |
|---|---|---|---|
| kdv_raporu (**çok-oranlı** 20/10 KDV) | E | kdv-listesi (tek oran) | VAR ama tek-oran |
| tahsilat_raporu | E | tahsilat-fatura (kısmi) | VAR (kısmi) |
| genel_kasa / kasa_dagilimi (kasa defteri) | E | kasa-banka defteri | VAR (kısmi) |
| banka_hesap_hareketleri / banka_islem_ara (**28 sütun**) / banka_virman_islem_ara | E | banka işlemleri var, export yok | **EKSİK export** |
| nakit_islem_ara (kasa işlem arama) | E | tahsilat/ödeme var, export yok | **EKSİK export** |
| fatura_detay_listesi / fatura_donem_raporu | E | fatura-donem (kısmi) | VAR (kısmi) |
| fatura_islem_listesi (**Excel + e-Fatura XML**) | E+XML | — | **EKSİK (e-Fatura)** |
| gelen_e_fatura_listesi (gelen e-Fatura kutusu) | E | — | **EKSİK (e-Fatura)** |
| gelir_tablosu (şube bazlı P&L) | E | gelir-gider (kısmi) | VAR (kısmi) |
| ceza_listesi (**29 sütun**) / ceza_gecis_listesi | E | Ceza var, export yok | **EKSİK export** |
| hgs_gecis_listesi (+Özet Excel) | E | HGS yansıtma var, liste-export yok | **EKSİK export** |
| hukuk_islem_listesi (dava/icra) | E | — | **EKSİK (feature)** |
| kabis_raporu (KABİS log) | E | KABİS stub | **EKSİK export** |
| gider_ara (gider arama) | E | gider var, arama-export yok | **EKSİK export** |

### Rezervasyon / Kira
| TürevRent ekranı | Excel | Bizde | Durum |
|---|---|---|---|
| rezervasyon_listesi (**100+ sütun**: sigorta paketleri/drop/LCF/utm kampanya/taksit-vade) | E | rezervasyon var, export yok | **EKSİK export (dev)** |
| rezervasyon_kaynak_raporu | E | rezervasyon-kaynak | VAR |
| kira_listesi (Excel + **Yazdır**) | E | kira var, export yok | **EKSİK export** |
| filo_arac_kiralama / filo_kiralama_listesi (filo kiralama) | E | FiloKiralama var, export yok | **EKSİK export** |
| baf_ara (BAF işlem arama) | E | Baf var, export yok | **EKSİK export** |
| extralar_raporu (ek hizmet) | E | ek-hizmet | VAR |

### Tanım / Lokasyon / Özel
| TürevRent ekranı | Excel | Bizde | Durum |
|---|---|---|---|
| lokasyonlar / lokasyon_sube_ara (drop/transfer kuralları) | E | Location/DropTanim var, export yok | **EKSİK export** |
| rezervasyon_kaynagi (master) | E | var | VAR |
| doluluk_grafik | E | doluluk | VAR |
| karsilastirmali_durum_analizi | E | — | **EKSİK** |
| rakip_fiyat_analizi (rakip fiyat) | E | — | **EKSİK (feature)** |
| web_site_yonetimi (çok-dilli CMS, Excel içe/dışa) | E | — | **EKSİK (feature — kapsam dışı)** |

## Sonuç — açık türleri
1. **Sütun-derinliği (VAR ama sığ):** araç (6→11/67), cari (4→15), KDV (tek→çok oran), gelir-gider/kasa/fatura raporları.
   → Mevcut export'lara sütun ekle (kolay, feature gerektirmez).
2. **Export EKSİK, feature VAR (~18 ekran):** kira listesi, rezervasyon listesi, banka defter/işlem arama, nakit işlem arama,
   ceza/HGS listesi, araç satış/sipariş/kredi listesi, sigorta-muayene vade, gider arama, personel, ekstre, cari CRM,
   BAF, filo kiralama, lokasyon/drop. → Sadece grid+export ucu eklemek yeter (feature zaten var).
3. **Export + feature EKSİK (~8):** e-Fatura giden/gelen (XML), hukuk modülü, anket/şikayet, gelen-mesaj kutusu,
   rakip fiyat analizi, broker müsaitlik, karşılaştırmalı analiz, web CMS. → Önce feature, sonra export.
4. **PDF/Sözleşme:** AÇIK YOK. TürevRent rapor çıktıları Excel-only; tek PDF fatura çıktısı + (bizde olan) sözleşme.

**Tavsiye sırası:** (1) mevcut export'ların sütunlarını zenginleştir → (2) feature'ı olup export'u eksik ~18 ekranı bağla
(mekanik, hızlı) → (3) feature-eksikleri roadmap'e (e-Fatura kimlik-bağlı, CMS/rakip-fiyat kapsam kararı).
