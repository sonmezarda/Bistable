# Faz 8 — Canlı Değer Kanalı: `ReadSignals` Batch IPC

**Plan kaydı:** [docs/ROADMAP.md](../ROADMAP.md) · Kaynak: [VISION_GAP_ANALYSIS.md §3.1, §5](../VISION_GAP_ANALYSIS.md)
**Öncelik:** P0
**Önkoşul:** yok (Faz 7 ile paralel geliştirilebilir; ROADMAP sırasına göre 7'den sonra kapanır)
**Durum (2026-07-16):** **Tamamlandı.** Uygulama, gerçek-worker entegrasyonu,
ölçüm kapısı ve tam çözüm doğrulaması tamamlandı.
**Hedef:** Görünür bir şematik karesinin canlı değerleri **tek IPC turu** ile
gelsin. Önce: görünür N sinyal = N × `ReadSignal`; şimdi normal bir frame =
tek `ReadSignals` stdin/stdout turu.

**Faz kapısı (kabul):**
- Protokolde `ReadSignals` (çoklu yol → çoklu değer) komutu; üretilen C++ worker
  uyguluyor; `SimulationWorkerClient.ReadSignalsAsync` var.
- `LiveProbeService` frame-görünür kümesini tek istekte tazeliyor; sinyal
  başına ayrı komut yolu yalnız tekil sorgular için kalıyor.
- Ölçüm testi: ≥100 görünür probe'lu senaryoda frame tazeleme tek round-trip
  ve eski yoldan ölçülebilir hızlı (bütçe testi, önce/sonra kayıtlı).
- Eski worker ikilileriyle uyumluluk: protokol sürümü yükselir; sürüm
  uyuşmazlığında worker yeniden derlenir (mevcut build akışı).

## Görevler

| ID | Görev | Tahmin |
|---|---|---|
| P8-1 ✅ | Protokol: `SimulationCommandType.ReadSignals`, komut gövdesi (`IReadOnlyList<string> Paths`), `SignalsReadResult` yanıtı (`src/Bistable.Protocol`) | 0,5 g |
| P8-2 ✅ | Üretilen C++ worker (`SimulationWorkerBuilder`): `readSignals` döngüsü; yol-başına sonuç, tek JSON yanıt satırı | 1 g |
| P8-3 ✅ | `SimulationWorkerClient.ReadSignalsAsync` — atomik komut/yanıt disiplini + 4.096 yolluk parçalama | 0,5 g |
| P8-4 ✅ | `LiveProbeService`: frame görünür kümesi → tek batch; önbellek güncellemesi tek `ValuesUpdated` olayı | 1 g |
| P8-5 ✅ | Protokol v3 + `hello`/capabilities; GUI Build her ikiliyi yeniden üretir, `StartAsync` eski/uyumsuz ikiliyi reddeder | 0,5 g |
| P8-6 ✅ | Protokol round-trip, gerçek Verilator batch, 4K limit/chunk ve 128-probe bütçe testleri | 1 g |

**Toplam tahmin:** ~4,5 gün

## Kod dokunuş noktaları

- `src/Bistable.Protocol/SimulationCommandType.cs`, `WorkerResponse.cs`, yeni `SignalsReadResult.cs`
- `src/Bistable.Verilator/SimulationWorkerBuilder.cs` (C++ dispatcher, protokol sürümü)
- `src/Bistable.App/Services/SimulationWorkerClient.cs`
- `src/Bistable.App/Services/LiveProbeService.cs`
- Testler: `Bistable.Tests/Protocol/*`, `VerilatorIntegrationTests`

## Riskler / notlar

- Yanıt boyutu: çok geniş vektörlerde tek satır JSON büyür — makul üst sınır
  (örn. 4K yol/istek) + parçalama.
- Var olmayan yol: yanıt yol-başına `ok/err` taşımalı; tek bozuk yol tüm frame'i
  düşürmemeli.
- Bu faz **Faz 9'un ön şartı**: canlı döngü değer basacaksa bu kanal olmadan
  "canlı" hissi büyük görünümde ölür.

## Ölçüm ve kabul kanıtı (2026-07-16)

- 128 görünür scalar probe, gecikmeli deterministik worker:
  - eski tekil yol: **533,9 ms / 128 round-trip**;
  - batch yol: **7,7 ms / 1 round-trip**.
- Gerçek Verilator `counter` worker testi, iki geçerli + bir bilinmeyen yolu tek
  round-trip'te döndürüyor; bilinmeyen yol yalnız kendi outcome'unu hata yapıyor.
- 4.097 yol istemci tarafından iki komuta parçalanıyor; ham worker isteğinde
  4.096 üstü açık `ErrorResponse` oluyor.
