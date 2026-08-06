# FAZ-43 — Teslim/Dönüş-Bağlı Şikayet Değerlendirme (Yeni Dikey)

| | |
|---|---|
| **Desen** | D7 — yeni dikey |
| **Efor** | 3 gün |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `sikayet_listesi.aspx` |
| **Risk** | düşük — mevcut `Sikayet` tablosuna additive kolon, yeni tablo yok, para/defter dokunmuyor |

## Amaç
Kullanıcı bir şikayeti araç teslim/dönüş sürecine (sözleşme, teslim alan/eden personel, puan, kanal,
şikayet yeri, çıkış ofisi) bağlayabilir ve `/sikayetler`'de Müşteri/Ofis/Şikayet Yeri/Şikayet Kanalı
ile filtreleyebilir hale gelir — bugünkü sözleşmeden bağımsız genel şikayet-bileti modelinden ayrı,
canlı paritesine yakın bir derinlik.

## Neden (kanıt)
`src/RentACar.Domain/Entities/Sikayet.cs` şu an `Id, TenantId, CariId, Konu, Detay, Durum, Tarih,
Cozum` taşıyor — sözleşme/teslim sürecinden bağımsız genel şikayet-bileti. Canlı ekran araç
teslim/dönüş sürecine bağlı şikayet-değerlendirme; plandaki `RentalId`, `TeslimAlanPersonelId`,
`TeslimEdenPersonelId`, `Puan`, `SikayetKanali`, `SikayetYeri`, `CikisOfisi` **hiçbiri yok** (grep
doğrulandı). `RentalContract` zaten `TeslimAlanPersonelId` (dönüş) taşıyor — Şikayet'te aynı deseni
tekrar kullanmak (yeni FK alanı, aynı isim) tutarlılık sağlar. `SikayetList.razor` (satır 15) grid'i
yalnız Tarih/Konu/Durum/Çözüm gösteriyor, filtre yok.

## Yapılacaklar
1. `src/RentACar.Domain/Entities/Sikayet.cs` — additive alanlar ekle: `RentalId` (Guid?,
   `RentalContract` FK — `Plaka` ve `SozlesmeNo` bu FK'den `VehicleId`→`Vehicle.Plaka` join'iyle
   türetilir, snapshot alan eklenmez), `TeslimAlanPersonelId` (Guid?, `Personel` FK),
   `TeslimEdenPersonelId` (Guid?, `Personel` FK), `Puan` (int?), `SikayetKanali` (string?, "Telefon"/
   "Web"/"Yüz Yüze" — seç-veya-yaz `<input list>`+`<datalist>` ComboBox konvansiyonu, CLAUDE.md
   memory "combobox-sec-veya-yaz"), `SikayetYeri` (enum `Kira`/`Rezervasyon`, yeni
   `src/RentACar.Domain/Enums/SikayetYeri.cs`), `CikisOfisi` (string?, `RentalContract.CikisOfisi`'nden
   snapshot).
2. `src/RentACar.Infrastructure/Persistence/Configurations/CustomerConfigs.cs` — mevcut
   `SikayetConfig` sınıfına (satır 153) 7 yeni kolon ekle.
3. Migration: `dotnet ef migrations add AddSikayetTeslimBagli --project src/RentACar.Infrastructure
   --startup-project src/RentACar.Infrastructure`.
4. `src/RentACar.Application/Crm/CrmModels.cs` — `SikayetInput`'a 7 alanı ekle.
5. `src/RentACar.Application/Crm/ICrmRepositories.cs` — mevcut `ISikayetRepository`'ye
   `SearchAsync(SikayetFilter filtre, CancellationToken ct = default)` overload'u ekle;
   `src/RentACar.Infrastructure/Persistence/Repositories/CrmRepositories.cs`'teki mevcut
   `SikayetRepository` sınıfına (satır ~55) implementasyonu ekle (Müşteri/Ofis/Şikayet Yeri/Kanalı
   `Where` filtresi + `RentalContract` join'i Plaka için).
6. `src/RentACar.Application/Crm/SikayetService.cs` — mevcut `Apply` metoduna 7 alanı map et; yeni
   `SikayetFilter` (Müşteri, Ofis, Şikayet Yeri, Şikayet Kanalı) sınıfını `CrmModels.cs`'e ekle +
   `SearchAsync(filtre)` servis metodunu ekle.
7. `src/RentACar.Web/Components/Pages/Crm/SikayetList.razor` — forma sözleşme seçici (ComboBox,
   sözleşme no ile arama) + Teslim Alan/Eden Personel select + Puan + Şikayet Kanalı ComboBox +
   Şikayet Yeri select ekle. Filtre barı: Müşteri, Ofis, Şikayet Yeri, Şikayet Kanalı. Liste
   kolonlarına Kayıt No, Plaka (join), Türü, Cep Tel (Customer'dan), Belge No, Puan, Teslim Alan/
   Eden, Özet, Çıkış Ofisi ekle.

## Dokunulacak dosyalar
- `src/RentACar.Domain/Entities/Sikayet.cs` — 7 alan
- (yeni) `src/RentACar.Domain/Enums/SikayetYeri.cs`
- `src/RentACar.Infrastructure/Persistence/Configurations/CustomerConfigs.cs` — `SikayetConfig`
  (satır 153)
- (yeni) migration `AddSikayetTeslimBagli`
- `src/RentACar.Application/Crm/{CrmModels.cs,SikayetService.cs,ICrmRepositories.cs}` (hepsi mevcut
  dosya, yeni dosya yok)
- `src/RentACar.Infrastructure/Persistence/Repositories/CrmRepositories.cs` — `SikayetRepository`
  (mevcut sınıf, yeni metod)
- `src/RentACar.Web/Components/Pages/Crm/SikayetList.razor`

## Migration
Var — `Sikayet`'e (mevcut tenant-owned tablo, RLS zaten aktif) 7 nullable kolon. **RLS bloğu
gerekmez** — yeni tablo yok.

## Test
- `SikayetTests` (yeni veya mevcut `CustomerCrmTests`'e ekleme): 7 yeni alan round-trip (bağımsız
  oracle — elle kurulan Şikayet nesnesi).
- Filtre testi: 3 şikayet (2 farklı ofis, 2 farklı kanal) elle oluşturulur; `SikayetKanali="Telefon"`
  filtresiyle arama yalnız o kanaldaki kaydı döndürüyor mu (beklenen sayı testte sabit).
- Plaka join testi: 1 `RentalContract` (Vehicle Plaka="34ABC12") + 1 Şikayet `RentalId` ile bağlanır;
  liste sorgusunda `Plaka == "34ABC12"` beklenir (sabit, join sorgusundan değil elle girilen
  plakadan).

## Exit
- [ ] `/sikayetler` formunda sözleşme/personel/puan/kanal/yer/ofis alanları çalışıyor, kaydediliyor
- [ ] Filtre barı (Müşteri/Ofis/Şikayet Yeri/Şikayet Kanalı) çalışıyor
- [ ] Liste kolonunda Plaka join'i doğru çalışıyor
- [ ] Tam suite yeşil

## Notlar
`TeslimAlanPersonelId`/`TeslimEdenPersonelId` isim çakışması `RentalContract.TeslimAlanPersonelId`
ile KASITLI — aynı kavram, aynı isim; formda hangi kayıttan geldiği (Şikayet'in kendi alanı, kira
sözleşmesinden kopya değil) açık yazılmalı ki kullanıcı ikisini karıştırmasın (Şikayet formunda
sözleşme seçilince bu iki alan sözleşmeden ÖNERİ olarak gelebilir ama kullanıcı değiştirebilir).
