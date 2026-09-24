# F10.3 — Parite ve e2e doğrulaması (Raporlar fazı)

> Kalıp adım 3 ("Doğrulama: parite + e2e"). Kaynak: `F10.md` envanterindeki 26 Blazor rapor sayfası
> (`Components/Pages/Reports/…`) ile SPA'nın TEK ortak rapor ekranı (`src/RentACar.Frontend/src/app/features/reports`
> — #296) ve rapor tanımları (`catalog-*.ts`), 2026-09-24 `main`. Yöntem: her razor dosyasının sorgu
> parametreleri (`SupplyParameterFromQuery`), tabloları, export bağlantıları ve yetki özniteliği; SPA'da görünüm,
> süzgeç, kart, özet tablosu, satır tablosu, export ve rota izniyle karşılaştırıldı. Tutarlar yalnız sunucudan
> (F10.1 uçları; hesap aynı servislerde) — ekran hesap yapmaz. Referans sistemin ekranları bu fazda ayrıca taranmadı
> (Blazor sayfaları referans sistem paritesini zaten taşıyor; bkz. `docs/parite`).

## Özet

| Sayfa (Blazor → SPA, `/app` altında aynı ad)   | Süzgeç (Blazor → SPA)                                                                 | Görünüm / bölüm                                          | Export (sunucu bağlantısı)                   | İzin                                                 | Sonuç                                  |
| ---------------------------------------------- | ------------------------------------------------------------------------------------- | -------------------------------------------------------- | -------------------------------------------- | ---------------------------------------------------- | -------------------------------------- |
| `/raporlar/gelir-gider`                        | from/to → dönem                                                                        | kartlar + gelir / gider kırılımı                         | Excel/CSV/PDF                                | ViewReports                                          | tam                                    |
| `/raporlar/kasa-banka`                         | 8/8 (dönem, hesap türü, hesap, döviz, tür, şube, devir)                                | hesap özetleri + defter satırları                        | var                                          | ViewReports                                          | tam                                    |
| `/raporlar/finans-analiz`                      | from/to → dönem                                                                        | kartlar + trend / gelir / gider / son 30 gün tabloları   | yok (Blazor'da da yok)                       | ViewReports                                          | tam (grafik tablo — bkz. fark)         |
| `/raporlar/virman-gecmisi`                     | 3/3 (dönem, hesap, ara)                                                                | döviz özeti + satırlar                                   | yok (Blazor'da da yok)                       | ViewReports                                          | tam                                    |
| `/raporlar/kdv-listesi`                        | 3/3 (dönem, alış dahil; `mod` → görünüm)                                               | oran özeti + belge bazlı (geniş) görünüm                 | kdv-listesi + kdv-genis                      | ViewReports                                          | tam                                    |
| `/raporlar/cari-bakiye`                        | 7/7 (ara, özel kod, sınıf, döviz, tip, bakiye, en az) + yaşlandırma tarihi             | bakiye + yaşlandırma görünümü                            | cari-bakiye + yaslandirma                    | ViewReports                                          | tam                                    |
| `/raporlar/extre-ozeti`                        | 6/6 (cari, plaka, ofis, dönem, gecikmiş)                                               | satırlar                                                 | yok (Blazor'da da yok)                       | ViewReports                                          | tam                                    |
| `/raporlar/tahsilat-fatura`                    | 6/6 (dönem, ara, durum, bakiye, tutarsız)                                              | özet + mutabakat görünümü                                | var                                          | ViewReports                                          | tam                                    |
| `/raporlar/fatura-donem`                       | 6/6 (dönem, ara, fatura durumu, şube; `sekme` → görünüm)                               | dönem faturaları + kira fatura durumu görünümü           | fatura-donem + kira-fatura-durum             | ViewReports                                          | tam                                    |
| `/raporlar/karlilik`                           | 9/9 (dönem, şube, grup, plaka, kaynak, SIPP, KDV dahil; `boyut` → kırılım görünümü)    | araç satırları (plaka → karne) + kırılım görünümü        | karlilik + karlilik-{boyut}                  | ViewReports                                          | tam                                    |
| `/raporlar/ek-hizmet`                          | 8/8 (dönem, ara, satan personel, ofis, kaynak, sistem gizle; `pivot` → görünüm)        | özet + araç pivotu + detay görünümleri                   | ek-hizmet + ek-hizmet-arac                   | ViewReports                                          | tam                                    |
| `/raporlar/gunluk`                             | 2/2 (gün, şube)                                                                        | günlük faaliyet bölümleri                                | var                                          | ViewReports                                          | tam                                    |
| `/raporlar/arac-karne/{id}`                    | from/to → dönem                                                                        | P&L kartları + yıllık / gelir / gider / olay / vade / maliyet / tut-sat | var                                          | ViewReports                                          | tam                                    |
| `/raporlar/filo-analiz`                        | 3/3 (dönem, sıralama)                                                                  | havuz KPI kartları + yaş kohortu + araç satırları        | var                                          | ViewReports                                          | tam (vade sayısı sütunu — bkz. fark)   |
| `/raporlar/filo`                               | —                                                                                      | durum kartları + şube tablosu                            | var                                          | ViewReports                                          | tam                                    |
| `/raporlar/doluluk`                            | 3/3 (dönem, kırılım)                                                                   | kartlar + gün tablosu                                    | var                                          | ViewReports                                          | tam (grafik tablo — bkz. fark)         |
| `/raporlar/arac-gunluk-durum`                  | 6/6 (gün, ofis, grup, SIPP, araç sahibi, plaka)                                        | satırlar                                                 | var                                          | ViewReports                                          | tam                                    |
| `/raporlar/servis-ozet`                        | from/to → dönem                                                                        | satırlar                                                 | var                                          | ViewReports                                          | tam                                    |
| `/raporlar/rezervasyon-kaynak`                 | 6/6 (dönem, tarih tipi, ofis, grup, iptal dahil)                                       | kaynak tablosu                                           | var                                          | ViewReports                                          | tam                                    |
| `/raporlar/otomatik-servisler`                 | 4/4 (dönem, iş adı, yalnız hatalı)                                                     | son çalışmalar + satırlar                                | yok (Blazor'da da yok)                       | ViewReports                                          | tam                                    |
| `/raporlar/arac-durum-takip`                   | 8/8 (dönem, şube, araç sahibi, grup, SIPP, plaka; `gorunum` → gün / araç)              | gün ve araç görünümleri                                  | arac-durum-takip + -arac                     | OperationsWrite ∨ ViewReports                        | tam                                    |
| `/raporlar/km-detay`                           | from/to → dönem                                                                        | satırlar                                                 | var                                          | OperationsWrite ∨ ViewReports                        | tam                                    |
| `/raporlar/periyodik-servis`                   | 4/4 (plaka, şube, durum, eşik)                                                         | satırlar                                                 | var                                          | OperationsWrite ∨ ViewReports                        | tam                                    |
| `/raporlar/sigorta-muayene`                    | 4/4 (tür, en geç, sahip, plaka)                                                        | satırlar                                                 | yok (Blazor'da da yok)                       | OperationsWrite ∨ ViewReports                        | tam                                    |
| `/raporlar/karsilastirmali-analiz`             | 6/6 (tablo, veri, kırılım, ofis, dönem)                                                | ay × kırılım matrisi + toplam satırı                     | var                                          | OperationsWrite ∨ ViewReports (firma geneli)         | tam (kapsam — bkz. fark)               |
| `/raporlar/personel-calisma`                   | 4/4 (dönem, personel, şube)                                                            | matris + vardiya listesi + **ekle / düzenle / sil**      | yok (Blazor'da da yok)                       | okuma OperationsWrite ∨ ViewReports, yazma OperationsWrite | **eklendi:** vardiya yazma (#302)  |

## Bulunan eksik (F10.3'te eklendi)

1. **Personel çalışma — vardiya ekle / düzenle / sil (#302).** F10.2'de API'de yazma ucu olmadığı için ekran salt
   okunurdu. `/api/ui/v1/vardiyalar` (GET/POST/PUT/DELETE) eklendi. Kurallar Blazor ile AYNI servis yolunda:
   - zorunlu alanlar, sıfır süre reddi, gece vardiyası, kesişen aralık reddi, tanımlı şube;
   - kapsam vardiyanın şubesine göre;
   - tam değiştirmede `surum` ve 409 `cakisma`.

   SPA'da rapor ekranına gömülü "Vardiya Ekle / Düzenle" formu ve işlemli vardiya listesi var (varsayılanlar
   Blazor'la aynı: pencerenin ilk günü, 08:00–18:00). Çitler: `UiShiftTests`, `shift-model.spec.ts`,
   `shift-editor.spec.ts`.
2. **SPA içi bağlantılar.** Panel (filo analiz, finans analiz, "km'si geçen bakım" kutusu) ve araç ekranları (liste,
   kart, detay "Araç Karnesi") rapora eski arayüzün adresiyle tam sayfa gidiyordu. Artık SPA rotasıyla açılıyorlar.
   Çit: `kesis-f10.spec.ts`.

## Bilinçli farklar (gerekçeli)

| Fark                                                                                                                 | Gerekçe                                                                                                                                                                                   |
| -------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Grafikler (finans analiz trendi, doluluk serisi) tablo olarak gösteriliyor                                           | SPA'da ortak SVG grafik deseni yok; yeni kütüphane eklenmedi (#296). Veri aynı uçtan; sayılar aynı.                                                                                         |
| Filo analiz satırında araç başı vade uyarı sayısı sütunu yok                                                          | Sayı uç özetinde (`vadeUyariSayilari`) dönüyor ama ortak tablo sütunu yalnız satırdan okur. Vade uyarıları vade panosunda ve araç karnesinin vade bölümünde görünür.                       |
| Karşılaştırmalı analiz şube kapsamlı kullanıcıya 403                                                                  | Firma geneli sayım; Blazor operatöre tüm firmayı gösteriyordu (kapsam açığı, F10.1'de kapatıldı). Ekran açık mesaj gösterir.                                                               |
| Vardiya yazma bölümü yalnız OperationsWrite'ta                                                                        | Blazor formu dört role de çiziyordu ama POST uçları OperationsWrite istiyordu; Muhasebe formu doldurup 403 alıyordu. SPA'da Muhasebe raporu ve listeyi salt okunur görür.                   |
| Vardiya saatleri `SS:dd` metin kutusu (Blazor `type="time"`)                                                           | Ortak form setinde saat kontrolü yok; biçim istemcide ve sunucuda doğrulanır (`errors[baslangicSaat|bitisSaat]`), gece vardiyası ipucuyla anlatılır.                                         |
| Vardiya çakışma denetimi satır kilidi dışında                                                                          | Blazor ile aynı servis yolu (eşzamanlı iki eklemede teorik yarış mevcut davranış); bu faz davranışı değiştirmedi. Tam değiştirmede sürüm kilit altında karşılaştırılır.                     |
| Eski yer imlerindeki farklı adlı sorgu parametreleri (`from`/`to`, `personelFiltre`, `subeFiltre`, `engec`, `hata`, `sekme`, `mod`, `pivot`) SPA süzgecine dönüşmez | Yönlendirme sorguyu AYNEN taşır (tek kural, `IlkKesis`); SPA bilinmeyen parametreyi yok sayar ve varsayılan dönemle açılır. Uygulama içi bağlantıların hiçbiri bu parametreleri taşımıyor. |
| Menüde "operasyon" raporları (araç durum takip, km detay, periyodik servis, sigorta-muayene, karşılaştırmalı analiz, personel çalışma) ViewReports ile görünür | Menü kaydı Blazor menüsüyle aynı (grup kapısı); operatör bu raporlara rota ile girebilir (rota ve uç OperationsWrite ∨ ViewReports).                                                        |

## e2e

**Sahte API (CI'da koşar):**

- `reports.spec.ts` (F10.2): ortak akış, 403, axe iki tema, taşma.
- `shift-editor.spec.ts` (F10.3a) — üç zorunlu senaryo:
  - doğrulama hatasında form korunur (hatalı saat istek göndermez; sunucu `errors[baslangicSaat]`);
  - oturum düşünce yerinde giriş, sonra AYNI anahtar ve gövdeyle tekrar;
  - `cakisma` formu silmez: güncel kayıt kirli forma birleşir, sonraki PUT yeni sürümle gider.
- `shift-editor.spec.ts` ayrıca: sil onaylı ve rapor tazelenir, yetkisizde bölüm yok, 320/390/768 taşma 0.
- `kesis-f10.spec.ts` (F10.3b):
  - 26 eski adres SPA'ya gider (sorgu ve fragment korunur, doğru başlık);
  - rapor ekranlarında Blazor rapor sayfasına düşen bağlantı yok;
  - araç ekranlarındaki "Araç Karnesi" SPA içinde açılır.

**Backend (gerçek PostgreSQL, `racar_app`):**

- `UiShiftTests`: bağımsız oracle, sürüm, alan eşlemesi, çakışma, başka şube 403, başka kiracı 404, Muhasebe salt okunur.
- `UiReportTests` (F10.1): rapor tutarları elle kurulmuş defter senaryosundan.
- `IlkKesisTests`: F10 haritası = envanter = sayfaların gerçek `@page`'i; export / POST / API yönlenmez; pilot 302,
  pilot olmayan 200.

Gerçek backend e2e bu fazda koşulmadı: raporlar yalnız okur (F10.1 uç testleri gerçek DB'de) ve vardiya yazma uçları
gerçek boru hattında `UiShiftTests` ile kilitli.
