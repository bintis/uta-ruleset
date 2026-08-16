# validation record

## Baseline

- uta-ruleset commit: `d0bac5ce3c0441877ef9cac7ec4e01b11e7d545f`
- osu!lazer API/runtime reviewed: `2026.804.2-lazer`
- target framework: .NET 8 / C# 12

## Checks completed in the generation environment

- XML parsing for both modified `.csproj` files;
- lexical delimiter/string/comment scan for every included C# source file;
- duplicate top-level type-name scan across the generated source set;
- Markdown code-fence balance;
- LF/final-newline/tab/trailing-whitespace checks;
- `git diff --check`;
- independent reference calculation for Pitch similarity boundaries, grade bands,
  pitch-gated profiles and short-note Technique fallback;
- review against lazer's `Ruleset`, `ScoreProcessor`, `HealthProcessor`,
  `JudgementResult`, `DrawableRuleset`, `Player.ImportScore()` and custom replay
  extension points;
- clean patch application and clean `git am` import are performed when the package is
  assembled.

## Reference calculation results

```text
similarity:
  0c=1000, 35c=1000, 65c=910, 75c=880,
  95c=725, 100c=687, 150c=300, 250c=0

grades:
  exact=Perfect, 65c=Great, 95c=Good, 100c=Bad, silence=Miss

stable 100-cent-sharp tone:
  Faithful=673, Stable=664, Technique=659, selected=Faithful
```

## Checks not available in the generation environment

The container did not provide a .NET SDK, C# compiler or a reachable package feed.
Therefore this package does **not** claim that `dotnet format`, compilation or NUnit
execution was completed here. Run the repository CI commands after import:

```sh
dotnet restore osu.Game.Rulesets.Uta.Tests/osu.Game.Rulesets.Uta.Tests.csproj

dotnet format osu.Game.Rulesets.Uta/osu.Game.Rulesets.Uta.csproj \
  --no-restore --verify-no-changes

dotnet build osu.Game.Rulesets.Uta/osu.Game.Rulesets.Uta.csproj \
  -c Release --no-restore

dotnet test osu.Game.Rulesets.Uta.Tests/osu.Game.Rulesets.Uta.Tests.csproj \
  -c Release --no-restore
```

The package deliberately leaves live gameplay activation disabled, so passing the
kernel/archive tests is necessary but not sufficient for enabling native scoring.
The gameplay-equivalence checklist is in `SCORING_INTEGRATION.md`.
