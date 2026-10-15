# Contributing to Star Blaster

Thanks for wanting to help! Bug reports, balance tweaks and new ideas are all welcome.

## Getting set up

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download).
2. Fork and clone the repo, then:

   ```sh
   dotnet run                    # play
   dotnet run -- --selftest      # headless rule checks
   ```

## Before you open a pull request

- `dotnet build -c Release` has no warnings.
- `dotnet run -c Release -- --selftest` prints `ALL CHECKS PASSED`.
- If you change gameplay rules, add or extend a scenario in [Tests/SelfTest.cs](Tests/SelfTest.cs).
- If you change visuals, attach a screenshot. `--shot out.png --seed N` is deterministic,
  so before/after images are easy to compare.

## Code layout

| Folder | What lives there |
|---|---|
| `Game/` | Pure game logic: no Raylib calls, fully testable headless |
| `Platform/` | Rendering, generated sprites and synthesized sound (Raylib) |
| `Tests/` | The self-test harness behind `--selftest` |

Keep the game logic free of platform calls so the self-test can keep running without a window.

## Style

Follow the surrounding code: file-scoped namespaces, nullable enabled, short comments that explain *why*.
The look is deliberately retro: sprites and sounds are generated in code, so please don't add binary assets.
