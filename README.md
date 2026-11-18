# TLOUP1 v1.0

The Last of Us Part I için Windows x64 uygulaması. Orijinal oyun ikonu ve
oyun ikonundaki kahverengi/amber/krem tonlarında bir tema kullanır;
resmî bir Naughty Dog / Sony ürünü değildir.

**Sürüm:** v1.0 (1.0.0) — [GitHub Releases](https://github.com/sametyamanxx/TLOU1/releases) sayfasından indirin.

## Portable kullanım

`portable\TLOUP1\TLOUP1.exe` dosyasını doğrudan çalıştır.
Portable klasöründe yalnızca bu EXE bulunur; ZIP, setup veya harici DLL yoktur.

Kurulum veya ayrıca .NET yüklemek gerekmez. Gerekli .NET 8 masaüstü bileşenleri
ve lisanslar EXE içine gömülüdür. .NET, yerel çalışma bileşenlerini ilk açılışta
`%TEMP%\.net\TLOUP1` altında açabilir; bu sistem çalışma önbelleği kapanıştan
sonra kalabilir. Portable klasöründe ek dosya üretilmez.
Kaldırıcı, kayıt defteri kurulumu, kısayol,
reklam, ek yazılım, servis, görev, otomatik başlangıç veya güncelleyici oluşturulmaz.
Günlük bellekte tutulur; AppData'ya günlük yazılmaz.

Sağ üstteki **—** simgesi paneli gizler; Insert geri getirir.
Belirgin kırmızı **×** uygulamayı normal biçimde kapatır.
Altta yeniden bağlan, bilgi veya kapat düğmesi yoktur; oyun otomatik algılanır.
Oyun kapalıyken açılışta temalı bir “Önce oyunu aç” önerisi gösterilir.
Bu zorunlu bir yeniden başlatma şartı değildir; oyun sonradan da açılabilir.

## Tuşlar

| Tuş | İşlev |
|---|---|
| Insert | Pencereyi gizle/göster (programı kapatmaz) |
| F1 | Sınırsız mühimmat; çakı tüketimi, yakın dövüş dayanıklılığı ve sarf/üretim kontrolleri dahil |
| F2 | Sınırsız sağlık (hasar kontrollerini aç/kapat) |

Tüm silahları verme özelliği, ilgili analiz ve çağrı kodlarıyla birlikte kaldırılmıştır.
F3 artık kaydedilmez veya kullanılmaz.

Kurulu veya çalışan oyun her bağlantıda analiz edilir. Sürüme özel adresler sabit
kabul edilmez. Doğrulanamayan özellik açık gösterilmez. F1'in ek alt kontrolleri
bulunamazsa ayrıntı satırı bunu belirtir; her oyun sürümü için bütün etkiler garanti değildir.
Çakı için önce envanterinde en az bir çakı bulunmalıdır; uygulama çakı veya silah vermez.
Çakı tüketim işlevi, iki eşya etiketi, PE işlev sınırları ve iki ayrı envanter çağrısıyla
doğrulanır. Sadece çakı adedi/dayanıklılığını azaltan bu iki çağrı geçici olarak atlanır.
F1 kapatılınca ve çıkışta çağrıların orijinal kodu geri yüklenir.

## Geri alma ve sınırlar

- Oyun yürütülebilir dosyasına veya diğer oyun dosyalarına yazılmaz.
- Kayıt dosyalarına doğrudan erişilmez; silinmez, yedekle değiştirilmez.
- F1/F2 kapatılınca yamalanan kod alanları orijinal baytlarına döndürülür.
- Normal çıkışta tüm kod noktaları yeniden doğrulanıp geri yüklenir ve ayrılan sayfa bırakılır.
- Aynı EXE'nin ayrı kurtarma işlemi, UI beklenmedik biçimde kapanırsa tanıdığı
  yamaları geri yüklemeyi dener. Yama/suspend işlemlerini de bu yardımcı yürütür;
  UI'nin kapanması oyunun askı sayacını yarıda bırakmaz.
- Kurtarma bilgisi yalnızca bellekte ve yerel, aynı kullanıcıya ait bir named pipe'tadır.
  Diskte günlük veya kurtarma dosyası oluşturulmaz. Yardımcı olay bekler; kare
  başına tarama/yazma yapmaz. Gerçek oyun FPS ölçümü yapılmadı.
- Bağlıyken periyodik işlem taraması ve UI zamanlayıcısı durur; oyun kapanışı bir
  işletim sistemi olayıyla izlenir. Oyun yokken arama iki saniyede bir yapılır.
- Analiz yalnızca bağlanırken, tek düşük öncelikli iş parçacığında yapılır.
  Dosyanın ikinci büyük kopyası tutulmaz; geçici analiz belleği işlem bitince toplanır.
- Başka bir araç kodu değiştirmişse uygulama onun değişikliğini ezmez; kurtarma
  başarısız olabilir. Her iki uygulama işlemi de zorla öldürülürse koruma çalışamaz.
  Bu durumlarda oyunu yeniden başlatmak gerekir.
- Oyun kapanınca Windows işlem belleğini zaten serbest bırakır.
- Oynanış ilerlemesini veya oyunun kendi otomatik kayıtlarını geri sarmaz.
  “Orijinale dönme” kod yamalarının kaldırılmasıdır; geçmiş kayıt dosyasına dönmek değildir.

Silmek için önce sağ üstteki **×** ile çık, sonra çıkarttığın TLOUP1 klasörünü
sil. Önceden setup sürümünü kurduysan önce Windows Ayarlar'dan onu kaldır.
Windows'un kendi tuttuğu geçmiş, Prefetch, Defender ve ikon önbelleği kayıtlarında
**sıfır iz** garanti edilemez; bunlar temizlenmez.

## Doğrulama

Statik analiz Epic 1.1.4.0 üzerinde yapılmıştır. Bir işlevin bulunması, oyun içindeki
etkisinin doğrulandığı anlamına gelmez. Canlı oyun içi silah/sağlık/dayanıklılık
etkileri ve FPS ölçümü ayrıca test edilmelidir.

Testler sentetik görüntü analizi, bellek yazımı/geri alma, yabancı değişikliği
koruma ve ayrı yardımcı işlemin beklenmeyen ana işlem çıkışında kurtarmasını kapsar.

### Analiz optimizasyonu ölçümü

Bu bilgisayarda aynı Epic oyun dosyası üzerinde üç geçişlik geliştirici ölçümü:

| Ölçüm | Önce | Sonra |
|---|---:|---:|
| Ortanca analiz süresi | 661 ms | 261 ms |
| Bir analizde yönetilen bellek tahsisi | 621 MB | 133 MB |
| Doğrulanan mühimmat/sağlık noktaları | 14 / 12 | 14 / 12 |

Bellek tahsisi kalıcı RAM kullanımı veya oyun FPS'si değildir. Oyun içi FPS artışı
ölçülmedi; panel/başka bir pencere öndeyken oyunun arka plan FPS sınırı ayrıca etkili olabilir.

## Derleme

.NET 8 SDK ve Windows x64 gerekir; kurulum derleyicisi gerekmez.

```powershell
dotnet test TLOUTrainer.sln -c Release
powershell -ExecutionPolicy Bypass -File tools/Build-Portable.ps1
powershell -ExecutionPolicy Bypass -File tools/Test-Portable.ps1
```

### GitHub Actions ile Otomatik Release

1. Bu depoyu GitHub'a push edin
2. Sürüm etiketi atın: `git tag v1.0 && git push origin v1.0`
3. GitHub Actions otomatik:
   - Derler, testleri çalıştırır
   - Tek EXE portable üretir (`portable/TLOUP1/TLOUP1.exe`)
   - Release sayfasında `TLOUP1 v1.0` adıyla yayımlar
4. Kullanıcılar Release sayfasından `TLOUP1.exe` indirip çalıştırır

> **Not:** Workflow dosyasında `RELEASE_NOTES.md` dosyası release açıklaması olarak kullanılır.

Tek dosya yayını kullanılır: .NET çalışma dosyaları EXE içine paketlenir;
nihai portable klasöründe tek dosya bırakılır. Kaynak kod,
test araçları ve setup dosyaları portable pakete eklenmez. Büyük gömülü bileşenler
sıkıştırılmaz; açılıştaki dekompresyon CPU/RAM yükü önlenir.

Lisanslar: `TLOUP1.exe --licenses` standart çıktıya gömülü lisans metinlerini yazar.

`tools/IconGen` kurulu oyunun ikonunu kaynak verisi olarak (oyun kodunu çalıştırmadan)
çıkarır. `THIRD-PARTY-NOTICES.md` lisansları ve ikon sahipliğini açıklar.
