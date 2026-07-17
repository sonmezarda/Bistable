# Faz 9 — Canlı Döngü: İzle → Artımlı Elaborasyon → Tazele

**Plan kaydı:** [docs/ROADMAP.md](../ROADMAP.md) · Kaynak: [VISION_GAP_ANALYSIS.md §7 H1+H2](../VISION_GAP_ANALYSIS.md)
**Öncelik:** P0 — **vizyonun kalbi**
**Önkoşul:** Faz 7 (dürüst şematik) + Faz 8 (batch değer kanalı) kapanmış olmalı.
**Durum (2026-07-17):** Otomatik kapılar tamamlandı; sahibin `riscv_single_cycle`
ile elle kaydet→şematik ve IDE çalışma yüzeyi kabulü bekleniyor. Sahibin
IDE-benzeri kaynak/diagnostics talebi P9-5 ve P9-8 kapsamına alındı; geniş
XAML/platform modernizasyonu ROADMAP sırasındaki Faz 14'te kalır.
**Hedef deneyim:** *Dosyayı kaydet → ≤1–2 sn içinde şematik güncel → değerler
canlı akmaya devam ediyor → hata varsa satır referanslı panelde.*

**Faz kapısı (kabul):**
- Örnek ölçekli tasarımda (riscv_single_cycle) kaydet→şematik-güncel süresi
  ≤2 sn (otomatik ölçüm testi + sahibin elle denemesi).
- Sözdizimi/elaborasyon hatasında: panelde `dosya:satır` tıklanabilir kayıt;
  şematik "bayat" (stale) rozetiyle solmuş; düzeltince kendiliğinden toparlıyor.
- Port arayüzü değişmeyen kayıtta mevcut worker ve canlı değerler kesintisiz;
  arayüz değişiminde arka planda yeni worker derlenip şeffaf takas.
- Otomatik yeniden derleme aç/kapa ayarı; debounce süresi yapılandırılabilir.

## Görevler

| ID | Görev | Tahmin |
|---|---|---|
| P9-1 ✓ | `ProjectFileWatcherService`: `FileSystemWatcher` (sources + includeDirs), 100–5000 ms yapılandırılabilir debounce, olay birleştirme (coalescing) | 1 g |
| P9-2 ✓ | Otomatik yeniden elaborasyon: mevcut `DesignLoadService.LoadAsync` yolunun izleyiciye bağlanması; koşarken yeni kayıt gelirse iptal+yeniden (mevcut cancellation altyapısı) | 1 g |
| P9-3 ✓ | Modül-düzeyi diff: eski/yeni `DesignAst` modül içerik-hash karşılaştırması → kirli modül kümesi; `ElkSchematicEngine` anahtarı yalnız erişilebilir modül kataloğunu içerir → ilgisiz scope değişmeden önbellekte kalır | 1,5 g |
| P9-4 ✓ | Worker sıcak-takas politikası: semantik HDL değişiminde yeni worker ayrı artifact slotunda hazırlanır; eski worker ve canlı değerler hazır olana kadar korunur, girişler aktarılıp atomik takas yapılır; arayüz değişimi ayrıca hash ile saptanır | 2 g |
| P9-5 ✓ | Hata yüzeyi: Verilator stderr ayrıştırma (`%Error: dosya:satır:kolon`) → Problems paneli (tıklanınca editörde satıra git); şematikte stale rozeti | 1 g |
| P9-6 ✓ | Ayarlar: kök `liveReload { enabled, debounceMs }` proje sözleşmesine + global Preferences'a | 0,5 g |
| P9-7 ✓ | Testler: gerçek dosya olayı/debounce; diff kirli-kümesi; uçtan uca "dosyaya yaz → yeni elaborasyon → graf değişti → hata → otomatik toparlan"; parser ve sıcak-takas entegrasyonu | 1,5 g |
| P9-8 ✓ | Düzenlenebilir AvaloniaEdit Source dokümanı: proje dosya listesi, Ctrl+S/Ctrl+F, canlı-reload kontrolleri ve tıklanabilir Problems paneli | 2 g |

**Toplam tahmin:** ~8,5 gün (+stretch 2 g)

## Kod dokunuş noktaları

- **Yeni:** `src/Bistable.App/Services/ProjectFileWatcherService.cs`,
  `Services/ElaborationDiagnosticsParser.cs`
