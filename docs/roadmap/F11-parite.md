# F11.3 — Parite ve e2e doğrulaması (Tanımlar + Sistem + Web Sitesi fazı)

> Kalıp adım 3 ("Doğrulama: parite + e2e"). Kaynak: `F11.md` envanterindeki 47 Blazor sayfası (`Components/Pages/…`)
> ile SPA bileşenleri (`src/RentACar.Frontend/src/app/features/definitions` — #297, #306, #308 — ve `features/system`
> — #304), 2026-09-24 `main`. Yöntem: dört ekran PR'ının parite tabloları ve güvenlik kararları bu belgeye toplandı;
> her sayfanın `@page` rotası, sorgu parametreleri (`SupplyParameterFromQuery`), GET süzgeç formları ve başka ekranlardan
> gelen bağlantıları SPA rota tablosu, sayfa bileşeni ve bağlantılarıyla karşılaştırıldı. Eksik bulunan eklendi,
> bilinçli farklar gerekçeli. Uçlar #282 (F11.1a) ve #288 (F11.1b), ekler #306/#308; bu PR'da backend iş koduna
> dokunulmadı (yalnız yönlendirme haritası ve menü kaydı).

## Envanter kontrolü

47 sayfanın 47'si SPA'da (tembel rota, aynı yol): 21 genel tanım (`DEFINITION_PATHS`), 9 özel tanım/KVKK ekranı
(`definitions.routes.ts`: araç grupları, rezervasyon kaynakları, personel, içe aktar, şubeler, doluluk kuralları,
dokümanlar, firma belgeleri, takvim aboneliği) ve 17 sistem/web ekranı (`system.routes.ts`). **Taşınmamış F11 ekranı
yok.** `/tarife-aktar` (Import klasöründe) F11 envanterinde değil; Blazor'da kalır ve haritaya girmez.

## Özet

| Grup (Blazor → SPA, aynı yol)                                                                                                                                          | Alan / sütun                         | Süzgeç                                       | Eylem                                                                                                                                                            | İzin (uçla birebir)                     | Sonuç                                    |
| ---------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------ | -------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------- | ---------------------------------------- |
| 12 genel tanım (#297): markalar, iptal sebepleri, ülkeler, müşteri grupları, departmanlar, aksesuarlar, bankalar, dövizler, özel kodlar, gider türleri, hesaplar, drop | tam (hesaplar ve drop panel formu)   | yok (drop hariç Blazor'da da yok)            | ekle, düzenle (`surum`, 409 birleştirme), sil (kullanımdaysa 400)                                                                                                | OperationsWrite                         | tam (bkz. bilinçli farklar)              |
| 9 genel tanım (#306): ödeme tipleri, yakıt/vites türleri, renkler, hesap kodları, sigorta şirketleri, KDV oranları, ceza türleri, belge şablonları                     | tam                                  | —                                            | ekle, düzenle (`surum`), sil                                                                                                                                     | OW; belge şablonları ManageUsers        | tam (bkz. bilinçli farklar)              |
| araç grupları, rezervasyon kaynakları (#306)                                                                                                                           | 38 alan / oran-kural-tutar alanları  | —                                            | CRUD + eşleşmeyen grup ataması / "Aşağıya Yansıt" (onaylı)                                                                                                       | OW                                      | tam (bkz. bilinçli farklar)              |
| şubeler, doluluk kuralları (#297)                                                                                                                                      | tam (gizli bağlı hesaplar korunur)   | —                                            | CRUD, ücretsiz hizmetler, birleştirme önizleme → onay; doluluk toplu 10 kademe                                                                                   | şubeler ManageUsers, doluluk OW         | tam                                      |
| dokümanlar, firma belgeleri, takvim aboneliği (#297)                                                                                                                   | tam                                  | —                                            | yükle/sil (OW, sayfada gizli + uçta), indir; takvim kopyala + onaylı yenile                                                                                      | oturum                                  | tam                                      |
| personel, veri içe aktar (#308)                                                                                                                                        | 33 alan; TC/maaş yazma-yalnız        | personel süzgeci yok (bkz. bilinçli farklar) | CRUD, TC/maaş temizle; araç/müşteri içe aktarımı + hata özeti                                                                                                    | ManageUsers                             | tam (bkz. KVKK)                          |
| kullanıcılar, ekran yetkileri, ayarlar, mesaj şablonları, denetim (#304)                                                                                               | tam; sır alanları yalnız yazılır     | denetim süzgeç + sayfalama                   | kullanıcı CRUD/aktif/parola sıfırla/istisna; yetki override/kopya/grup; ayarlar PUT (`surum`), logo, site aç, alan adı + TXT doğrula, test gönderimleri (onaylı) | ManageUsers                             | tam (bkz. güvenlik)                      |
| lokasyonlar (#304)                                                                                                                                                     | tam; haftalık saatler PUT'ta korunur | —                                            | CRUD (`surum`)                                                                                                                                                   | OW (+ şube kapsamı sunucuda)            | tam (bkz. bilinçli farklar)              |
| web sitesi: ilanlar + sihirbaz (fiyat, özellikler), site içeriği, blog + önizleme (#304)                                                                               | tam                                  | —                                            | ilan oluştur/durum/sil/foto, fiyat ve özellik PUT (`surum`); sayfa + SSS; yazı + kapak                                                                           | OW + Web Sitesi modülü (blog yalnız OW) | tam (bkz. bilinçli farklar)              |
| gelen talepler (#304)                                                                                                                                                  | tam (TC yok)                         | durum, arama, sayfa                          | durum ilerlet, üstlen/bırak, not, onaylı ret, müsait araçla dönüştür                                                                                             | OW                                      | **eklendi:** `?durum=` süzgeci URL'den   |
| bildirimler, genel arama, kendi parolası (#304)                                                                                                                        | tam                                  | arama metni                                  | oku / hepsini oku; arama; parola değiştir (eski parola + hız sınırı)                                                                                             | oturum                                  | **eklendi:** `?q=` ve sonuç bağlantıları |

## Bulunan eksikler (bu PR'da eklendi)

1. **Panel "Site talebi" kutusu (gezinme paritesi).** `panel-sayfasi.ts` bağlantıyı `href: '/gelen-talepler?durum=0'` ile
   Blazor sayfasına TAM SAYFA veriyordu (F4.5 yer tutucusu). Artık `routerLink` + sorgu: `/app/gelen-talepler?durum=Yeni`
   (`VadeKutusu.sorgu`).
2. **Gelen talepler `?durum=` süzgeci.** Blazor sayfası sorgudaki durum kodunu (`0` = Yeni … `5` = Kayıp, enum değeri)
   okuyordu; SPA hiç okumuyordu, eski yer imi ve panel bağlantısı süzgeçsiz liste açıyordu. `statusFromQuery` durum adını
   ya da eski sayı kodunu kabul eder, bilinmeyen değer varsayılan liste. Kişisel veri taşıyabilen `ara` URL'den alınmaz
   (#304 kararı korunur).
3. **Genel arama `?q=`.** Eski arayüzün kenar çubuğu/üst çubuk arama kutusu `GET /ara?q=…` gönderir; kesişten sonra pilot
   firmada bu SPA'ya yönlenir ama SPA sorguyu okumuyordu (boş ekran). Artık metin bir kez okunur, arama yapılır ve sorgu
   `replaceUrl` ile adresten silinir (aranan metin URL'de ve geçmişte kalmaz).
4. **Arama sonuç bağlantıları.** Sonuçlar Blazor adresine tam sayfa gidiyordu. `hitRoute` bilinen hedefleri SPA rotasına
   çevirir ve bağlantı `routerLink` olur: araç `/araclar/{id}` → `/app/araclar/{id}/detay` (Blazor'da detay sayfası),
   cari ve kira kimlikli, rezervasyon ve fatura listeleri. Karşılığı olmayan hedef düz adresle kalır; `safeHitUrl`
   (#304 L1) önce uygulanır.

Çit: `e2e/kesis-f11.spec.ts` + `panel.spec.ts` (site talebi bağlantısı) + Vitest `search-page.spec.ts` (`hitRoute`),
`booking-requests-page.spec.ts` (`statusFromQuery`).

## Güvenlik ve KVKK kararları (#288, #304, #308 — değişmedi, kesiş bunları taşır)

- **Kendi parolasını sıfırlama yasağı (#304 M1):** yönetici "parola sıfırla" kendi hesabına uygulanmaz
  (`UserService.ResetPasswordAsync` → 400 `errors[sifre]`, Blazor yolu da). Kendi parolası yalnız eski parola doğrulanarak
  ve giriş hız sınırıyla `/profil/sifre-degistir`'den değişir; kimlik gövdeden değil oturumdan alınır. Kullanıcı
  listesinde kendi satırında "Parola sıfırla" yerine "Kendi parolam" bağlantısı (SPA rotası) var.
- **Kullanıcı yönetimi kemerleri:** kendini pasifleştiremez, son aktif Admin pasifleştirilemez (kilit altında sayım), kendi
  istisnanı değiştiremezsin, Admin hesabına ve Admin rolüne yalnız Admin dokunur (M2). Admin olmayan ManageUsers sahibine
  bu seçenekler gösterilmez; asıl kapı sunucuda (403).
- **Gizli alanlar (sırlar):** e-Fatura şifresi, SMS/POS API anahtarı, SMTP şifresi hiçbir yanıtta yok, yalnız `*Tanimli`
  → "kayıtlı" yer tutucusu. `type=password` + `autocomplete=new-password`; boş = `null` = sunucu korur; kayıttan sonra
  formdan silinir; tarayıcı deposuna yazılmaz. Denetim ekranı `*Enc`, parola hash'i ve takvim belirtecini `***` gösterir.
  SMTP hedefi değişince şifre zorunlu (M3).
- **Personel TC ve maaş (#308):** TC hiçbir yanıtta yok; alan daima boş açılır. Boş/eksik = kayıtlı değer korunur, `""`
  ("Kayıtlı TC'yi sil") = cipher silinir, dolu = yeni değer; dolu değer silme bayrağına üstün gelir. Yalnız boşluktan
  oluşan TC silmez, 400 `errors[tcKimlik]` (M1). Maaş yalnız tekil kayıtta döner, silme `maasTemizle: true`. Tüm personel
  uçları ManageUsers; liste PII'sız.
- **İçe aktarım sınırları (#308):** ManageUsers, XSRF başlığı, `.xlsx/.xls/.csv`, dosya 5 MB (SPA ucu; ortak ayrıştırıcı
  en çok 8 MB okur), 20.000 satır (ayrıştırma sırasında), 100 sütun, başlık satırı 8.192 / veri satırı 65.536 karakter,
  xlsx: ZIP girdisi ≤ 2.000, açılmış boyut ≤ 16 MB, toplam dolu hücre ≤ 500.000 — xlsx sayımı ClosedXML yüklemesinden
  ÖNCE akışla. Bozuk dosya 400 `errors[dosya]` (500 değil). Yanıt kişisel veri taşımaz: hata özeti etiketsiz (plaka /
  müşteri adı atılır), en sık 20 mesaj + "Diğer hatalar". Müşteri PII'sı şifreli yazılır; tekrar plaka/TC atlanır.
- **Alan adı ve dosyalar (#288):** özel alan adı biçim doğrulaması, alt alan adı sahiplenmesi reddi, TXT doğrulamasından
  önce yayına girmez (M6). Logo, ilan fotoğrafı, blog kapağı içerikten tür tespiti; satır içi + `nosniff`.
- **XSS ve arama (#304):** blog/sayfa/denetim/belge şablonu metinleri yalnız metin bağlamasıyla basılır (`innerHTML` yok).
  Arama sonucu yalnız kök-göreli ve bilinen kökle başlayan yol olabilir (`safeHitUrl`, L1).
- **İyimser eşzamanlılık:** tüm tam değiştirme PUT'larında zorunlu `surum`; 409 `cakisma` formu silmez.

## Bilinçli farklar (gerekçeli)

| Fark                                                                                                                                            | Gerekçe                                                                                                                                                                                                                           |
| ----------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Silinemeyen tanımlar (kullanımdaki marka, banka, döviz, özel kod, gider türü)                                                                   | #282: Blazor serbestçe siliyordu; artık 400 + pasife alma önerisi (FK ihlali 500 yerine).                                                                                                                                         |
| Hesap kodları OperationsWrite ister ve düzenlenebilir                                                                                           | Blazor sayfası her role açıktı, düzenleme yoktu; uç grubu tek izinli (#306).                                                                                                                                                      |
| Araç gruplarında "Araç sayısı" sütunu yok; eşleşmeyen atama tek formda                                                                          | Tanım CRUD'unda salt okunur sütun yok (değer yanıtta var) (#306).                                                                                                                                                                 |
| KDV oranı listede "0,20" kesir                                                                                                                  | Giriş Blazor gibi 0–1 (#306).                                                                                                                                                                                                     |
| Rezervasyon kaynağında "Uygulanan kurallar" özet sütunu yok                                                                                     | Kural bayrakları panelde "Kural:" önekiyle (#306).                                                                                                                                                                                |
| Drop matrisi ve personel listesinde süzgeç yok (Blazor `fDonus/fCikis/fSube/fAktif`, `ara/subeF/gorev/durum`)                                   | Tanım CRUD bileşeninde süzgeç yok; uçlar destekliyor. Liste tamamı okunur. Yer imindeki bu parametreler yok sayılır.                                                                                                              |
| Personelde "Kayıtlı TC'yi sil" var                                                                                                              | Blazor'da TC silinemiyordu; yeni davranış (#308, KVKK).                                                                                                                                                                           |
| İçe aktarımda hata özeti de gösterilir                                                                                                          | Blazor yalnız sayaç gösteriyordu; özet etiketsiz (#308). Blazor `?tur/eklenen/atlanan/hatali` sonuç sorgusu SPA'da yok (sonuç bellekte).                                                                                          |
| Renk kodları `#rrggbb` metin; site adresi düz metin                                                                                             | `type=color` "boş = varsayılan"ı taşımıyor; SPA'da mutlak URL yasağı (#304).                                                                                                                                                      |
| Blog kapak/önizleme ayrı bölümde; site içeriği "yayında" form alanı                                                                             | Tanım CRUD satırında ek düğme yok; PUT aynı değeri yazar (#304).                                                                                                                                                                  |
| İlan fotoğraf sırası bu ekranda yok                                                                                                             | Araç kartında var (#304).                                                                                                                                                                                                         |
| Ofis haftalık çalışma saatleri formda düzenlenmiyor                                                                                             | Tam PUT'ta aynen korunur (#304).                                                                                                                                                                                                  |
| Sır temizleme yok                                                                                                                               | `SettingsRequest`'te boş = korunur; sırrı silmek için ayrı açık işaret alanı gerekir (#304 eksik uç).                                                                                                                             |
| Liste ekranlarında `?duzenle=`, şube birleştirme `?kaynak=&hedef=`, takvim `?yeni=`, site içeriği `?sayfa=`, ilan `?bos=`, sihirbaz `?mod=` yok | Blazor bu durumları sorguyla taşıyordu; SPA'da aynı işlem sayfa içinde (panel, önizleme, sekme). Uygulama içinde bu parametrelerle F11 sayfasına giden bağlantı yok (tarama: yalnız Blazor Panel'in `?durum=0`'ı vardı, o F4'te). |
| Denetim süzgeci URL'den okunmaz (`entity/kullanici/islem/page`)                                                                                 | Süzgeç ekranda; bu parametrelerle denetime giden bağlantı yok. Yer imi varsayılan listeyi açar.                                                                                                                                   |
| Gelen talepler `ara` ve `donustur` sorgusu okunmaz                                                                                              | Arama kişisel veri olabilir (URL'ye yazılmaz); dönüştürme satırın düğmesinden açılır.                                                                                                                                             |
| Özel alan adı TXT doğrulama düğmesi SPA'da var                                                                                                  | Blazor'da yoktu (M6).                                                                                                                                                                                                             |

## e2e

**Sahte API (CI'da koşar):** `definitions.spec.ts`, `definitions-remaining.spec.ts`, `definitions-personnel.spec.ts`,
`system-admin.spec.ts`, `system-settings.spec.ts`, `system-web.spec.ts` (#297/#304/#306/#308: üç zorunlu senaryo, KVKK
ve sır kanıtları, axe iki tema, 320/390/768/1440 taşma) + `kesis-f11.spec.ts` (bu PR): 47 eski adres → SPA (sorgu ve
fragment korunur, doğru sekme başlığı); 47 ekranın sayfa içeriğindeki hiçbir bağlantı Blazor F11 sayfasına düşmez;
`/ara?q=` bir kez aranır, adresten silinir, sonuç bağlantısı router'la gider (tam sayfa yüklemesi yok); `/gelen-talepler?durum=0`
"Yeni" süzgeciyle açılır ve uca `durum=Yeni` gider. `panel.spec.ts`: site talebi kutusu `/app/gelen-talepler?durum=Yeni`.

Koşum (yerel, port 4621): `kesis-f11` + `kesis-f10` + `kesis-f7` + `kesis-f6` + `kesis-f5` + `kesis` + `definitions*` +
`system-*` + `panel` — 186/186 ✔.

**Gerçek backend:** bu PR yeni ekran ya da uç getirmiyor; tanım/sistem/web uçlarının gerçek PostgreSQL üzerindeki izin,
kapsam, KVKK ve sınır testleri `UiTanimTests`, `UiSystemAdminTests`, `UiSystemDefinitionsTests`, `UiWebsiteApiTests`,
`ImportLimitsTests` (#282/#288/#306/#308); yönlendirmenin gerçek boru hattı `IlkKesisHostTests` (bu PR: pilot firmada
302 — admin ve operatör —, pilot olmayan firmada 200, F11 POST uçları yönlenmez, dosya GET'leri yönlenmez, döngü yok).
