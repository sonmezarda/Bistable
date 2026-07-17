# Faz 9.5 — Workbench Architecture Spike: Theia + .NET Engine Host

**Plan kaydı:** [docs/ROADMAP.md](../ROADMAP.md)
**Karar sahibi:** Ürün sahibi, 2026-07-17
**Durum:** Aktif — stratejik yön sahibi tarafından kabul edildi; göç kapıları uygulanıyor
**Öncelik:** P0 karar kapısı

## Neden bu faz var?

Faz 9'un watcher, AST diff/cache, diagnostics, stale-schematic ve worker
hot-swap backend kapıları tamamlandı. Buna karşılık sahibi, Avalonia tabanlı
Source yüzeyini manuel kabulde reddetti:

- araç çubuğu dar alanda çakışıyor;
- seçilen dosyanın metni görünmüyor;
- Project paneli kapatılamıyor;
- belge/panel yaşam döngüsü VS Code benzeri serbest bir workbench oluşturmuyor.

Kod da bunun yalnız görsel cila olmadığını doğruluyor: mevcut dockable'lar
`CanClose/CanDrag/CanFloat/CanPin` bayraklarıyla kilitli ve Source UI tek büyük
programatik C# görünümünde kuruluyor. Yeni UI-ağır özellikleri aynı kabuğa
eklemek taşıma maliyetini büyüteceği için Faz 10'dan önce ölçümlü bir platform
kararı gerekir.

## Karar

- **Sahip kararı, 2026-07-17:** Bistable'ın ana ürün yönü Theia tabanlı markalı
  IDE olacaktır. Yeni UI-ağır özellikler Avalonia kabuğuna eklenmeyecek; önce
  Theia dilimi uygulanacaktır.
- Code-OSS fork'u yapılmayacak: upstream ürünleştirme/merge yükü alınmayacak.
- Ana POC, AMD Vitis Unified IDE ile aynı temel yaklaşım olan **Eclipse Theia
  tabanlı markalı masaüstü ürün** olacaktır.
- Çalışan HDL/simülasyon/sentez kodu TypeScript'e yazılmayacak. Yeni bir
  `.NET Bistable.Engine.Host` süreci, kararlı ve sürümlü JSON-RPC/stdio sınırı
  üzerinden Theia frontend'e hizmet verecek.
- VS Code extension uyumluluğu dil özellikleri ve genel komutlar için
  korunacak; şematik/waveform gibi ürün widget'ları Theia extension olacaktır.
- POC kararı çıkana kadar Avalonia uygulaması korunur; silme veya büyük taşıma
  yapılmaz.

Bu bir "hemen Avalonia'yı sil" kararı değildir. Electron paketleme, canlı
elaborasyon, RTL şematik, waveform/simülasyon ve RV32 performans kapıları
kapanana kadar Avalonia karşılaştırma ve geri dönüş yüzeyi olarak korunur.

## Kabul kapısı

1. Theia Electron uygulaması tek komutla build/start olur; Explorer, Monaco,
   Problems, Terminal ve Settings hazır workbench parçaları çalışır.
2. Dosya sekmeleri kapanabilir ve taşınabilir; Project/Problems/Terminal
   panelleri gizlenebilir, yeniden açılabilir ve alanlar arasında taşınabilir.
3. `Bistable.Engine.Host` JSON-line protokolünde en az `hello`, `loadProject`
   ve `shutdown` isteklerini sunar; protokol sürümü handshake ile doğrulanır.
4. Theia, `samples/riscv_single_cycle` projesini engine host üzerinden yükler;
   top modül, port/modül sayısı, Verilator sürümü ve diagnostics görüntülenir.
5. İlk Bistable widget'ı kapatılabilir/taşınabilir bir workbench view olarak
   açılır ve engine bağlantı durumunu gösterir.
6. Kaydet→yeniden elaborasyon POC'si ≤2 sn kapısını korur; hata Problems'e
   dosya/satır/kolon olarak düşer ve düzeltince temizlenir.
7. Soğuk başlangıç, RSS, `riscv_single_cycle` yükleme ve temel şematik pan/zoom
   ölçümleri kaydedilir. Sonuçta yazılı **go / no-go** kararı çıkar.

## Görevler

| ID | Görev | Durum |
|---|---|---|
| P9.5-1 | Theia 1.73.x workspace; sürümler sabitlenmiş lockfile ve tekrar üretilebilir komutlar | **Browser tamam; Electron yerel paketleri bekliyor** |
| P9.5-2 | `Bistable.Engine` servis sınırı: UI bağımsız elaboration/project DTO'ları | **Tamamlandı** |
| P9.5-3 | `Bistable.Engine.Host`: sürümlü JSON-line RPC, süreç yaşam döngüsü, diagnostics | **Tamamlandı** |
| P9.5-4 | Theia backend extension: engine host süreç sahipliği ve frontend proxy | **Tamamlandı** |
| P9.5-5 | Bistable workbench widget + Explorer/Monaco/Problems entegrasyonu | **Tamamlandı; sahibi yön kabulü verdi** |
| P9.5-6 | Live reload ve şematik veri/render köprüsü | **Dockable top RTL + ELK sembolleri tamam; hiyerarşi aktif iş** |
| P9.5-7 | Otomatik testler, performans ölçümü ve ADR go/no-go sonucu | Aktif |

