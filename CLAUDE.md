# DeskVibes

The Windows sibling of VibeCraft: a tray app that lets everyday Windows users make little helpers ("vibes")
without code. A vibe is either a **rule** (when *this* happens → do *that*) or a **widget** (an info card on the
desktop that can stay on top). Built by Alfred Ducharme (Photronix). Nothing to do with WoW, apart from the optional
realm-status trigger and widget.

C# / .NET 8 WinForms, no NuGet packages (tests use a tiny runner of their own). Started 2026-10-19.

## Folder layout

| Path | What |
|---|---|
| `src\DeskVibes\Model.cs` | `Vibe`, `Step` (type + answers dict `P`), `Place` (widget position, layer top/normal/desktop, click-through, opacity, note text), `Settings`, `Ctx` ({placeholder} values, `Fill`), `Colors` |
| `src\DeskVibes\Catalog.cs` | Every trigger, action, widget and "only if" condition with its fields. **The builder forms are generated from this.** `Summary`/`Describe` make the sentences in the list |
| `src\DeskVibes\Engine.cs` | Runs rules: `Tick()` once a second polls triggers, `Push(type, ctx)` takes Windows events. Edge detection, cooldown, only-if, quiet hours, pause. `Sample()` gives fake values for the Try button |
| `src\DeskVibes\World.cs` | `IWorld` (idle, battery, CPU, memory, foreground app/title, full screen, processes, online, USB drives, disk, screen time today, minutes since a break) + `RealWorld` |
| `src\DeskVibes\Feeds.cs` | `IFeeds`: weather (Open-Meteo, geocoding included), quotes (Yahoo chart API), websites, RSS/Atom, WoW realms (Blizzard Game Data API with the user's own client id/secret). Cached, refreshed in the background, `Problem(key)` for errors |
| `src\DeskVibes\Mail.cs` | `MailHub`: read-only IMAP client (EXAMINE, UID SEARCH UNSEEN, BODY.PEEK headers; never marks read) and classic Outlook through COM (inbox + today's calendar). First check only learns what's there |
| `src\DeskVibes\Watchers.cs` | Hidden message window: global hotkeys, clipboard changes (+ history), lock/unlock |
| `src\DeskVibes\Actions.cs` | Runs actions (overlays, sounds from `C:\Windows\Media`, SAPI speech, open, copy, show desktop, mute, lock, log) |
| `src\DeskVibes\Overlays.cs` | Per-pixel-alpha click-through `LayeredForm`; screen-edge glow (all monitors; seconds = 0 → until mouse/keyboard input), flash, dim, banner, `Cards` (stacking pop-ups) |
| `src\DeskVibes\Widgets.cs` | `WidgetContent` (what each widget shows, as `WLine`s; the focus timer keeps its state here) and `WidgetForm` (drag, resize grip, right-click menu, auto height, desktop layer via WM_WINDOWPOSCHANGING) |
| `src\DeskVibes\Deck.cs` | Button deck widget `w.deck` (Stream Deck on screen). `Deck` = logic (buttons stored in the widget answers as `b{i}` / `b{i}.name`, `Layout`, `NameFor`, `Launch`, shell icons via `IShellItemImageFactory` with alpha, cached). `DeckForm` = its own window (not `WidgetForm`): a small tab that opens the grid after ~0.2 s of hover and folds 0.6 s after the mouse leaves (or stays open with "Always"); grows upward near the screen bottom; `WS_EX_NOACTIVATE` so it never steals focus; click empty / right-click to pick a program (shortcuts kept, not dereferenced), drag-and-drop works. `TrayApp.Sync` keys decks on `Deck.Shape` so assigning a button doesn't rebuild the window |
| `src\DeskVibes\Timers.cs` | Timers and alarms (`TimerItem`, saved in `Settings.Timers`). `Timers` = logic: `Timer`/`Alarm`/`NextAlarm` (Once, Every day, Weekdays, Weekends), `TakeDue` (timers removed, one-time alarms switch off, repeating ones move on; >10 min late → a "missed" card instead of ringing), `Left`, and `Quick(sentence)`, which the Describe box tries first ("set a 10 minute timer for the pizza", "wake me up at 7 on weekdays", "remind me in 20 minutes to…"). They ring even when paused or in quiet hours |
| `src\DeskVibes\UI\TimerForms.cs` | `TimerDialog` (one-click 1–60 min timers, own length, alarm time + repeat + name), `RingForm` (orange pop-up, Alarm01.wav every 4 s for up to 3 min, edges; Stop / Snooze 5 min / Again), `TimerMenus` (shared by the tray "Timers and alarms" submenu and the `w.timers` widget) |
| `src\DeskVibes\Health.cs` | PC health readings for `w.system`: a background sampler every 2 s while a widget asks (stops after 30 s unused). PDH counters for GPU engines/VRAM, disk busy/read/write and CPU speed; all fixed drives; network Mbps; ping to 1.1.1.1 every 10 s; busiest app by CPU and by memory; battery time left. No temperatures (they need admin rights or vendor drivers). `WidgetContent.HealthLines` turns a reading into lines (each section can be switched off) |
| `src\DeskVibes\Describe.cs` | "Describe it" phrase matcher (regex, not AI): sentence → draft vibe + "what I understood" notes. The trigger's matched words are blanked out before looking for actions (so "when I open spotify" isn't also an "open" action) |
| `src\DeskVibes\AiDescribe.cs` | Optional AI "Describe it": with the user's own Anthropic key (Settings, DPAPI, never logged) it sends the sentence + `CatalogJson()` to the Messages API (`claude-opus-5-5`, effort low, structured output `OutputSchema()`, `fallbacks: "default"`; retried once without fallbacks on a 400 about them). `FromResponse`/`FromJson` validate against the catalog (unknown types refused like share codes, odd values dropped). Any failure → `Describe.Parse` with a note saying why |
| `src\DeskVibes\Gallery.cs` | The Ideas tab (36 ideas) and `Starter()` (what a fresh install gets) |
| `src\DeskVibes\Store.cs` | JSON in `%APPDATA%\DeskVibes` (`vibes.json`, `settings.json`; a broken file is set aside as `.broken`), share codes (`DV1:` + base64 deflate JSON; unknown types are refused), DPAPI `Secret` |
| `src\DeskVibes\TrayApp.cs` | The app: tray menu, 1-second loop, widget sync, hotkey registration, saving, builder dialog |
| `src\DeskVibes\UI\` | `MainForm` (My vibes / Ideas / Settings / What happened), `BuilderForm` + `Fields` (catalog → inputs), `Theme` (dark colors, fonts, drawn icon, owner-drawn combos) |
| `tests\DeskVibes.Tests\Program.cs` | 300+ checks with fake world/feeds/mail. `-- live` also hits the real internet feeds |
| `tools\pack.py` | Release zip |

## Workflow

1. Change the source. Anything new in the catalog needs: fields in `Catalog.cs`, handling in `Engine.Check`/`Push`
   (triggers) or `Actions.Run` (actions) or `WidgetContent.Lines` (widgets), a `Sample()` case, a `Describe.CardText` line,
   and ideally phrases in `Describe.FindTrigger`. The catalog test checks samples and gallery ids.
2. Test: `dotnet run --project tests\DeskVibes.Tests` (add `-- live` for real weather/stocks/news/sites).
3. Look at it without disturbing the user (they often game on the main screen):
   `DeskVibes.exe --data <scratch dir> --render-ui <dir>` draws the main window pages, the builder and every widget
   (plus `ui-builder-ai.png` from a canned AI answer and `ui-settings-2/3.png` scrolled) into PNGs off-screen without taking focus (`Theme.Quiet`). `--demo --shot file.png 2.2` shows the edges, cards and
   banner for ~5 s and screenshots the whole desktop (visible to the user, so keep it rare).
   Always use `--data` with a scratch folder so the user's real vibes aren't touched; each `--data` folder is its own
   single instance.
4. Package: `python tools\pack.py <version>` (add `--full` for the self-contained build). Runs the tests first.

## Things to know

- High-DPI: `PerMonitorV2`, so screen bounds are physical pixels. The user has two monitors (a 1080p one above a
  5120x1440 main screen); the virtual screen is 5120x2520.
- Edge glow uses `UpdateLayeredWindow`; pulsing only changes the constant alpha (`SetAlpha`), never redraws.
- Widgets with click-through on can't be dragged; turn it off from the tray "Widgets" menu or the ⋯ menu in the list.
- The new Outlook (olk.exe) has no COM, so only classic Outlook works for mail/meetings; IMAP covers the rest.
- Blizzard realm namespaces: retail `dynamic-{region}`, Classic `dynamic-classic-{region}`, Era `dynamic-classic1x-{region}`;
  "Anniversary" `dynamic-classicann-{region}` is a guess, check it when a key is available.
- Closing the window keeps it in the tray; Quit is in the tray menu. Start with Windows = HKCU Run key, set only when
  the user ticks it in Settings.

## Ideas not done yet

- Windows notification listener (Teams/Discord/any app) needs package identity (MSIX); not possible unpackaged.
- Per-monitor edge choice, sound volume, an installer, auto-update.
