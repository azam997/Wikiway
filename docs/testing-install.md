# Installing the test version

Custom repo was used to distribute a few copies to friends for testing and use. I will deprecate custom repository use if approved.

1. Open Dalamud settings: type `/xlsettings` in chat.
2. Go to the Experimental tab.
3. Under Custom Plugin Repositories, paste this URL into the empty box and press the + button:

   ```
   https://raw.githubusercontent.com/azam997/Wikiway/master/repo.json
   ```

4. Press the save icon at the bottom right.
5. Open the plugin installer with `/xlplugins`, search for Wikiway, and press Install.

Updates show up in the installer's "Available updates" like any other plugin.

## Publishing a new build (maintainer)

```powershell
.\scripts\publish-testing.ps1 -Version 1.0.0.2
```

Bump the version each time or Dalamud will not offer the update.