- `src/Bistable.App/Services/DesignLoadService*` — tetiklenebilir/iptal-birleştirmeli hâle
- `src/Bistable.App/Services/Routing/Elk/ElkSchematicEngine.cs` — önbellek anahtarı denetimi
- `src/Bistable.Verilator/SimulationWorkerBuilder.cs` + operasyon koordinatörü — arka plan derleme/takas
- `MainWindowViewModel` — **yalnız bağlama**; yeni mantık servislerde (Faz 12 ayrıştırmasına borç bırakma)
- `src/Bistable.Core/Projects/ProjectConfiguration` — liveReload ayarı

## Uygulama sonucu (2026-07-17)

- `ProjectFileWatcherService` proje dosyasını, açık kaynakları ve include
  ağaçlarını izler. `ProjectReloadCoordinator`, kayıt fırtınasını tek kümeye
  indirir; yeni kayıt aktif elaborasyonu iptal edip en yeni kümeyi çalıştırır.
- `AstModuleDiff` SHA-256 içerik ve top-port arayüz hash'leri üretir. ELK LRU
  anahtarı yalnız scope'tan erişilebilir modülleri kapsar; ilgisiz modül
  değişikliği mevcut layout sonucunu geçersiz kılmaz.
- Şema başarılı XML elaborasyonundan hemen sonra yenilenir. Native worker iki
  dönüşümlü artifact slotundan birinde arka planda hazırlanır; giriş değerleri
  aktarılıp ilk frame alındıktan sonra eski worker ile takas edilir. Hazırlama
  başarısızsa yeni şema güncel kalır ve eski simülasyon çalışmaya devam eder.
- Verilator hataları `ElaborationDiagnostic` kayıtlarına dönüşür. Son iyi şema
  soluk `STALE` rozetiyle korunur; Problems kaydı Source editöründe dosya/satır/
  kolona gider ve sonraki geçerli kayıtta durum kendiliğinden temizlenir.
- `Source` dock'u dosya gezgini, düzenlenebilir AvaloniaEdit yüzeyi, kaydet/ara
  kısayolları, Problems paneli ve canlı-reload ayarlarını tek IDE-benzeri
  çalışma alanında birleştirir.

## Doğrulama kaydı

- `LiveReloadPerformanceTests`: `riscv_single_cycle` XML elaborasyonu izole
  ölçümde **243 ms** (kapı: ≤2000 ms).
- `LiveReloadWorkspaceTests`: kaynak değişimiyle primitive/graf yenilenmesi,
  ≤2 sn kapısı, sözdizimi hatasında stale+dosya/satır navigasyonu ve düzeltince
  otomatik toparlanma.
- `SimulationWorkerHotSwapServiceTests`: replacement hazırlanırken eski worker
  okunabilir; replacement giriş değerini devralır.
- `dotnet build Bistable.slnx`: 0 warning / 0 error. Son tam çözüm koşusunda
  919/924 ilk geçişte yeşil; yalnız belgelenmiş üç `ElkRunnerCancellation*`,
  bir `SimulationWorkerClientCancellation*` ve bir `GateSchematicPerformance*`
  paralel-yük zamanlama testi düştü. İlgili ailelerin altı testi de izole yeniden
  koşuda geçti. 14/14 golden snapshot değişmeden geçti.
- **Açık kapı:** sahibin gerçek uygulamada `samples/riscv_single_cycle` ile
  Source dock'u, kaydet→şematik gecikmesi, hata/stale/toparlanma ve canlı değer
  sürekliliğini görsel olarak kabul etmesi.

## Riskler / notlar

- **UI thread disiplini:** tüm izleme/elaborasyon/derleme arka planda; guardrail
  zaten "layout/measure UI thread'e dönmez" diyor — aynı kural burada da geçerli.
- Watcher fırtınası (editörlerin çoklu yazması): debounce + tek kuyruk; test edilmeli.
- Büyük tasarımda elaborasyon süresi: `--xml-only` hızlıdır ama ölçülmeli;
  gerekirse "yalnız kaydedilen dosyanın modülleri" için Verilator'a dosya-seti
  daraltması sonraki iterasyon.
- Bu faz kapandığında ürün, vizyon cümlesinin kendisini yapar; tanıtım
  GIF'i/videosu bu fazın kapısında çekilmeli (H6 görünürlüğü).
