# Toplama — 01-arac-filo (25 ekran)

Kaynak plan: `docs/parite/plan/01-arac-filo-plan.md` (+ kanıt: `docs/parite/01-arac-filo.md`).
Faz numara aralığı: **10–19** (`docs/roadmap/FAZ-10-*.md` … `FAZ-19-*.md`).

## BLOKE (D8 — kimlik/credential gerektirir)

**Bu modülde D8 kalemi YOK.** 25 ekranın hiçbiri gerçek entegrasyon (e-Fatura/GİB, SMS, banka/
POS, e-Devlet sorgusu vb.) gerektirmiyor — hepsi kod tabanında yapısal eylemle karşılanabilir
ya da (aşağıdaki iki istisna) araştırma/karar gerektiriyor.

## YAPILMAZ

### arac_kayit.aspx — Sigorta/Muayene/Servis geçmişini VehicleEdit'e SEKME olarak gömme

**Gerekçe (plan dosyasından aynen taşındı):** Sigorta/Trafik/Kasko/Muayene bloğunu
`VehicleEdit.razor`'a sekme olarak gömmek — bu veri zaten `/regulasyon`'da birincil kaynakta
yaşıyor (`InsurancePolicy`/`MtvRecord`/`InspectionRecord`, kendi ödeme akışlarıyla); aynı
veriyi ikinci bir formda tekrar yazılabilir hale getirmek CLAUDE.md §5'in "katmanlı mimari"
ilkesini bozar ve iki kopyayı senkron tutma riski yaratır. Yerine: `VehicleDetail`'e (zaten
var) sigorta/muayene bitiş tarihi özet-rozeti + `/regulasyon` linki (D3, ayrı küçük ek, bu
PR'ın parçası değil — zaten `VehicleDetail`'de kısmen var, doğrulanmalı).

Bu karar `FAZ-10-arac-kayit-alan-zenginlestirme.md`'nin Notlar bölümünde referanslanmıştır,
faz dosyasına yapısal eylem olarak KONULMADI.

## ARAŞTIRMA GEREKLİ (ne bloke ne yapılmaz — üçüncü kategori)

### arac_satis_bedeli.aspx

Bu kalem **D8 değil** (credential gerektirmiyor, çerez-tabanlı canlı erişim zaten mevcut —
bkz. kullanıcı hafızası "TürevRent screen inventory") ve **YAPILMAZ değil** (reddedilmiş bir
karar yok) — plan dosyasının kendi ifadesiyle: "Yapısal eylem YAZILAMAZ — canlı ekranın gerçek
işlevi doğrulanamadı." Bu yüzden ayrı bir faz dosyası ALMADI (efor/dosya/adım verilemiyor,
şablonun "Dokunulacak dosyalar" ve "Efor" alanları dolduramaz).

- **Kanıt:** K2=%0 (0/6, `RentTo`/`Ofis_Durum` kavramları bizde tanımsız), K3=%7 (2/27, yalnız
  Plaka/Marka gibi zayıf tekil eşleşme). "RentTo" kavramı (muhtemelen alt-kiralama/ortak filo
  partneri) kod tabanında `grep`'le bulunamadı. Canlı başlığı jenerik "TürevRent".
- **Önerilen ilk adım:** canlı ekran çerez-erişimiyle tekrar gezilip `RentTo`/`Ofis_Durum`
  (İşlem Ofisi vs Çıkış Ofisi) alan anlamları teyit edilmeli; ancak sonra bir desen/dosya
  iddia edilebilir. En yakın adaylar `/raporlar/karlilik`, `/raporlar/filo-analiz`.
- Bu araştırma tamamlanınca (Opus/kullanıcı kararıyla) yeni bir faz numarası (bu modülün
  10–19 aralığı dışında) açılmalı.

## WIRE-IN (Faz-00'a devredildi — bu modülün faz dosyalarına KONULMADI)

Aşağıdaki 3 alan grubu entity+input+servis katmanında ZATEN var, yalnız form sormuyor
("bedava kazanç" kalıbı). `docs/roadmap/FAZ-00-bedava-kazanclar.md` bu üçünü TAM olarak
kapsıyor (dosya zaten mevcut ve doğrulandı) — bu modülün FAZ-13/FAZ-18 dosyaları bu alanları
BİLEREK ATLADI, tekrar yazmadı:

| Alan | Nerede var | Hangi faz normalde kapsardı |
|---|---|---|
| `AracKredi.VehicleId` | `AracKrediModels.cs` (input), `AracKrediService.cs` (map) | FAZ-13 (arac_kredi) |
| `VehicleSale.HedefFiyat` / `SatisKm` / `SatisKanali` / `Devir` | `VehicleSale.cs`, `VehicleSaleInput.cs` | FAZ-18 (arac_satis) |
| `Baf.DonusTarihi` / `DonusYakit` | `Baf.cs`, `BafService.TeslimAlAsync` parametreleri | FAZ-18 (baf_islemleri) |

`FAZ-00-bedava-kazanclar.md` içinde ayrıca önemli bir para-atfı hatası not edilmiş: kredi
taksitinin gider bacağı `AccountRef = kredi.VehicleId` ile postlanıyor ama form `VehicleId`'yi
hiç doldurmadığı için **her kredi taksiti `AccountRef = null` ile deftere düşüyor** —
Araç Karnesi/Filo Analiz raporlarında "(Atanmamış)" tarafında birikiyor. Bu, FAZ-13'ün
kapsamındaki `CariId`/özet-alan çalışmasından BAĞIMSIZ, öncelikli bir düzeltme.

## Faz listesi

