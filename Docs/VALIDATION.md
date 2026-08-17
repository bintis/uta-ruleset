# validation record

## Baseline

- input package version: `0.6.0`;
- delivered project version: `0.7.0`;
- target framework: .NET 8 / C# 12.

## Checks completed in the generation environment

- `git diff --check` on the complete modification set;
- UTF-8, LF, final-newline and trailing-whitespace checks for all 24 modified/new text files;
- XML parsing for all three `.csproj`/`.props` project files;
- C# token/delimiter scanning for all 17 modified/new C# files;
- Markdown code-fence validation for all 11 documentation files;
- removal scan for obsolete `UtaModChallenge`, `UtaChallengeHealthProcessor` and `ChallengeMode` symbols;
- confirmation that `RecordMicrophone` remains only as the intentionally reserved legacy enum key;
- confirmation that `UtaModRecording.cs` and `UtaModScoringMode.cs` are present, the former Challenge file is absent, and the project version is exactly `0.7.0`;
- code-path review for mode combinations, signed microphone latency, bounded realtime scoring windows, replay snapshots and recording/archive finalisation.

## Runtime invariants covered by added or updated tests

- signed negative microphone latency;
- bounded capture queue with negative calibration;
- recording-only Pitch replay mapping without a formal scoring session;
- default ignored judgement and score-free native processor state;
- `评分模式` enabling native vocal judgements, score and note-driven health;
- `Recording` (`REC`) MOD registration and Auto incompatibility;
- continuous Uta units in native `ScoreInfo`;
- native/archive integer score parity;
- frames behind the committed watermark cannot mutate a judgement;
- phrase aggregation from committed note scores;
- a 300-note long-session regression in which each realtime note score receives only a bounded local frame window while the final full-performance score remains `1,000,000`.

The existing foundation tests continue to cover grade bands, Bad/fault separation,
Pitch gate, vibrato, Transpose/OCT, uta.song 0.1/0.2 note kinds, stream/batch
invariance, archive checksums, score-ID linking and expression reports.

## Checks not available in the generation environment

The container does not provide a .NET SDK, MSBuild or C# compiler, and external
package restoration is unavailable. Therefore this package does **not** claim that
`dotnet format`, compilation or NUnit execution completed here. Run the repository
CI commands before merging or distributing a binary:

```sh
dotnet restore osu.Game.Rulesets.Uta.Tests/osu.Game.Rulesets.Uta.Tests.csproj

dotnet format osu.Game.Rulesets.Uta/osu.Game.Rulesets.Uta.csproj \
  --no-restore --verify-no-changes

dotnet build osu.Game.Rulesets.Uta/osu.Game.Rulesets.Uta.csproj \
  -c Release --no-restore

dotnet test osu.Game.Rulesets.Uta.Tests/osu.Game.Rulesets.Uta.Tests.csproj \
  -c Release --no-restore
```