## İlk dilim sonucu — 2026-07-17

- `Bistable.Engine` elaboration ve Verilator diagnostic parser sahipliğini UI'dan
  aldı; Avalonia `DesignLoadService` bu servise delegasyon yapan uyumluluk
  adaptörü oldu.
- Engine host protocol v1 üzerinde `hello`, `loadProject`, `shutdown` sunuyor.
  `riscv_single_cycle` smoke testinde top/modül/port/Verilator özeti alındı;
  bozuk HDL testi Problems'e taşınabilir dosya/satır/sütun verisini doğruluyor.
- Theia browser workbench, Explorer/Monaco/Problems/Terminal/Settings ve ilk
  kapatılabilir Bistable widget'ıyla build/start oluyor.
- Bistable widget'ları React'i doğrudan paketlemez; Theia'nın paylaşılan React
  runtime'ını kullanır. `npm test`, ikinci bir React kopyasının yeniden yalnız
  başlığı görünen boş widget üretmesini engelleyen kimlik sözleşmesini doğrular.
- Ürün çalışma zamanı audit'i: 1 low, 23 moderate, 0 high, 0 critical. Geliştirme
  zincirindeki kalan high/critical kayıtlar ve Open VSX marketplace zinciri
  kalıcı ürün kararı öncesinde kapatılacak.
- Electron native build'i bu makinede `libxkbfile-dev` ve `libsecret-1-dev`
  kurulmadan kapanmıyor. Browser hedefi karar çalışmasını bloklamıyor; masaüstü
  kabul kapısı henüz kapanmış sayılmaz.

## Geçiş uygulaması — ilk canlı dilim, 2026-07-17

- Workspace kökündeki `.bistable.json` açılışta otomatik bulunup elaborasyon
  başlatılıyor; manuel düğme artık yalnız zorla yeniden yükleme içindir.
- `.sv/.svh/.v/.vh` ve proje dosyası kayıtları 400 ms debounce edilir. Aktif
  elaborasyon sırasında gelen kayıtlar üst üste bindirilmez; tamamlanınca tam
  bir kez en yeni durum çalıştırılır.
- Hatalı elaborasyon Verilator diagnostics'ini Theia Problems'e koyar; başarılı
  takip turu önceki Bistable marker'larını temizler. Her iki yaşam döngüsü
  otomatik sözleşme testiyle kilitlidir.
- `loadProject` cevabı top-modül için layout-agnostic şematik düğüm/kenar DTO'su
  taşır. Şematik yan panele gömülmez; kaynak dosyalar gibi merkez document
  dock'unda açılır, kapanır ve başka tab gruplarına taşınabilir.
- ELK layered/orthogonal layout Theia backend sürecinde çalışır; renderer ana
  thread'inde layout yoktur. SVG renderer port, mux, AND/OR/XOR, inverter,
  buffer, arithmetic/comparator, DFF/latch, memory, splitter/joiner ve module
  instance için RTL sembolleri ve gerçek pin konumları çizer.
- Top-modül document yolu tamamdır. Instance içine girme, breadcrumb ve her
  modül için ayrı document kimliği bir sonraki hiyerarşi dilimidir.

## Hedef mimari

```text
Bistable Theia Desktop (Electron)
├── Monaco / Explorer / Problems / Terminal / Settings
├── Bistable project & simulation widgets
├── Schematic documents (ELK geometry → SVG; layout Theia backend'de)
└── Waveform widget
                 │
                 │ versioned JSON-RPC over stdio
                 ▼
Bistable.Engine.Host (.NET)
├── Bistable.Engine (application services + transport DTOs)
├── Bistable.Core
├── Bistable.Verilator
├── Bistable.Yosys
└── Bistable.Protocol
```

## Guardrail'ler

- Var olan per-bit net kimlikleri ve seçim semantiği transport sırasında
  korunur; frontend için kozmetik bus collapse yapılmaz.
- Theia frontend ana thread'inde ELK layout veya tam RV32 graph taraması yoktur;
  layout backend sürecinde kalır.
- Engine host stdout'u yalnız protokol frame'leri içindir; loglar stderr'e gider.
- Host kapanınca sahip olduğu Verilator/Yosys/worker alt süreçlerini bırakmaz.
- POC, Avalonia kodunu silmek veya mevcut feature setini yeniden yazmak için
  otomatik yetki değildir; kalıcı göç go kararından sonra ayrı planlanır.