| Faz | Başlık | Kapsanan ekranlar | Efor |
|---|---|---|---|
| FAZ-10 | Araç Kayıt: Alan Zenginleştirmesi | arac_kayit | 1,5 gün |
| FAZ-11 | Araç Görünürlük Ekranları: Aksiyon Konsolu + Liste + Takvim Filtreleri | arac_guncel_durum, arac_listesi, arac_rac_takvim | 3,5 gün |
| FAZ-12 | Araç Durum/Günlük Durum/Gelir-Gider Raporları Derinliği | arac_durum_takip, arac_gunluk_durum, arac_gelir_gider_tablosu | 3,5 gün |
| FAZ-13 | Araç Kredisi Zenginleştirme | arac_kredi, arac_kredi_listesi | 2 gün |
| FAZ-14 | Regülasyon Kısmi Ödeme Genişletmesi (MTV+Muayene+Servis Tanım) | arac_mtv_islemleri, arac_muayene_islemleri, servis_tanim_tablosu | 3,5 gün |
| FAZ-15 | Araç Sigorta Zeyil Alt-Sistemi | arac_sigorta_islemleri | 2,5 gün |
| FAZ-16 | Servis Kaydı: Kaza/Fatura/Ödeme Derinliği + Rezervasyon Durumu | arac_servis_islemleri, servis_rezervasyon | 4 gün |
| FAZ-17 | Araç Sipariş: Cari-FK + Çok-Katmanlı Fiyat + Filtre | arac_siparis, arac_siparis_detay_listesi, arac_siparis_listesi | 2,5 gün |
| FAZ-18 | Araç Satış + Baf: Alan/Filtre Zenginleştirmesi | arac_satis, arac_satis_ara, baf_ara, baf_islemleri | 3 gün |
| FAZ-19 | Filo Plan Yönetimi (Yeni Dikey) + Boş Araç Listesi Zenginleştirmesi | arac_plan_yonetim, bos_arac_listesi | 4 gün |
| **TOPLAM** | **10 faz** | **23 ekran** | **30 gün** |

**Sayım notu:** 25 ekrandan 23'ü yukarıdaki 10 faza dağıtıldı. Kalan 2 ekran: `arac_satis_bedeli`
(ARAŞTIRMA GEREKLİ, yukarıda) faz almadı; `arac_kayit`'in YAPILMAZ alt-kararı zaten ayrı bir
ekran değil (arac_kayit'in içinde, FAZ-10'da kapsanıyor, yalnız bir alt-eylem hariç tutuldu).
3 wire-in alan grubu (AracKredi/VehicleSale/Baf) `FAZ-00-bedava-kazanclar.md`'de zaten var ve
bu modülün fazlarından BİLEREK çıkarıldı (yukarıdaki tablo). 5 çok-ekranlı gruplama plan
dosyasının kendi `**gruplama:**` etiketleriyle uyumlu (arac_kredi+arac_kredi_listesi,
arac_mtv+arac_muayene, arac_satis+arac_satis_ara, arac_siparis üçlüsü, baf_ara+baf_islemleri);
ayrıca slot-bütçesi (10 faz sınırı) nedeniyle 3 ek D3/D4 grubu (arac_durum_takip+
arac_gunluk_durum+arac_gelir_gider_tablosu; arac_guncel_durum+arac_listesi+arac_rac_takvim;
servis_rezervasyon+arac_servis_islemleri) ve 2 küçük "bağımsız-ama-slot-paylaşan" grup
(arac_satis grubu+baf grubu; arac_plan_yonetim+bos_arac_listesi) oluşturuldu — bu ikisi ilgili
faz dosyalarının başında açıkça "2 bağımsız PR, istenirse ayrılabilir" notuyla işaretlendi.

Plan dosyasının belirttiği "~31 gün yapısal toplam" (24 ekran, `arac_satis_bedeli` hariç) ile bu
tablodaki 30 gün tutarlı (küçük yuvarlama farkı, aynı 24 ekranın 23'ü + arac_kayit'in YAPILMAZ
alt-kararı çıkarılmış hali).

## PARA — Opus'a devredilmiş kalemler (faz dosyalarında yapısal eylem yazıldı, karar bekliyor)

Aşağıdaki fazlarda tutar/formül/muhasebe-modeli kararı Opus'a devredilmiştir (her fazın kendi
"Notlar" bölümünde detaylı):
- FAZ-12: "Potansiyel" kolonu formülü + arac_gelir_gider_tablosu'nun A/B pivot seçeneği (bu
  ikinci karar netleşmeden Bölüm C'de kod DEĞİŞMEZ).
- FAZ-13: cari-bağlama modeli (kredi veren mi ilişkili mi) + faiz/vade formülünün canlıyla
  uyuşup uyuşmadığı.
- FAZ-14 (Bölüm A/B): kısmi ödemenin defter dengesi zamanlaması zaten HER ADIMDA yazma olarak
  seçildi (bu faz kendi kararını verdi) — adversarial inceleme bu tercihi sınamalı.
- FAZ-15: Zeyil'in kendi defter kaydını postlayıp postlamayacağı + `AracDegeri`/`ImmDegeri`'nin
  hasar/rücu hesabına girip girmeyeceği.
- FAZ-16: fatura/ödeme bloğunun GERÇEK gider+defter kaydına (`ExpenseType`) bağlanıp
  bağlanmayacağı; işçilik 6-kırılımının sabit-alan mı serbest-satır mı olacağı.
- FAZ-17: hangi fiyat katmanının (Liste/Piyasa/Ops/Filo/Onay) "resmi" sipariş tutarı sayılacağı.
- FAZ-18: `HedefFiyat` vs `SatisNet` ayrımının KDV/kur hesabına etkisi.
