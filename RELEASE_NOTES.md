# TLOUP1 v1.0 — The Last of Us Part I Trainer

**Tek dosya, taşınabilir, açık kaynak.** Kurulum yok, kayıt defteri yok, kalıntı yok.

## 🎮 Özellikler

| Tuş | İşlev |
|-----|-------|
| **Insert** | Pencereyi gizle / göster (programı kapatmaz) |
| **F1** | **Sınırsız mühimmat** — şarjör, yedek mermi, yakın dövüş dayanıklılığı, sarf malzemesi, üretim **ve çakı tüketimi** |
| **F2** | **Sınırsız sağlık** — hasar kontrollerini aç/kapat |
| **—** (sağ üst) | Paneli gizle |
| **×** (kırmızı) | Normal çıkış — tüm yamaları geri alır ve kapanır |

> ⚠️ **Tüm silah verme özelliği kaldırılmıştır.** Sadece yukarıdaki iki hile mevcuttur.

## 🔧 Çakı (Shiv) Koruması — Nasıl Çalışır?

- Oyunun çakı tüketim fonksiyonu statik analizle bulunur (iki eşya etiketi: `t1x-shiv` ve `t1x-shiv-upgrade`, PE `.pdata` fonksiyon sınırları, iki ayrı envanter `CALL` sitesi).
- F1 açıldığında **sadece bu iki çağrı NOPlanır** (`CALL` → `90 90 90`).
- F1 kapatıldığında / program kapanırken / çökürse **orijinal kod geri yüklenir** (bellek kurtarma koruması).
- **Şart:** Envanterde en az 1 çakı olmalı. Program çakı **vermez**, sadece tüketimi engeller.
- Oyun güncellenirse ve fonksiyon değişirse analiz "yok: çakı" der; sadece çakı yaması kapanır, mermi/dövüş/sarf **çalışmaya devam eder**.

## 🚀 Kullanım

1. `TLOUP1.exe` indir (tek dosya, ~142 MB)
2. Çift tıkla çalıştır
3. Oyun açık değilse **temalı "Önce oyunu aç" önerisi** çıkar — **zorunlu değil**, oyunu sonra açıp programa dönersen otomatik bağlanır
4. Oyunda F1 / F2 ile hileleri aç/kapat
5. Insert ile paneli gizle/göster, × ile temiz çıkış

## 📦 Portable Prensipleri

- **Tek EXE** — harici .NET runtime, DLL, yapılandırma dosyası yok
- **Kurulum yok** — kayıt defteri, başlangıç menüsü, hizmet, görev, güncelleyici oluşturmaz
- **Günlük bellekte** — AppData'ya yazmaz
- **Kaldırıcı yok** — dosyayı silmek yeterli
- **.NET 8 self-contained** — `%TEMP%\.net\TLOUP1` klasörüne geçici çözümleme yapar (bu .NET'in normal davranışıdır, sıfır kalıntı vaadi vermez)

## ⚖️ Lisans ve Uyarılar

- **Kişisel/öğrenim amaçlı.** Çevrimiçi/çok oyunculu modlarda kullanmayın. Hesap yasaklama riski vardır.
- Oyun dosyalarını, kayıt defterini veya kaydedilmiş oyunu **değiştirmez**. Sadece çalışan süreçte bellek yaması uygular.
- Kod: MIT lisansı. Üçüncü taraf lisanslar EXE içinde gömülü (`--licenses` ile görüntülenebilir).
- The Last of Us™ Part I, Naughty Dog ve Sony Interactive Entertainment'in ticari markasıdır.

## 🔨 Derleme (Geliştiriciler İçin)

```powershell
# Gereksinimler: .NET 8 SDK, Windows x64
dotnet restore TLOUTrainer.sln
dotnet build TLOUTrainer.sln -c Release
dotnet test TLOUTrainer.sln -c Release
./tools/Build-Portable.ps1   # portable/TLOUP1/TLOUP1.exe üretir
```

## 📂 Depo Yapısı

```
src/TLOUTrainer/        # Ana uygulama (WinForms, .NET 8)
  Core/                 # Analiz, yama yazma, kurtarma, bellek
  UI/                   # Temalı arayüz, diyaloglar
  Native/               # P/Invoke imzaları
tools/
  Build-Portable.ps1    # Tek EXE üretimi + lisans gömme + testler
  Test-Portable.ps1     # UI duman testi (Insert/×/F1/F2)
  Inspector/            # Statik analiz CLI (--analyze, --func, --xref...)
tests/Trainer.Tests/    # Birim + kurtarma + sentetik PE testleri
portable/TLOUP1/        # Dağıtım çıktısı (sadece TLOUP1.exe)
```

## 📝 Sürüm Geçmişi

- **v1.0** — İlk sürüm: F1 (mermi + çakı + dövüş + sarf/üretim), F2 (sağlık), Insert gizle/göster, temalı başlangıç uyarısı, tek EXE portable, kurtarma koruması, açık kaynak.