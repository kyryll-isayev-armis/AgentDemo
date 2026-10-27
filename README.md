# GRID//ZERO

A neon-noir arcade take on tic-tac-toe built with ASP.NET Core Blazor (.NET 10, interactive server rendering): VECTOR (X) versus ORBIT (O), Local 2P or vs AI (Rookie, Tactician, Oracle), Classic or DECAY rules, best-of 1/3/5, themes, history.

## Requirements

- .NET SDK 10.0 (developed with 10.0.403)

## Run

```powershell
dotnet run --project src\AgentDemo.Web
```

Then open the URL printed in the console (default `http://localhost:5057` with the `http` profile: `dotnet run --project src\AgentDemo.Web --launch-profile http`).

## Test

```powershell
dotnet test
```

## Structure

```
AgentDemo.slnx
src\AgentDemo.Web\
  Game\                      Pure C# rules, AI, Match, MatchSession, BoardNavigator (no UI dependencies)
  Services\                  Profile/history storage and sound (localStorage / WebAudio via JS interop)
  Components\Pages\Home.razor  Orchestrator: title -> setup -> match, panels, keyboard, AI turns
  Components\                TitleScreen, SetupScreen, MatchHud, Board, Mark, RoundPanel, SettingsPanel, HistoryPanel
  wwwroot\                   themes.css (tokens), base.css, fx.css (atmosphere), board.css
tests\AgentDemo.Tests\       xUnit tests
```

## Keyboard

Arrow keys move between cells (clamped at the edges), 1-9 play a cell, Enter/Space activate, U undo, H hint, N next round/rematch, M mute, Esc closes panels. Shortcuts are ignored while typing in the handle field.

## Undo, hint, replay, daily challenge

- **Undo / Hint** (match HUD, keys U / H): Undo takes back your last move and the AI's reply; Hint highlights the Oracle's best cell (depth-limited under DECAY, so not perfect there). Using either marks the match **assisted**: it is kept in History with an ASSISTED tag but excluded from stats, achievements and the daily challenge.
- **Replay**: finished matches store each round's moves (rounds over 60 moves are not stored). History > Replay steps through a round with Start/Back/Next/End. Stored moves are re-validated on load, and older saves without them still load.
- **Daily challenge**: a UTC-date-seeded match (rules, difficulty, best-of, side, optional "no lost round" goal; Classic never uses Oracle because it cannot be beaten). Completing it unassisted extends a streak that survives only if you finish on consecutive UTC days.
- **Achievements**: First blood, Flawless, Decay survivor, Oracle slayer, Regular, Hot streak. Each unlocks once.

## OVERCLOCK (roguelite run)

A new mode on the title screen ("OVERCLOCK · roguelite run"), inspired by the design principles of score-attack roguelites. Classic, Decay and the other modes are unchanged.

- **Encounters:** each round is a Volt quota to reach within a limited number of cycles. Act 1 has Skirmish I, Skirmish II and the **Warden** boss (middle row locked).
- **Fire:** three of your marks in a line fire for Volts and clear. Fired cells turn **Hot** (▲): marks there pay double Volts and add Gain. One placement firing two lines is a **Cross** and doubles Gain. Gain is additive and capped at x12.
- **ORBIT** is a seeded saboteur that jams one cell per cycle (✕ JAM). **Corrupt** (◈) and **Locked** (▣) cells also exist. Cells are marked by icon and word, never colour alone.
- **Shop:** after each encounter pick from 3 offers (with pity rules), reroll, or Tune a lane. 4 modifier slots, 12 Common/Uncommon modifiers, each with a drawback.
- **Runs are seeded and deterministic.** The seed plus your action list is saved after every decision, so a run survives a reload ("Resume run" on the title screen). Invalid or tampered saves are discarded.
- **Keys:** 1-9 place a mark, arrows move, Enter places the focused cell, S or Esc skips animation; in the shop 1-3 buy, R rerolls, L leaves. Hover/focus a cell to preview a fire.
- **Tests:** `RunEngineTests` (golden scoring per modifier, caps, pairwise modifiers), `RunSessionTests` (determinism, save/resume, shop pity, fuzz), `RunSimTests` (headless greedy/random bots over thousands of seeded runs; greedy clears Act 1 roughly half the time), `RunUiTests`.
- Roadmap (not built): acts 2-3, more bosses, Rare/Legendary modifiers, stakes, endless mode, daily run.

## Accessibility notes

- Board is a group of native `<button>` cells labelled like "Row 2, column 3, X"; occupied/locked cells use `aria-disabled` and stay focusable.
- One `aria-live="polite"` region announces moves and round results.
- Visible focus ring in every theme; touch targets of at least 44px; four themes including a high-contrast one.
- X (VECTOR) and O (ORBIT) differ by shape, not colour; the decay legend and win beam are not colour-only.
- `prefers-reduced-motion` and the in-app "Reduced effects" setting remove continuous animation; forced-colors mode is supported.
- Page has `lang="en"` and a descriptive title; layout works from 320px wide upward.
- Settings and History are modal: `aria-modal="true"`, the rest of the page is `inert` while open, focus returns to the opener on close.

## Known limitations

- No browser-level component tests (bUnit was intentionally not added); UI behaviour was checked manually in a browser.
- OVERCLOCK covers Act 1 only. It was played in a real headless Edge through the shop (keyboard buy and reroll), a failed-run summary and resume after reload; the Warden boss encounter and a cleared-run summary were not reached in the browser.
- Screen reader behaviour has not been tested with real assistive technology.
