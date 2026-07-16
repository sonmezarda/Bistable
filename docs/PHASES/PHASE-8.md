# Faz 8 — Canlı Değer Kanalı: `ReadSignals` Batch IPC

**Plan kaydı:** [docs/ROADMAP.md](../ROADMAP.md) · Kaynak: [VISION_GAP_ANALYSIS.md §3.1, §5](../VISION_GAP_ANALYSIS.md)
**Öncelik:** P0
**Önkoşul:** yok (Faz 7 ile paralel geliştirilebilir; ROADMAP sırasına göre 7'den sonra kapanır)
**Hedef:** Görünür bir şematik karesinin canlı değerleri **tek IPC turu** ile
gelsin. Bugün: görünür N sinyal = N × `ReadSignal` stdin/stdout turu.

**Faz kapısı (kabul):**
- Protokolde `ReadSignals` (çoklu yol → çoklu değer) komutu; worker şablonu
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
| P8-1 | Protokol: `SimulationCommandType.ReadSignals`, komut gövdesi (`IReadOnlyList<string> Paths`), `SignalsReadResult` yanıtı (`src/Bistable.Protocol`) | 0,5 g |
| P8-2 | Worker şablonu C++ (`native/worker-template`): `read_signals` — mevcut tekil okuma mantığının döngüsü; tek JSON yanıt satırı | 1 g |
| P8-3 | `SimulationWorkerClient.ReadSignalsAsync` — mevcut atomik komut/yanıt disiplinine uyar (iptal drain'i dahil) | 0,5 g |
| P8-4 | `LiveProbeService`: `_visibleProbePaths` frame kümesi → Eval/Tick sonrası tek batch; önbellek güncellemesi tek event ile (UI'yi N kez uyandırma) | 1 g |
| P8-5 | Protokol sürüm damgası + uyuşmazlıkta yeniden-derleme yolu; worker `hello`/`capabilities` alanına sürüm | 0,5 g |
| P8-6 | Testler: protokol round-trip birim testi; `VerilatorIntegrationTests`'e batch senaryosu; 100+ probe bütçe testi | 1 g |

**Toplam tahmin:** ~4,5 gün

## Kod dokunuş noktaları

- `src/Bistable.Protocol/SimulationCommandType.cs`, `WorkerResponse.cs`, yeni `SignalsReadResult.cs`
- `native/worker-template/…` (komut dispatcher + okuma döngüsü)
- `src/Bistable.Verilator/SimulationWorkerBuilder.cs` (şablon sürümü), `SimulationWorkerClient`
- `src/Bistable.App/Services/LiveProbeService.cs`
- Testler: `Bistable.Tests/Protocol/*`, `VerilatorIntegrationTests`

## Riskler / notlar

- Yanıt boyutu: çok geniş vektörlerde tek satır JSON büyür — makul üst sınır
  (örn. 4K yol/istek) + parçalama.
- Var olmayan yol: yanıt yol-başına `ok/err` taşımalı; tek bozuk yol tüm frame'i
  düşürmemeli.
- Bu faz **Faz 9'un ön şartı**: canlı döngü değer basacaksa bu kanal olmadan
  "canlı" hissi büyük görünümde ölür.
