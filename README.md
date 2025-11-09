<img width="600" height="400" alt="image" src="https://github.com/user-attachments/assets/a2dfa69a-c85f-4a4d-9071-99ada66db8b9" />

# League Patch Collection – Vanguard Disabler & QOL Optimizer for League Client

**League Patch Collection** is a lightweight C# application designed for Windows to enhance your League of Legends experience. This tool introduces several quality-of-life features and optimizations League Client experience.

## Features

This app provides the following enhancements/mods:
- :white_check_mark: **Auto Accept**: Automatically accepts queue popup by sending the accept request directly to the backend — the UI may still show the popup briefly, but you’re already accepted and will enter champion select without needing to click.
- :white_check_mark: **Legacy Honor**: Restores the old post game honor screen pre patch 14.19 where you can only only one teammate. Honoring enemies is cringe.
- :white_check_mark: **Log Cleaner**: Clean all client and account logs with the click of a button.
- :white_check_mark: **Dodge Button**: Exit champ select without having to close client (you still get dodge timer though).
- :white_check_mark: **Vanguard Disabler**: Access full functionality of the League Client and use Kbot without risk of being banned. Usefull if you want to safely debug the client. NOTE: this isnt a bypass, you cannot play without Vanguard yet alone even get past champ select.
- :white_check_mark: **Vanguard Uninstaller**: One click uninstall Vanguard completly without needing to confirm "are you sure popup".
- :white_check_mark: **Disable Store**: Greys out store button and prevents popups and nags related to in-game purchases.
- :white_check_mark: **Name Change Bypass**: Bypasses forced name change/Riot id required screens.
- :white_check_mark: **Appear offline**: Option to mask your status to appear offline to your friends list. They cannot invite you, however, you can still invite them. You can also mask your status to show as Riot Mobile as well.
- :white_check_mark: **Removal of Bloatware**: Removes the Legends of Runeterra (LoR) button, Info Hub and suppresses some behavior warnings (ranked restrictions and so on). Also makes the patch number show in old format (ex: 15.3 instead of 25.S1 fomart). **This also disables Sanctum**.
- :white_check_mark: **Streamlined Interface**: Eliminates promotions and other unnecessary clutter from the client. 
- :white_check_mark: **Ban Reason Checker**: Find out details on why your account was banned (eg. trigger gameId) and duration of penalty.
- :white_check_mark: **Enhanced Privacy**: Disables all tracking and telemetry services, including Sentry, to reduce tracking and prevent unnecessary background activity.
- :white_check_mark: **Home Hub Fix**: Fixes home hubs taking longer than usual to load issue.

##  Usage
If you are on older version of Windows 10, you may need to install [.NET 8.0 SDK](https://dotnet.microsoft.com/en-us/download) before running the app.

You can either use the precompiled executable available in the releases section or clone this repository using Visual Studio. To manually build the project, run the following command in the terminal:
```bash   
dotnet publish "C:\path\to\league-patch-collection.csproj" -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true
```
