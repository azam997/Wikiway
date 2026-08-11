# After an FFXIV patch or Dalamud update

Run through this when the game patches, when Dalamud bumps its API level, or
when a canary run fails.

1. **Build.** `dotnet build`. If it fails right after a Dalamud update, the API
   level probably bumped: check the latest `Dalamud.NET.Sdk` on nuget.org,
   update the pin in `src\Wikiway.Plugin\Wikiway.Plugin.csproj`, rebuild, and
   fix whatever the compiler reports. That build break *is* the canary for
   Dalamud API changes.
2. **Align Lumina pins.** Look at the file versions of `Lumina.dll` and
   `Lumina.Excel.dll` in `%AppData%\XIVLauncher\addon\Hooks\dev\` and make the
   NuGet pins match in `src\Wikiway.GameData\Wikiway.GameData.csproj` and
   `tests\Wikiway.Canary.Tests\Wikiway.Canary.Tests.csproj`. A drift here can
   surface later as a MissingMethodException in-game, so do it even when the
   build is green.
3. **Unit tests.** `scripts\run-tests.ps1` - should always pass; failures here
   are our bug, not the patch's.
4. **Canaries.** `scripts\run-canaries.ps1`:
   - **GameData failures** mean a sheet or join changed (column renamed,
     renumbered, or a schema update in Lumina.Excel). Fix
     `src\Wikiway.GameData\LuminaGameDataStore.cs` to match the new schema.
   - **WikiLive failures** mean the wiki changed its API or response shape.
     Fix `src\Wikiway.Core\Wiki\ConsoleGamesWikiClient.cs` and update the
     canned fixtures in `tests\Wikiway.Core.Tests\WikiClientTests.cs` to the
     new shape.
5. **In-game smoke.** Load the dev plugin and try:
   - `/wikiway momodi` - NPC card, map flag opens the Ul'dah map
   - `/wikiway the ultimate weapon` - quest card with prerequisites
   - `/wikiway aether currents` - wiki results with a lead paragraph
