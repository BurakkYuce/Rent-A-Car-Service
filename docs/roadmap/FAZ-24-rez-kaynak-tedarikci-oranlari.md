# FAZ-24 — Rez. Kaynağı × Tedarikçi Oranları

| | |
|---|---|
| **Desen** | D2 (yapısal alan+buton iskeleti) — oranların hesaplamaya nasıl yansıdığı **PARA — Opus'a devredilir** |
| **Efor** | 0,75 gün |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `xml_rez_kaynak_tedarikci.aspx` |
| **Risk** | düşük (bu fazda) — alanlar ve "Aşağıya Yansıt" butonu yalnız İSKELET olarak kurulur;
  gerçek yansıtma hedefi (hangi hesaplamayı değiştirdiği) devre dışı bayrakla korunur, para riski
  Opus onayına kadar AÇILMAZ |

## Amaç
Rezervasyon Kaynağı tanımına tedarikçi adı ve kira/hizmet/drop oran alanlarını ekleyip, seçili
kaynağın oranlarını tüm aktif kayıtlara toplu kopyalayan bir "Aşağıya Yansıt" buton-iskeleti
kurularak kullanıcı bu alanları görüp doldurabilir hale gelir — gerçek komisyon/karlılık
hesaplamasına yansıtma AYRI bir Opus incelemesi bekler.

## Neden (kanıt)
`src/RentACar.Domain/Entities/ReservationSource.cs` şu an yalnız `Kod, Ad, Aktif` taşıyor —
`Tedarikci`, `KiraOrani`, `HizmetOrani`, `DropOrani` **hiçbiri yok** (dosya tam içeriği doğrulandı).

## Yapılacaklar
1. `src/RentACar.Domain/Entities/ReservationSource.cs` — ekle: `Tedarikci` (string?), `KiraOrani`
   (decimal?, %), `HizmetOrani` (decimal?, %), `DropOrani` (decimal?, %).
2. `src/RentACar.Infrastructure/Persistence/Configurations/MasterConfigs.cs` — ReservationSource
   config'e 4 yeni kolon (`numeric(5,2)` oran alanları için) ekle.
3. Migration: `dotnet ef migrations add AddReservationSourceOranlar --project
   src/RentACar.Infrastructure --startup-project src/RentACar.Infrastructure`.
4. `src/RentACar.Application/ReservationSources/ReservationSourceInput.cs` + `ReservationSourceService.cs`
   — 4 alanı input/map'e ekle.
5. `src/RentACar.Web/Components/Pages/ReservationSources/ReservationSourceList.razor` — create/edit
   formuna 4 alan ekle.
6. Aynı sayfaya "Aşağıya Yansıt" butonu ekle: seçili kaynağın oranlarını tüm **aktif** kayıtlara
   kopyalayan bir POST ucu (`ReservationSourceEndpoints.cs`'e yeni action). **Bu fazda buton
   yalnız oranları diğer `ReservationSource` satırlarına kopyalar** (kendi tablosu içinde toplu
   update) — hangi hesaplamaya (komisyon/karlılık raporu) yansıyacağı, işlemin idempotent olup
   olmadığı ve mevcut kayıtlı rezervasyonların oranını değiştirip değiştirmeyeceği **Opus
   incelemesine bırakılır**; bu faz bittiğinde buton çalışır ama etkisi SADECE
   `ReservationSource` tablosuyla sınırlıdır, başka bir tabloya/hesaba yazmaz.

## Dokunulacak dosyalar
- `src/RentACar.Domain/Entities/ReservationSource.cs` — 4 alan
- `src/RentACar.Infrastructure/Persistence/Configurations/MasterConfigs.cs`
- (yeni) migration dosyası
- `src/RentACar.Web/Components/Pages/ReservationSources/ReservationSourceList.razor`
- `src/RentACar.Web/ReservationSources/ReservationSourceEndpoints.cs` — "Aşağıya Yansıt" ucu
- `src/RentACar.Application/ReservationSources/ReservationSourceInput.cs`,
  `ReservationSourceService.cs`

## Migration
Var — mevcut tenant-owned `ReservationSources` tablosuna 4 nullable kolon. RLS zaten aktif, yeni
tablo yok → RLS bloğu gerekmez.

## Test
- `ReservationSourceTests`: 4 alan round-trip.
- "Aşağıya Yansıt" testi (bağımsız oracle): 3 kaynak elle oluşturulur (1 kaynak oranlı, 2 boş);
  buton tetiklenir; 2 boş kaynağın oranlarının seçili kaynağınkiyle EŞİT olduğu, PASİF bir 4.
  kaynağın DEĞİŞMEDİĞİ doğrulanır (aktif-filtre testi). Bu testin kapsamı SADECE
  `ReservationSource` tablosudur — başka bir tabloya sızma yoksa test bunu da assert eder (ör.
  komisyon/karlılık raporu tablosuna yazılmadı).

## Exit
- [ ] 4 yeni alan formda + kaydediliyor
- [ ] "Aşağıya Yansıt" butonu yalnız aktif kayıtları, yalnız `ReservationSource` tablosunda günceller
- [ ] Hiçbir ledger/komisyon hesaplaması bu fazda DEĞİŞMEDİ (regresyon testiyle doğrulanır)
- [ ] Tam suite yeşil

## Notlar
"Aşağıya Yansıt"ın gerçek anlamı (komisyon hesaplamasına ne zaman/nasıl gireceği) kullanıcıya AYRI
bir soru olarak sorulmalı — bu faz bittiğinde buton var ama para mantığına kör; Opus onayı gelene
kadar yalnızca kendi tablosu içinde oran kopyalar.
