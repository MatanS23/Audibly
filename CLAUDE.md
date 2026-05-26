# Audibly Fork — Project Context for Claude Code

## What this project is
A personal fork of [Audibly](https://github.com/rstewa/Audibly), an open-source WinUI 3 audiobook player for Windows.
The goal is to add personal-taste features on top of the original app while keeping the fork mergeable with upstream updates.

**Fork repo:** `D:\Matan\Coding\AudiblyFork\Audibly`
**Original upstream:** `https://github.com/rstewa/Audibly`
**Upstream remote name:** `upstream`

---

## Solution structure

```
Audibly/
├── Audibly.App/                  # Main WinUI 3 app (startup project)
│   ├── Views/
│   │   └── LibraryCardPage.xaml(.cs)   # Main library page — most of our changes live here
│   ├── UserControls/
│   │   ├── AudiobookTile.xaml(.cs)     # Grid view tile (original, unchanged)
│   │   ├── AudiobookListItem.xaml(.cs) # List view row (NEW — added by us)
│   │   ├── PlayerControlGrid.xaml(.cs) # Player bar host control
│   │   └── NowPlayingBar.xaml(.cs)     # Progress slider + time displays
│   ├── ViewModels/
│   │   ├── MainViewModel.cs            # App-wide VM — sort, view toggle, audiobook list
│   │   └── PlayerViewModel.cs          # Player state — playback, chapter, volume, speed
│   └── Helpers/
│       └── UserSettings.cs             # LocalSettings persistence — view mode, sort, etc.
├── Audibly.Models/
│   └── Audiobook.cs                    # Core model — Author, Title, Composer, etc.
├── Audibly.Repository/
│   └── Sql/
│       ├── AudiblyContext.cs           # EF Core DbContext (SQLite)
│       ├── AudiblyContextFactory.cs    # Design-time factory
│       └── SqlAudiobookRepository.cs  # CRUD operations
└── NuGet.config                        # Added by us — includes CommunityToolkit-Labs feed
```

**Target framework:** .NET 8, WinUI 3 (Windows App SDK)
**Build:** Must be built via Visual Studio 2022, configuration `Debug x64`.
`dotnet build` cannot build `Audibly.App` (WinUI 3 limitation).
For EF migrations always use `--no-build` flag after building in VS.

---

## NuGet setup
A `NuGet.config` was added to the repo root to include the CommunityToolkit Labs feed:
```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="CommunityToolkit-Labs" value="https://pkgs.dev.azure.com/dotnet/CommunityToolkit/_packaging/CommunityToolkit-Labs/nuget/v3/index.json" />
  </packageSources>
</configuration>
```

---

## Database notes
- **App database path:** `C:\Users\matan\AppData\Local\Packages\38488StewartRyan.24898061B3F0E_8hz582d7yec5r\LocalState\Audibly.db` (SQLite)
- The DB was originally created with `EnsureCreated()` rather than EF migrations, so `__EFMigrationsHistory` did not exist initially.
- We decided **not** to add a `Series` field to the model/DB due to migration complexity. Author is used instead for grouping/sorting/filtering.
- The development database and the regular-use database are the **same file**. Do not attempt to recreate or reset the DB — it will wipe the user's library.
- EF migration commands (run from `Audibly.Repository/`):
  ```
  dotnet ef migrations add <Name> --startup-project ../Audibly.App --no-build
  dotnet ef database update --startup-project ../Audibly.App --no-build
  ```

---

## Features implemented so far

### ✅ Step 2 — Grid view: switched ItemsRepeater → GridView
**File:** `Audibly.App/Views/LibraryCardPage.xaml`

Replaced the `ScrollViewer` + `ItemsRepeater` + `UniformGridLayout` block with a `GridView` using `ItemsWrapGrid`.
- Kept `x:Name="LibraryCardScrollView"` so the `VisualStateManager` still works.
- `SelectionMode="None"`, `IsItemClickEnabled="False"` — tile handles its own clicks.
- Bottom padding `0,8,0,208` replaces the old invisible spacer Rectangle.

### ✅ Step 3 — List/Grid view toggle
**Files:** `MainViewModel.cs`, `UserSettings.cs`, `LibraryCardPage.xaml(.cs)`, new `AudiobookListItem.xaml(.cs)`

**UserSettings.cs** — added `IsGridView` (bool, default `true`), persisted to `LocalSettings["IsGridView"]`.

**MainViewModel.cs** — added:
```csharp
private bool _isGridView = UserSettings.IsGridView;
public bool IsGridView { get => _isGridView; set { ... UserSettings.IsGridView = value; OnPropertyChanged(nameof(IsListView)); } }
public bool IsListView => !_isGridView;
```

**LibraryCardPage.xaml** — added:
- `AudiobookListTemplate` DataTemplate using `AudiobookListItem` user control.
- `AppBarToggleButton` (glyph `&#xE8FD;`) bound to `ViewModel.IsListView`.
- `ListView` named `LibraryListView` bound to `ViewModel.IsListView` for visibility.
- `GridView` visibility bound to `ViewModel.IsGridView`.
- Zoom `AppBarButton` visibility bound to `ViewModel.IsGridView` (hidden in list view).
- Both views added to the `VisualStateManager` `NoAudiobooksState` setters.

**AudiobookListItem** (new UserControl) — mirrors all AudiobookTile interactions:
- Play overlay on hover (over thumbnail).
- Right-click context menu (Play, Delete, Show in File Explorer, Open in App Folder, More Info, Mark as Completed/Incomplete, Export Metadata).
- `IsCompleted` callback toggles Mark as Completed / Mark as Incomplete menu items.
- `ProgressBar` with `Maximum="100"` and a percentage `TextBlock` using `FormatProgress(Progress)` function binding.
- Dependency properties: `Id`, `Title`, `Author`, `Source`, `Progress`, `IsCompleted`, `SourcePathsCount`, `SourcePaths`, `FilePath`.

### ✅ Step 4 — Sorting (Title A→Z, Title Z→A, Author A→Z, Author Z→A)
**Files:** `MainViewModel.cs`, `UserSettings.cs`, `LibraryCardPage.xaml(.cs)`

**UserSettings.cs** — added `SortOption` (string, default `"TitleAsc"`), persisted to `LocalSettings["SortOption"]`.

**MainViewModel.cs** — added:
- `SortOption` enum: `TitleAsc`, `TitleDesc`, `AuthorAsc`, `AuthorDesc`.
- `CurrentSort` property that calls `ApplySortToAudiobooks()` on set and persists to `UserSettings`.
- `ApplySortToAudiobooks()` — sorts `Audiobooks` in-place on the dispatcher queue.
- Sort is also applied inside `GetAudiobookListAsync()` after populating the list.

**LibraryCardPage.xaml** — added Sort `AppBarButton` (glyph `&#xE8CB;`) with a `MenuFlyout` containing four `ToggleMenuFlyoutItem` entries.

**LibraryCardPage.xaml.cs** — added:
- `UpdateSortCheckmarks()` — syncs checked state of menu items to `ViewModel.CurrentSort`.
- Four click handlers (`SortTitleAsc_OnClick`, etc.) that set `ViewModel.CurrentSort` and call `UpdateSortCheckmarks()`.
- `UpdateSortCheckmarks()` called at end of `LibraryCardPage_Loaded`.

### ✅ Bug fix — Volume and playback speed not applied when switching audiobooks
**File:** `PlayerViewModel.cs`, method `OpenAudiobook`

**Root cause:** The `Audiobook` model's `PlaybackSpeed` and `Volume` properties default to `0.0` in C# for books imported before these values were ever explicitly saved. The `else` branch was calling `UpdatePlaybackSpeed(0)` and `UpdateVolume(0)`, silencing audio and freezing playback even though the UI sliders showed correct values.

**Fix:** Added a `> 0` guard that falls back to `UserSettings` defaults when the stored value is uninitialized:
```csharp
else
{
    UpdatePlaybackSpeed(NowPlaying.PlaybackSpeed > 0
        ? NowPlaying.PlaybackSpeed
        : UserSettings.PlaybackSpeed);
    UpdateVolume(NowPlaying.Volume > 0
        ? NowPlaying.Volume
        : UserSettings.Volume);
}
```

---

## Features remaining to implement

### ✅ Step 5 — Multi-select delete
Allow selecting multiple audiobooks in the library and deleting them all at once.

**Files:** `MainViewModel.cs`, `LibraryCardPage.xaml(.cs)`, `AudiobookTile.xaml`, `AudiobookListItem.xaml`

**MainViewModel.cs** — added:
- `IsSelectMode` / `IsNotSelectMode` / `HasSelectedAudiobooks` properties.
- `SelectedAudiobooks` (`HashSet<Guid>`) to track selection.
- `UpdateSelectedAudiobooks(added, removed)` — called from `SelectionChanged` handlers.
- `DeleteSelectedAudiobooksAsync()` — shows a confirmation dialog, deletes all selected books, exits select mode, and refreshes the list.

**LibraryCardPage.xaml** — added:
- `AppBarToggleButton` (glyph `&#xE8B3;`) bound to `ViewModel.IsSelectMode`.
- `AppBarButton` "Delete Selected" visible when `IsSelectMode`, enabled when `HasSelectedAudiobooks`.
- `SelectionChanged` attribute on both `GridView` and `ListView`.

**LibraryCardPage.xaml.cs** — added:
- Subscribed to `ViewModel.PropertyChanged` to reset `SelectionMode` on both views when `IsSelectMode` becomes false (e.g. after delete completes).
- `SelectModeToggle_OnClick` — toggles `IsSelectMode` and sets `SelectionMode.Multiple/None` on both views.
- `LibraryCardScrollView_SelectionChanged` / `LibraryListView_SelectionChanged` — delegate to `ViewModel.UpdateSelectedAudiobooks`.
- `DeleteSelectedButton_OnClick` — calls `ViewModel.DeleteSelectedAudiobooksAsync()`.

**AudiobookTile.xaml / AudiobookListItem.xaml** — added `IsHitTestVisible="{x:Bind ViewModel.IsNotSelectMode, Mode=OneWay}"` to `ButtonTile` so pointer events pass through to the `GridViewItem`/`ListViewItem` container for selection when in select mode.

### ✅ Bug fix — Context menu opens at click position
The right-click context menu was always anchored to the right edge of the entry tile/row (`Placement="RightEdgeAlignedTop"` on the element), which looked especially poor in list view.

**Files:** `AudiobookTile.xaml.cs`, `AudiobookListItem.xaml.cs`

**Fix:** In both `ButtonTile_OnRightTapped` handlers, added `Position = e.GetPosition(ButtonTile)` to the `FlyoutShowOptions`. This overrides the element-anchored placement and opens the menu at the exact pointer position instead.

---

### 🔲 Step 6 — Author sidebar
Dynamically list all authors in the left navigation pane under "Library". Clicking an author filters the main library to show only their books.

**Plan:**
- Add `ObservableCollection<string> Authors` to `MainViewModel`, populated from distinct non-empty `Author` values in `AudiobooksForFilter`. Refresh whenever `GetAudiobookListAsync` runs.
- Add `string? ActiveAuthorFilter` property to `MainViewModel`. When set, filters `Audiobooks` to only that author. When null, shows all.
- In `AppShell.xaml`, add author items under `LibraryCardMenuItem` in the `NavigationView.MenuItems` using an `ItemsRepeater` or by adding them as child `NavigationViewItem`s dynamically from code-behind.
- In `AppShell.xaml.cs`, handle `NavigationView_ItemInvoked` to detect author item clicks and set `ViewModel.ActiveAuthorFilter`.
- Filter must compose with existing progress filters and search — authors sidebar is an additional layer on top.

### 🔲 Step 7 — Metadata editing (Title and Author)
Allow the user to edit the `Title` and `Author` fields of an existing audiobook entry directly in the app, so books can be renamed for correct sorting without touching the database manually.

**Plan:**
- The existing "More Info" dialog (`MoreInfo_OnClick` in `AudiobookTile.xaml.cs` and `AudiobookListItem.xaml.cs`) is the natural place to add an edit mode. Find its content dialog in `Audibly.App/Views/ContentDialogs/`.
- Add editable `TextBox` fields for `Title` and `Author` to the More Info dialog, replacing or toggling with the current read-only display.
- On confirm, update `AudiobookViewModel.Model.Title` / `.Author`, set `IsModified = true`, and call `SaveAsync()` which internally calls `UpsertAsync(Model)` on the repository.
- After saving, call `GetAudiobookListAsync()` on `MainViewModel` to refresh the library and re-apply the current sort.
- No DB schema changes needed — `Title` and `Author` already exist on the `Audiobook` model.
- Be aware: `UpsertAsync` uses `Title + Author` as the uniqueness key — there is a known bug comment (`// TODO: fix this bug`) around duplicate detection that may need attention when both fields change simultaneously.

### 🔲 Step 8 — Chapter time display toggle (elapsed ↔ remaining)
The right-side time label at the end of the player progress bar currently always shows total chapter duration. Make it clickable to toggle between total duration and time remaining in the current chapter.

**Plan:**
- **`PlayerViewModel.cs`** — add:
  - `bool ShowChapterTimeRemaining` property.
  - `string ChapterRemainingText` computed property: `((long)(_chapterDurationMs - _chapterPositionMs)).ToStr_ms()`.
  - In the `ChapterPositionMs` setter, add `OnPropertyChanged(nameof(ChapterRemainingText))` so it updates live.
- **`NowPlayingBar.xaml`** — replace the right-side `TextBlock` (currently `RemainingTimeTextBlock`, bound to `ChapterDurationText`) with a `Button` styled as a label (transparent background, no border). Its inner `TextBlock` uses a conditional x:Bind:
  ```xml
  Text="{x:Bind PlayerViewModel.ShowChapterTimeRemaining ? PlayerViewModel.ChapterRemainingText : PlayerViewModel.ChapterDurationText, Mode=OneWay}"
  ```
- **`NowPlayingBar.xaml.cs`** — add click handler that toggles `PlayerViewModel.ShowChapterTimeRemaining`.

### 🔲 Step 9 — Book-level total progress and speed-adjusted remaining time
Add a second row below the existing chapter progress bar showing total elapsed time and total remaining time for the whole audiobook. Only the remaining time is adjusted by playback speed.

**Plan:**
- **`PlayerViewModel.cs`** — add:
  - `string BookElapsedText` and `string BookRemainingText` properties.
  - Private helper `UpdateBookTimeDisplays(double elapsedSeconds)` that calculates:
    - `BookElapsedText` from elapsed seconds converted to ms via `.ToStr_ms()`.
    - `BookRemainingText` from `(NowPlaying.Duration - elapsedSeconds) / PlaybackSpeed` — only remaining is speed-adjusted.
  - Call `UpdateBookTimeDisplays(tmp)` at the end of the position block in `PlaybackSession_PositionChanged`, where `tmp` already holds total elapsed seconds across all source files.
  - Also call it inside `UpdatePlaybackSpeed()` so remaining time recalculates immediately when speed changes.
- **`NowPlayingBar.xaml`** — wrap the existing single-row Grid in a two-row parent Grid. Second row: three-column layout with `BookElapsedText` (left), static "Book Progress" label (center), `BookRemainingText` (right). Use smaller font size and lower opacity than the chapter row to visually distinguish the two levels.
- No new `UserSettings` needed — these are ephemeral display values, not persisted.

---

## Key patterns in this codebase

- **MVVM:** `BindableBase` provides `Set()` and `OnPropertyChanged()`. ViewModels use `ObservableCollection` for UI-bound lists.
- **Dispatcher:** All UI collection mutations go through `_dispatcherQueue.EnqueueAsync()` or `TryEnqueue()`.
- **UserSettings:** All persisted settings use `ApplicationData.Current.LocalSettings.Values["Key"]` with try/catch + Sentry logging. Follow the same pattern for any new settings.
- **x:Bind:** The app uses compiled bindings throughout. Function bindings like `{x:Bind FormatProgress(Progress), Mode=OneWay}` require the method to be public on the code-behind class. Conditional x:Bind (`condition ? a : b`) is supported.
- **DataTemplates:** `AudiobookTemplate` (grid) and `AudiobookListTemplate` (list) are defined in `LibraryCardPage.xaml` `Page.Resources`.
- **AudiobookTile interactions:** Play button appears on hover via `BlackOverlayGrid`/`PlayOverlayGrid` visibility toggle. Right-click shows `MenuFlyout`. All actions look up the audiobook by `Id` from `ViewModel.Audiobooks`.
- **No Series field:** Decision was made to use `Author` for grouping instead of adding a `Series` DB column, to avoid EF migration complexity.
- **Time formatting:** Use the `.ToStr_ms()` extension method (on `long`) for `mm:ss` or `h:mm:ss` display. Available via `Audibly.App.Extensions`.
- **Save pattern:** `audiobook.IsModified = true` then `await audiobook.SaveAsync()` which internally calls `App.Repository.Audiobooks.UpsertAsync(Model)`.

---

## Coding style conventions (follow these)
- File headers: `// Author: ... · https://github.com/rstewa` then `// Updated: MM/DD/YYYY`
- Regions used for grouping: `#region`, `#endregion`
- Async void only for event handlers; async Task for everything else
- Null checks via `if (x == null) return;` pattern
- `?.` and `??` used liberally for null safety
