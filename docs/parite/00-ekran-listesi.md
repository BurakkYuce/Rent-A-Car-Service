# Canlı TürevRent — 156 ekran, güncel parite durumu

**Tarama:** 2026-08-06, canlı `turev2.turevrac.com`, salt-okuma (yalnız GET; tek bir yazma isteği gönderilmedi).
**Yöntem:** ham HTML deterministik ayrıştırıldı (alan/kolon/filtre/aksiyon/export), eşleştirmeyi 9 ajan kanıt zorunluluğuyla yaptı, `✅` iddialarının tamamı elle doğrulandı.

Durum kodları: `✅ TAM` · `🟡 KISMİ` · `❌ YOK` · `⚠ ERİŞİLEMEZ` (kod var, menüde yok) · `🩹 CANLI BOZUK` (canlıda 500/302 — bize parite borcu yazılmaz) · `❓ DOĞRULANAMADI` · `PARA` (tutar doğruluğu ayrı incelendi).

| # | Ekran | Ad | Modül | Durum | Bizdeki karşılık | Alan | Kolon |
|---:|---|---|---|---|---|---:|---:|
| 1 | `anket_listesi.aspx` | Anket Listesi | 03-cari-crm | ❌ YOK | /anketler (`src/RentACar.Web/Components/Pages/Crm/AnketList. | 14 | 36 |
| 2 | `arac_durum_takip.aspx` | Araç Hareket Raporu | 01-arac-filo | 🟡 KISMİ | /raporlar/arac-durum-takip (`src/RentACar.Web/Components/Pag | 18 | 7 |
| 3 | `arac_gelir_gider_tablosu.aspx` | Araç Gelir (Yeni) | 01-arac-filo | PARA — OPUS'A DEVİR | /raporlar/karlilik (`src/RentACar.Web/Components/Pages/Repor | 17 | 64 |
| 4 | `arac_genel_durumu_grafik.aspx` | Araç Park Durumu | 07-raporlar | 🟡 KISMİ | `/raporlar/filo` (`src/RentACar.Web/Components/Pages/Reports | 0 | 13 |
| 5 | `arac_grubu.aspx` | Araç Grubu Tanımı | 02-tanim-master | 🟡 KISMİ | /arac-gruplari (`src/RentACar.Web/Components/Pages/VehicleGr | 51 | 9 |
| 6 | `arac_guncel_durum.aspx` | Araç Listesi | 01-arac-filo | ❌ YOK | /arac-durum (`src/RentACar.Web/Components/Pages/Fleet/FleetS | 49 | 68 |
| 7 | `arac_gunluk_durum.aspx` | Araç Günlük Durum | 01-arac-filo | PARA — OPUS'A DEVİR | /raporlar/karlilik (`src/RentACar.Web/Components/Pages/Repor | 8 | 12 |
| 8 | `arac_kayit.aspx` | Yeni Araç | 01-arac-filo | 🟡 KISMİ | /vehicles/{Id:guid} (`src/RentACar.Web/Components/Pages/Vehi | 152 | 35 |
| 9 | `arac_kredi.aspx` | Yeni Araç Kredi | 01-arac-filo | PARA — OPUS'A DEVİR | /arac-kredi (`src/RentACar.Web/Components/Pages/AracKrediler | 28 | 0 |
| 10 | `arac_kredi_listesi.aspx` | Kredi Listesi | 01-arac-filo | PARA — OPUS'A DEVİR | /arac-kredi (`src/RentACar.Web/Components/Pages/AracKrediler | 12 | 5 |
| 11 | `arac_listesi.aspx` | Tüm Araçlar | 01-arac-filo | 🟡 KISMİ | /vehicles (`src/RentACar.Web/Components/Pages/Vehicles/Vehic | 23 | 49 |
| 12 | `arac_mtv_islemleri.aspx` | Yeni MTV Gideri | 01-arac-filo | PARA — OPUS'A DEVİR | /regulasyon (`src/RentACar.Web/Components/Pages/Regulation/R | 54 | 0 |
| 13 | `arac_muayene_islemleri.aspx` | Yeni Muayene Gideri | 01-arac-filo | PARA — OPUS'A DEVİR | /regulasyon (`src/RentACar.Web/Components/Pages/Regulation/R | 53 | 0 |
| 14 | `arac_plan_yonetim.aspx` | Araç Artır & Azalt | 01-arac-filo | ❌ YOK | — (aday bulunamadı) | 20 | 0 |
| 15 | `arac_rac_takvim.aspx` | Çalışma Takvimi | 01-arac-filo | 🟡 KISMİ | /takvim (`src/RentACar.Web/Components/Pages/Bookings/Reserva | 12 | 0 |
| 16 | `arac_sahibi.aspx` | Araç Sahip Grubu | 02-tanim-master | ✅ TAM | /arac-sahipleri (`src/RentACar.Web/Components/Pages/VehicleO | 5 | 1 |
| 17 | `arac_satis.aspx` | Yeni Araç Satış | 01-arac-filo | PARA — OPUS'A DEVİR | /satislar (`src/RentACar.Web/Components/Pages/VehicleSales/V | 44 | 0 |
| 18 | `arac_satis_ara.aspx` | Satış Listesi | 01-arac-filo | PARA — OPUS'A DEVİR | /satislar (`src/RentACar.Web/Components/Pages/VehicleSales/V | 16 | 36 |
| 19 | `arac_satis_bedeli.aspx` | Araç Satış Hesaplama | 01-arac-filo | PARA — OPUS'A DEVİR | ❓ belirsiz — en yakın adaylar /raporlar/karlilik, /raporlar/ | 9 | 27 |
| 20 | `arac_segment.aspx` | Araç Segment Tanımı | 02-tanim-master | ✅ TAM | /segmentler (`src/RentACar.Web/Components/Pages/VehicleSegme | 5 | 1 |
| 21 | `arac_servis_islemleri.aspx` | Yeni Servis & Bakım | 01-arac-filo | PARA — OPUS'A DEVİR | /servisler (`src/RentACar.Web/Components/Pages/ServiceRecord | 114 | 7 |
| 22 | `arac_sigorta_islemleri.aspx` | Yeni Sigorta Gideri | 01-arac-filo | PARA — OPUS'A DEVİR | /regulasyon (`src/RentACar.Web/Components/Pages/Regulation/R | 77 | 9 |
| 23 | `arac_siparis.aspx` | Yeni Sipariş | 01-arac-filo | PARA — OPUS'A DEVİR | /arac-siparis (`src/RentACar.Web/Components/Pages/AracSipari | 46 | 22 |
| 24 | `arac_siparis_detay_listesi.aspx` | Araç Siparis Detayı | 01-arac-filo | PARA — OPUS'A DEVİR | /arac-siparis (`src/RentACar.Web/Components/Pages/AracSipari | 12 | 21 |
| 25 | `arac_siparis_listesi.aspx` | Araç Siparis Listesi | 01-arac-filo | PARA — OPUS'A DEVİR | /arac-siparis (`src/RentACar.Web/Components/Pages/AracSipari | 12 | 4 |
| 26 | `arac_tipi_tanimlama.aspx` | Tip Tanımı | 02-tanim-master | ✅ TAM | /arac-tipleri (`src/RentACar.Web/Components/Pages/VehicleTyp | 12 | 5 |
| 27 | `ayarlar.aspx` | Ayarlar | 08-sistem | ❌ YOK | /ayarlar (`Settings/Ayarlar.razor`) + /belge-sablonlari (`Se | 148 | 2 |
| 28 | `baf_ara.aspx` | Baf Listesi | 01-arac-filo | ❌ YOK | /baf (`src/RentACar.Web/Components/Pages/Baflar/BafList.razo | 17 | 17 |
| 29 | `baf_islemleri.aspx` | Yeni Baf İşlemi | 01-arac-filo | 🟡 KISMİ | /baf (`src/RentACar.Web/Components/Pages/Baflar/BafList.razo | 49 | 0 |
| 30 | `bakiye_islem.aspx` | Cari Alacaklandırma | 05b-finans-kasa | PARA — OPUS'A DEVİR | — (gerçek karşılık yok; en yakın kavramsal komşular `/cari-v | 25 | 0 |
| 31 | `bakiye_islem_ara.aspx` | Alacaklandırma Listesi | 05b-finans-kasa | 🩹 CANLI BOZUK | — (canlı yetki reddine düşüyor, profil boş) | 0 | 0 |
| 32 | `banka_hesap_hareketleri.aspx` | Banka Hesap Hareketleri | 05b-finans-kasa | ⚠ ERİŞİLEMEZ | `/raporlar/kasa-banka` (`Reports/KasaBankaDefteri.razor`) —  | 12 | 9 |
| 33 | `banka_islem_ara.aspx` | K.Kartı Tahsilat Listesi | 05b-finans-kasa | ⚠ ERİŞİLEMEZ | `/raporlar/kasa-banka` (`Reports/KasaBankaDefteri.razor`) —  | 18 | 28 |
| 34 | `banka_islemleri.aspx` | K.Kartı Tahsilatı | 05b-finans-kasa | PARA — OPUS'A DEVİR | `/finans/tahsilat` (form, `Customers/CustomerStatement.razor | 95 | 0 |
| 35 | `banka_nakit_listesi.aspx` | Para Yatırma Listesi | 05b-finans-kasa | ⚠ ERİŞİLEMEZ | `/raporlar/kasa-banka` (`Reports/KasaBankaDefteri.razor`) —  | 10 | 0 |
| 36 | `banka_para_islem.aspx` | Para Giriş(Serbest) | 05b-finans-kasa | 🩹 CANLI BOZUK | — (canlı Runtime Error / HTTP 500, profil boş) | 0 | 0 |
| 37 | `banka_para_listesi.aspx` | Para Giriş Listesi | 05b-finans-kasa | ⚠ ERİŞİLEMEZ | `/raporlar/kasa-banka` (`Reports/KasaBankaDefteri.razor`) —  | 9 | 0 |
| 38 | `banka_virman.aspx` | Banka Virman | 05b-finans-kasa | PARA — OPUS'A DEVİR | `/kasa` (Virman formu, `Finance/KasaHub.razor` + `POST /fina | 24 | 0 |
| 39 | `banka_virman_islem_ara.aspx` | Banka Virman Listesi | 05b-finans-kasa | ❌ YOK | — (yakın adayı yok; `/kasa` son-işlemler listesi virman kayı | 7 | 7 |
| 40 | `bos_arac_listesi.aspx` | Boştaki Araçlar 0 0% | 01-arac-filo | 🟡 KISMİ | /musaitlik (`src/RentACar.Web/Components/Pages/Availability/ | 12 | 24 |
| 41 | `bos_arac_raporu.aspx` | Tarih Araç Raporu | 07-raporlar | 🟡 KISMİ | `/raporlar/arac-durum-takip` (`src/RentACar.Web/Components/P | 11 | 5 |
| 42 | `bos_km_detay.aspx` | KM Detay Listesi | 07-raporlar | 🟡 KISMİ (düşük güvenle — bkz Not) | `/raporlar/km-detay` (`src/RentACar.Web/Components/Pages/Rep | 14 | 14 |
| 43 | `broker_musaitlik_listesi.aspx` | Müsaitlik-Rez Açma | 06-fiyat-sigorta | PARA — OPUS'A DEVİR | `/musaitlik` (`src/RentACar.Web/Components/Pages/Availabilit | 26 | 17 |
| 44 | `broker_yasaklari.aspx` | Stop Sell (Yasaklar) | 06-fiyat-sigorta | 🟡 KISMİ | `/broker-yasaklari` (`src/RentACar.Web/Components/Pages/Brok | 17 | 0 |
| 45 | `cari_virman.aspx` | Cari Virman | 05b-finans-kasa | PARA — OPUS'A DEVİR | `/cari-virman` (`Finance/CariVirman.razor`, `POST /finans/ca | 25 | 0 |
| 46 | `cari_virman_islem_ara.aspx` | Cari Virman Listesi | 05b-finans-kasa | ❌ YOK | — (bileşen parçaları var ama tek liste yok) | 8 | 0 |
| 47 | `ceza_gecis_listesi.aspx` | Trafik Ceza Listesi | 05b-finans-kasa | ❌ YOK | — (`/cezalar` farklı iş yapıyor) | 21 | 13 |
| 48 | `ceza_listesi.aspx` | Ceza & Geçiş Listesi | 05b-finans-kasa | ❌ YOK | `/cezalar` (`Penalties/PenaltyList.razor`) var ama filtresiz | 17 | 29 |
| 49 | `cezalar.aspx` | Yeni Ceza & Geçiş | 05b-finans-kasa | PARA — OPUS'A DEVİR | `/cezalar` (create formu, `Penalties/PenaltyList.razor`, `PO | 54 | 0 |
| 50 | `cikis.aspx` | Çıkış | 08-sistem | ❓ DOĞRULANAMADI | `POST /auth/logout` (form: `Components/Layout/MainLayout.raz | 0 | 0 |
| 51 | `default.aspx` |  | 08-sistem | ❌ YOK | / (`Pages/Home.razor`) | 9 | 21 |
| 52 | `detayli_arac_listesi.aspx` | Detaylı Araç Listesi | 02-tanim-master | 🟡 KISMİ | /vehicles (`src/RentACar.Web/Components/Pages/Vehicles/Vehic | 8 | 67 |
| 53 | `dis_banka_entegrasyon_listesi.aspx` | Banka Entegrasyonu | 05b-finans-kasa | 🩹 CANLI BOZUK | — (canlı Runtime Error / HTTP 500, profil boş) | 0 | 0 |
| 54 | `doluluk_algoritma.aspx` | Dinamik Fiyatlama | 06-fiyat-sigorta | PARA — OPUS'A DEVİR | `/doluluk-kurallari` (`src/RentACar.Web/Components/Pages/Dol | 24 | 0 |
| 55 | `doluluk_grafik.aspx` | Doluluk Raporu | 07-raporlar | 🟡 KISMİ | `/raporlar/doluluk` (`src/RentACar.Web/Components/Pages/Repo | 16 | 6 |
| 56 | `evrak_listesi.aspx` | Evrak Listesi | 08-sistem | 🩹 CANLI BOZUK | /belge-sablonlari (`Settings/BelgeSablonList.razor`) — ad be | 0 | 0 |
| 57 | `extralar_raporu.aspx` | Ek Hizmet Raporu | 07-raporlar | 🟡 KISMİ | `/raporlar/ek-hizmet` (`src/RentACar.Web/Components/Pages/Re | 13 | 19 |
| 58 | `extre_ozeti.aspx` | Extre Özeti | 05b-finans-kasa | ❌ YOK | — (en yakın kavramsal `/raporlar/cari-bakiye`, ama farklı gr | 6 | 6 |
| 59 | `fatura.aspx` | Yeni Satış Faturası | 05a-finans-fatura | PARA — OPUS'A DEVİR | `/faturalar` içindeki "Manuel Fatura" formu (`src/RentACar.W | 100 | 0 |
| 60 | `fatura_detay_listesi.aspx` | Satış Detay Listesi | 05a-finans-fatura | PARA — OPUS'A DEVİR | `—` (eşleşen rota bulunamadı) | 15 | 46 |
| 61 | `fatura_donem_raporu.aspx` | Fatura Dönemleri (Taksit) | 05a-finans-fatura | PARA — OPUS'A DEVİR | `/raporlar/fatura-donem` (`src/RentACar.Web/Components/Pages | 12 | 11 |
| 62 | `fatura_islem_listesi.aspx` | Onay Bekleyen Faturalar | 05a-finans-fatura | PARA — OPUS'A DEVİR | `/faturalar` (`src/RentACar.Web/Components/Pages/Finance/Inv | 19 | 23 |
| 63 | `filo_arac_kiralama.aspx` | Yeni Filo Kiralama | 02-tanim-master | 🟡 KISMİ | /filo-kiralama (`src/RentACar.Web/Components/Pages/FiloKiral | 30 | 11 |
| 64 | `filo_kiralama_listesi.aspx` | Filo Sözleşmeleri | 02-tanim-master | ❌ YOK | /filo-kiralama (`src/RentACar.Web/Components/Pages/FiloKiral | 12 | 4 |
| 65 | `fiyat_grup_tanimlama.aspx` | Tarife Grubu Tanımı | 06-fiyat-sigorta | PARA — OPUS'A DEVİR | `—` (karşılık bulunamadı) | 8 | 3 |
| 66 | `fiyat_kampanya_yonetimi.aspx` | Fiyat Kampanya Yönetimi | 06-fiyat-sigorta | PARA — OPUS'A DEVİR | `/kira-kurallari` (`src/RentACar.Web/Components/Pages/Rental | 3 | 0 |
| 67 | `fonksiyonlar.aspx` |  | 08-sistem | ❓ DOĞRULANAMADI | — (aday bulunamadı) | 0 | 0 |
| 68 | `gelen_e_fatura_listesi.aspx` | Gelen E-Faturalar | 05a-finans-fatura | PARA — OPUS'A DEVİR | `/gelen-efatura` (`src/RentACar.Web/Components/Pages/GelenEF | 22 | 26 |
| 69 | `gelir_tablosu.aspx` | Gelir & Gider Raporu | 07-raporlar | PARA — OPUS'A DEVİR | `/raporlar/karlilik` (`src/RentACar.Web/Components/Pages/Rep | 25 | 40 |
| 70 | `genel_borc_alacak.aspx` | Genel Borç & Alacak Raporu | 05b-finans-kasa | ❌ YOK | `/raporlar/cari-bakiye` (`Reports/CariBakiye.razor`) — kavra | 24 | 15 |
| 71 | `genel_kasa.aspx` | Nakit Kasa Durumu | 02-tanim-master | PARA — OPUS'A DEVİR | /raporlar/kasa-banka (`src/RentACar.Web/Components/Pages/Rep | 16 | 13 |
| 72 | `genel_rapor.aspx` | Tüm Personeli Raporla | 07-raporlar | ❌ YOK | — (eşleşen rota yok) | 22 | 0 |
| 73 | `gider_ara.aspx` | Servis & Bakım Listesi | 05b-finans-kasa | ❌ YOK | `/giderler` (`Expenses/ExpenseList.razor`) — filtresiz | 19 | 47 |
| 74 | `gider_islemleri.aspx` | Yeni Personel Gideri | 05b-finans-kasa | PARA — OPUS'A DEVİR | `/giderler` (create formu, `Expenses/ExpenseList.razor`, `PO | 56 | 0 |
| 75 | `gider_tanimlama.aspx` | Gider Tanımı | 05b-finans-kasa | 🟡 KISMİ | `/gider-turleri` (`ExpenseCategories/ExpenseCategoryList.raz | 6 | 2 |
| 76 | `globalsearch.aspx` |  | 08-sistem | ❓ DOĞRULANAMADI | /ara (`Search/Ara.razor`, menude=hayır) — ad/amaç benzerliği | 0 | 0 |
| 77 | `gunraporu.aspx` | Günlük Faaliyet Raporu | 07-raporlar | ❓ DOĞRULANAMADI | `/raporlar/gunluk` (`src/RentACar.Web/Components/Pages/Repor | 4 | 0 |
| 78 | `hesap_extresi.aspx` | Cari Hesap Extresi | 05b-finans-kasa | ❌ YOK | `/cariler/{Id:guid}/ekstre` (`Customers/CustomerStatement.ra | 21 | 10 |
| 79 | `hesap_no_tanimlama.aspx` | Banka Hesabı Tanımı | 02-tanim-master | 🟡 KISMİ | /hesaplar (`src/RentACar.Web/Components/Pages/FinancialAccou | 14 | 6 |
| 80 | `hesap_para_islem.aspx` | Para Yatırma | 02-tanim-master | PARA — OPUS'A DEVİR | /kasa — Virman formu (`src/RentACar.Web/Components/Pages/Fin | 25 | 0 |
| 81 | `hesap_tanimalama.aspx` | Hesap Kod Tanım | 02-tanim-master | 🟡 KISMİ | /hesap-kodlari (`src/RentACar.Web/Components/Pages/HesapKodl | 6 | 0 |
| 82 | `hgs_gecis_listesi.aspx` | Hgs Geçiş Listesi | 05b-finans-kasa | ❌ YOK | — (bağımsız ekran yok; HGS yalnız kira mega-formu içinde sal | 28 | 16 |
| 83 | `hukuk_birimi.aspx` | Hukuk Birimi | 03-cari-crm | PARA — OPUS'A DEVİR | /hukuk (`src/RentACar.Web/Components/Pages/Legal/HukukList.r | 22 | 0 |
| 84 | `hukuk_islem_listesi.aspx` | Hukuk Dosyaları | 03-cari-crm | PARA — OPUS'A DEVİR | /hukuk (`src/RentACar.Web/Components/Pages/Legal/HukukList.r | 11 | 9 |
| 85 | `iptal_sebepleri.aspx` | İptal Sebep Tanımı | 02-tanim-master | ✅ TAM | /iptal-sebepleri (`src/RentACar.Web/Components/Pages/CancelR | 3 | 1 |
| 86 | `kabis_raporu.aspx` | Kabis Raporu | 07-raporlar | ❌ YOK | — (eşleşen rota yok) | 11 | 7 |
| 87 | `kampanya_ara.aspx` | Kampanya Listesi | 06-fiyat-sigorta | PARA — OPUS'A DEVİR | `/kira-kurallari` (`src/RentACar.Web/Components/Pages/Rental | 6 | 0 |
| 88 | `karsilastirmali_durum_analizi.aspx` | Dönem Analizi | 02-tanim-master | ❌ YOK | — | 7 | 1 |
| 89 | `kasa_dagilimi.aspx` | Genel Tahsilat & Ödeme | 05b-finans-kasa | ⚠ ERİŞİLEMEZ | `/raporlar/kasa-banka` (`Reports/KasaBankaDefteri.razor`) —  | 15 | 36 |
| 90 | `kasa_tanimi.aspx` | Kasa Tanımı | 05b-finans-kasa | 🟡 KISMİ | `/hesaplar` (`FinancialAccounts/FinancialAccountList.razor`) | 7 | 2 |
| 91 | `kasa_virman.aspx` | Kasa Virman | 05b-finans-kasa | PARA — OPUS'A DEVİR | `/kasa` (Virman formu, `Finance/KasaHub.razor`, `POST /finan | 23 | 0 |
| 92 | `kdv_raporu.aspx` | KDV Listesi | 05a-finans-fatura | PARA — OPUS'A DEVİR | `/raporlar/kdv-listesi` (`src/RentACar.Web/Components/Pages/ | 11 | 20 |
| 93 | `kira_listesi.aspx` | Açık Rezervasyonlar 0 0% | 04-kira-rezervasyon | PARA — OPUS'A DEVİR | /kiralar (`src/RentACar.Web/Components/Pages/Bookings/Rental | 61 | 163 |
| 94 | `kira_talep_ara.aspx` | Talep Listesi | 04-kira-rezervasyon | 🩹 CANLI BOZUK | — (doğrulanamadı) | 0 | 0 |
| 95 | `kiralama.aspx` | Yeni Kira | 04-kira-rezervasyon | PARA — OPUS'A DEVİR | /kiralar/yeni + /kiralar/{Id:guid} (`src/RentACar.Web/Compon | 705 | 0 |
| 96 | `kiralama_kurallari.aspx` | Detaylı Kupon Yönetimi | 04-kira-rezervasyon | PARA — OPUS'A DEVİR | /kira-kurallari (`src/RentACar.Web/Components/Pages/RentalRu | 35 | 3 |
| 97 | `kiralama_kurallari_basic.aspx` | Kupon Yönetimi | 04-kira-rezervasyon | PARA — OPUS'A DEVİR | /kira-kurallari (`src/RentACar.Web/Components/Pages/RentalRu | 14 | 0 |
| 98 | `kiralama_sartlari.aspx` | Kiralama Şartları | 04-kira-rezervasyon | 🟡 KISMİ | /kira-kurallari (`src/RentACar.Web/Components/Pages/RentalRu | 10 | 0 |
| 99 | `kredi_takip_listesi.aspx` | Kredi Takip Listesi | 05b-finans-kasa | ❌ YOK | `/arac-kredi` (`AracKredileri/AracKrediList.razor`) — ad ben | 14 | 6 |
| 100 | `kullanicilar.aspx` | Program Kullanıcıları | 08-sistem | ❌ YOK | /kullanicilar (`Users/UserList.razor`) | 132 | 7 |
| 101 | `log_kayit.aspx` | Log Kayıtları | 08-sistem | 🩹 CANLI BOZUK | /denetim (`Audit/AuditList.razor`) — isim benzerliğiyle en y | 0 | 0 |
| 102 | `lokasyon_sube_ara.aspx` | Lokasyon Drop Listesi | 02-tanim-master | ❌ YOK | /drop-tanimlari (`src/RentACar.Web/Components/Pages/DropTani | 7 | 7 |
| 103 | `lokasyonlar.aspx` | Lokasyon Tanımı | 02-tanim-master | ❌ YOK | /lokasyonlar (`src/RentACar.Web/Components/Pages/Locations/L | 58 | 7 |
| 104 | `maliyet_hesaplama.aspx` | Maliyet Hesaplama | 06-fiyat-sigorta | PARA — OPUS'A DEVİR | `/maliyet-hesapla` (`src/RentACar.Web/Components/Pages/Prici | 72 | 0 |
| 105 | `maliyet_hesaplama_ara.aspx` | Maliyet Teklif Listesi | 06-fiyat-sigorta | PARA — OPUS'A DEVİR | `—` | 9 | 5 |
| 106 | `marka_tanim.aspx` | Marka Tanımı | 02-tanim-master | ✅ TAM | /markalar (`src/RentACar.Web/Components/Pages/Brands/BrandLi | 2 | 1 |
| 107 | `mobil_odeme.aspx` | Mobil Ödeme 0 Adet | 08-sistem | PARA — OPUS'A DEVİR | /kasa (`Finance/KasaHub.razor`) — en yakın aday (genel kasa/ | 0 | 6 |
| 108 | `mobil_teslimat.aspx` | Mobil Teslimat 0 Adet | 08-sistem | ❌ YOK | — (aday bulunamadı) | 1 | 4 |
| 109 | `musait_arac_listesi.aspx` | Müsaitlik-Fiyat Verme | 04-kira-rezervasyon | PARA — OPUS'A DEVİR | /musaitlik (`src/RentACar.Web/Components/Pages/Availability/ | 23 | 19 |
| 110 | `musaitlik_durum.aspx` | Müsaitlik Raporu | 04-kira-rezervasyon | 🟡 KISMİ | /musaitlik (`src/RentACar.Web/Components/Pages/Availability/ | 4 | 0 |
| 111 | `musteri_crm.aspx` | Müşteri Analiz | 03-cari-crm | ❌ YOK | /crm (`src/RentACar.Web/Components/Pages/Crm/CrmAnaliz.razor | 11 | 12 |
| 112 | `musteri_genel_liste.aspx` | Cari Listesi | 03-cari-crm | ❌ YOK | /cariler (`src/RentACar.Web/Components/Pages/Customers/Custo | 16 | 41 |
| 113 | `musteri_kayit.aspx` | Yeni Bireysel Cari | 03-cari-crm | 🟡 KISMİ | /cariler/{Id:guid} (`src/RentACar.Web/Components/Pages/Custo | 124 | 0 |
| 114 | `musteri_listesi.aspx` | Müşteri Listesi | 03-cari-crm | 🟡 KISMİ | /cariler (`src/RentACar.Web/Components/Pages/Customers/Custo | 11 | 22 |
| 115 | `musterigelenmesajlar.aspx` | Assistans | 03-cari-crm | ❌ YOK | — (eşleşen rota yok) | 8 | 11 |
| 116 | `nakit_islem.aspx` | Nakit Tahsilat | 05b-finans-kasa | PARA — OPUS'A DEVİR | `/finans/tahsilat` + `/finans/odeme` (formlar, `Customers/Cu | 41 | 0 |
| 117 | `nakit_islem_ara.aspx` | Nakit Tahsilat Listesi | 05b-finans-kasa | ❌ YOK | `/kasa` (`Finance/KasaHub.razor`) — erişilebilir ama filtres | 12 | 13 |
| 118 | `otomatik_servisler.aspx` | Otomatik Servisler | 02-tanim-master | ❌ YOK | — | 6 | 0 |
| 119 | `otomatik_tahsilat.aspx` | Otomatik Tahsilat | 02-tanim-master | PARA — OPUS'A DEVİR | /ayarlar — `donemselOtomatikTahsilat` anahtarı (`src/RentACa | 10 | 0 |
| 120 | `ozel_kod_tanim.aspx` | Araç Kod Tanımı | 02-tanim-master | ✅ TAM | /ozel-kodlar (`src/RentACar.Web/Components/Pages/CustomCodes | 6 | 2 |
| 121 | `para_tanimlama.aspx` | Döviz Tanımı | 02-tanim-master | 🟡 KISMİ | /dovizler (`src/RentACar.Web/Components/Pages/Currencies/Cur | 6 | 3 |
| 122 | `periyodik_servis_raporu.aspx` | Periyodik Servis Raporu | 07-raporlar | 🟡 KISMİ | `/raporlar/periyodik-servis` (`src/RentACar.Web/Components/P | 5 | 12 |
| 123 | `personel_calisma_grafigi.aspx` | Per. Çalışma Tablosu | 03-cari-crm | ❌ YOK | /crm (`src/RentACar.Web/Components/Pages/Crm/CrmAnaliz.razor | 3 | 0 |
| 124 | `personel_kayit.aspx` | Yeni Personel | 03-cari-crm | ❌ YOK | /personel (`src/RentACar.Web/Components/Pages/Personnel/Pers | 45 | 0 |
| 125 | `personel_listesi.aspx` | Personel Listesi | 03-cari-crm | ❌ YOK | /personel (`src/RentACar.Web/Components/Pages/Personnel/Pers | 5 | 7 |
| 126 | `rakip_fiyat_analizi.aspx` | Rakip Fiyat Analiz | 02-tanim-master | ❌ YOK | — | 15 | 1 |
| 127 | `rezervasyon.aspx` | Yeni Rezervasyon | 04-kira-rezervasyon | PARA — OPUS'A DEVİR | /rezervasyonlar (`src/RentACar.Web/Components/Pages/Bookings | 597 | 0 |
| 128 | `rezervasyon_kaynagi.aspx` | Rezervasyon Kaynağı | 04-kira-rezervasyon | PARA — OPUS'A DEVİR | /rezervasyon-kaynaklari (`src/RentACar.Web/Components/Pages/ | 90 | 5 |
| 129 | `rezervasyon_kaynak_raporu.aspx` | Rez. Kaynak Raporu | 07-raporlar | 🟡 KISMİ | `/raporlar/rezervasyon-kaynak` (`src/RentACar.Web/Components | 12 | 5 |
| 130 | `rezervasyon_listesi.aspx` | Onaylı Rezervasyonlar 0 Adet | 04-kira-rezervasyon | PARA — OPUS'A DEVİR | /rezervasyonlar (`src/RentACar.Web/Components/Pages/Bookings | 40 | 102 |
| 131 | `rezsartlar.aspx` | Kurumsal Şartları | 02-tanim-master | ❌ YOK | — | 6 | 9 |
| 132 | `serbest_sms.aspx` | Sms Gönder | 02-tanim-master | ❌ YOK | — | 16 | 0 |
| 133 | `servis_rezervasyon.aspx` | Servis Rezervasyonu 0 Adet | 01-arac-filo | ❌ YOK | — (aday bulunamadı) | 0 | 5 |
| 134 | `servis_tanim_tablosu.aspx` | Periyodik Km Sınırları | 01-arac-filo | 🟡 KISMİ | /servis-tanimlari (`src/RentACar.Web/Components/Pages/Servis | 2 | 6 |
| 135 | `sifre_degistir.aspx` | Şifre Değiştir | 08-sistem | ❌ YOK | /kullanicilar (`Users/UserList.razor`, `POST /kullanicilar/s | 5 | 0 |
| 136 | `sigorta_gider_ara.aspx` | Sigorta İşlemleri | 06-fiyat-sigorta | PARA — OPUS'A DEVİR | `/giderler` (`src/RentACar.Web/Components/Pages/Expenses/Exp | 16 | 20 |
| 137 | `sigorta_muayene.aspx` | Sigorta & Muayene Raporu | 06-fiyat-sigorta | ❌ YOK | `—` (en yakın: `/vade` + `/regulasyon`, ikisi de yetersiz) | 11 | 24 |
| 138 | `sigorta_tarife_listesi.aspx` | Sigorta Tarifeleri | 06-fiyat-sigorta | PARA — OPUS'A DEVİR | `/sigorta-urunleri` (`src/RentACar.Web/Components/Pages/Cove | 114 | 0 |
| 139 | `sikayet_listesi.aspx` | Şikayet Listesi | 03-cari-crm | ❌ YOK | /sikayetler (`src/RentACar.Web/Components/Pages/Crm/SikayetL | 8 | 15 |
| 140 | `sube_tanimlama.aspx` | Şube Tanımı | 02-tanim-master | ❌ YOK | /subeler (`src/RentACar.Web/Components/Pages/Branches/Branch | 80 | 5 |
| 141 | `tabletyonetim.aspx` | Tablet Yönetimi | 02-tanim-master | ❌ YOK | — | 77 | 17 |
| 142 | `tahsilat_raporu.aspx` | Tahsilat Fatura Raporu | 05b-finans-kasa | ❌ YOK | `/raporlar/tahsilat-fatura` (`Reports/TahsilatFatura.razor`) | 14 | 35 |
| 143 | `tarifeler.aspx` | Genel Tarife (NRM) | 06-fiyat-sigorta | PARA — OPUS'A DEVİR | `/tarifeler` (`src/RentACar.Web/Components/Pages/Pricing/Rat | 40 | 0 |
| 144 | `tarifeler_xml.aspx` | XML Tarife (Fiyat Girişi) | 06-fiyat-sigorta | PARA — OPUS'A DEVİR | `/tarife-matris` (`src/RentACar.Web/Components/Pages/RateMat | 54 | 0 |
| 145 | `toplu_gider.aspx` | Toplu Gider Girişi | 02-tanim-master | PARA — OPUS'A DEVİR | /toplu-gider (`src/RentACar.Web/Components/Pages/Finance/Top | 32 | 3 |
| 146 | `toplu_tahsilat.aspx` | Toplu Tahsilat | 02-tanim-master | PARA — OPUS'A DEVİR | /toplu-tahsilat (`src/RentACar.Web/Components/Pages/Finance/ | 24 | 0 |
| 147 | `turevuzak.aspx` | Türev Uzak Erişim | 02-tanim-master | ❌ YOK | — | 0 | 0 |
| 148 | `web_log_kayit.aspx` | Web Log | 08-sistem | 🩹 CANLI BOZUK | — (aday belirsiz — muhtemelen web sitesi erişim logu) | 0 | 0 |
| 149 | `web_rezervasyon.aspx` | Görülmeyen Rezervasyonlar 0 | 08-sistem | 🟡 KISMİ | /rezervasyonlar (`Bookings/ReservationList.razor`) | 0 | 9 |
| 150 | `web_site_yonetimi.aspx` | Web Site Yönetimi | 08-sistem | ❌ YOK | /web-sitesi (`WebSite/WebSiteHub.razor`) + /site-icerik (`We | 181 | 29 |
| 151 | `xml_disardan_arac.aspx` | Broker Araç Ayarları | 02-tanim-master | ❌ YOK | /ice-aktar (`src/RentACar.Web/Components/Pages/Import/IceAkt | 8 | 0 |
| 152 | `xml_disardan_sube.aspx` | Broker Şube Ayarları | 02-tanim-master | ❌ YOK | — | 7 | 0 |
| 153 | `xml_firma_tanim.aspx` | Broker Şube Yönetimi | 02-tanim-master | ❌ YOK | — | 37 | 0 |
| 154 | `xml_fiyat_aktar.aspx` | Broker Fiyat Yönetimi | 02-tanim-master | PARA — OPUS'A DEVİR | /tarife-aktar (`src/RentACar.Web/Components/Pages/Import/Tar | 8 | 0 |
| 155 | `xml_rez_kaynak_tedarikci.aspx` | Broker Tedarikçi Oranları | 02-tanim-master | ❌ YOK | /rezervasyon-kaynaklari (`src/RentACar.Web/Components/Pages/ | 6 | 0 |
| 156 | `yetki_reset.aspx` | Yetkileri Yenile | 08-sistem | 🩹 CANLI BOZUK | /yetki (`Authorization/Yetki.razor`) — isim benzerliğiyle en | 0 | 0 |

**Toplam 156 ekran.** Dağılım: 57× PARA · 48× ❌ · 28× 🟡 · 8× 🩹 · 6× ✅ · 5× ⚠ · 4× ❓
