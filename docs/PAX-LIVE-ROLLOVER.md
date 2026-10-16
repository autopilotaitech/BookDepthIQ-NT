# PAX extensions stop advancing — beta.14 correction

Owner reported extension lines stopping around EXT6 during the October16 selloff. There is no
six-level cap in the chart calculation. Installed chart AddOn file hash matched the repository;
saved OR start08:30 Chicago, duration30seconds, end17:00 and NQ step65 were not altered.

A rollover regression exposed a bridge defect: an Update callback containing only the new index
published its forming bar but never closed the previous index. Cached bars could remain forming
forever. PAX intentionally ignores forming bars, so extensions could freeze at the levels present
when history loaded even while candles continued updating. This reproduces a credible freeze
mechanism; the owner's specific native event stream was not captured, so exact runtime attribution
is not claimed as proven.

The bridge now tracks its last published index and, on rollover, republishes the previous endpoint
from the authoritative live series together with new bars. Its final OHLCV is retained and
forming=false; only the newest endpoint remains forming. Same-index ticks still emit only the
changed bar. Sends are serialized so callback packets/revisions do not interleave. Older endpoints
cannot be relabeled forming merely because a delayed history batch ended there.

## Evidence

- New-index-only callback test failed before the fix and passes afterward. It asserts final previous
  low/volume, completed previous flag and current forming flag, not just packet presence.
- Twelve consecutive rollovers retain exactly one forming bar; same-bar update does not republish
  the previous bar repeatedly.
- Independent selloff calculation reaches EXT7 and EXT21, ruling out a six-level algorithm cap.
- Full suite:416passed (370 Core/DOM +46 Bridge), zero failed/skipped. Release and native C#6/net48
  installed-DLL compilation clean. No actual orders, connections or account changes used.

## Upgrade / acceptance

Replace `NinjaTrader/BookDepthIQChartBridge.cs` from beta.14 in the installed NT AddOns folder and
recompile in NinjaScript Editor (F5) when safe. Reconnect/reload chart history after activating the
newly compiled AddOn so already-stale forming flags are replaced. An EXE-only update does not
repair the old AddOn. Installed files/running sessions have not been changed automatically.

Use beta.14 app for the complete development build (includes beta.11–13 improvements). DOM bridge
unchanged from beta.12 and still requires order capability3. No extension math, trigger thresholds,
session settings, order rules or rendering limit were relaxed to hide this problem.

Native SIM acceptance remains: observe multiple actual30second rollovers and compare newly
activated extensions with the supplied PAX indicator. This regression/build proof is not live-provider
or HFT certification. Main and the published release are unchanged by this feature-branch build.
