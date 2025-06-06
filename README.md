> [!NOTE]  
> DO NOT RUN THIS APP AS ADMINISTRATOR - if you need to use option like Log Cleaner or TPM just click the button and youll get UAC prompt and itl launch background task to do the logic. Running the app itself as admin can cause issues with launching Riot Client and corrupt game install.  

![image](https://github.com/user-attachments/assets/b782abf7-1494-492d-843f-d1f3801a2166)

# League Patch Collection – Vanguard Disabler & QOL Optimizer for League Client

**League Patch Collection** is a lightweight C# application designed for Windows to enhance your League of Legends experience. This tool introduces several quality-of-life features and optimizations League Client experience.

## Features

This app provides the following enhancements/mods:
- :white_check_mark: **Auto Accept**: Automatically accepts queue popup by sending the accept request directly to the backend — the UI may still show the popup briefly, but you’re already accepted and will enter champion select without needing to click.
- :white_check_mark: **Dodge Button**: Exit champ select without having to close client (you still get dodge timer though).
- :white_check_mark: **Vanguard Disabler**: Access full functionality of the League Client and use Kbot without risk of being banned. Usefull if you want to safely debug the client. NOTE: this isnt a bypass, you cannot play without Vanguard yet alone even get past champ select.
- :white_check_mark: **TPM bypass**: When you get VAN9001 popup just click the button and it will supress the popup. No idea if this actually lets you into game though.
- :white_check_mark: **Vanguard Uninstaller**: One click uninstall Vanguard completly without needing to confirm "are you sure popup".
- :white_check_mark: **Disable Store**: Greys out store button and prevents popups and nags related to in-game purchases.
- :white_check_mark: **Legacy Honor**: Restores the old post game honor screen pre patch 14.19 where you can only only one teammate. Honoring enemies is cringe.
- :white_check_mark: **Log Cleaner**: Clean all client and account logs with the click of a button.
- :white_check_mark: **Name Change Bypass**: Bypasses forced name change/Riot id required screens.
- :white_check_mark: **Appear offline**: Option to mask your status to appear offline to your friends list. They cannot invite you, however, you can still invite them. You can also mask your status to show as Riot Mobile as well.
- :white_check_mark: **Hawolt ban bypass**: Proof of concept exploit that may or may not actually work, basically this will block RMS session notifications delaying the amount of time it takes for session to expire. [More info](https://web.archive.org/web/20230628125118/https://twitter.com/hawolt/status/1674029547363217410) about this exploit.
- :white_check_mark: **Removal of Bloatware**: Removes the Legends of Runeterra (LoR) button, Info Hub and suppresses some behavior warnings (ranked restrictions and so on). Also makes the patch number show in old format (ex: 15.3 instead of 25.S1 fomart). **This also disables Sanctum**.
- :white_check_mark: **Streamlined Interface**: Eliminates promotions and other unnecessary clutter from the client. 
- :white_check_mark: **Ban Reason Checker**: Find out details on why your account was banned (eg. trigger gameId) and duration of penalty.
- :white_check_mark: **Enhanced Privacy**: Disables all tracking and telemetry services, including Sentry, to reduce tracking and prevent unnecessary background activity.
- :white_check_mark: **Home Hub Fix**: Fixes home hubs taking longer than usual to load issue.
- :information_source: **[Coming Soon] Lobby Revealer**: A feature to reveal names in champ select.

##  Usage

Before running the application, ensure that you have the [.NET 8.0 SDK](https://dotnet.microsoft.com/en-us/download) installed.

You can either use the precompiled executable available in the releases section or clone this repository using Visual Studio. To manually build the project, run the following command in the terminal:
```bash   
dotnet publish "C:\path\to\league-patch-collection.csproj" -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true
```
## Pull requests needed

Pull requests are always welcome. Also, if anyone knows a library or easy way to decode Riot's rtmp (they use action message format) please contact me **c4t_bot** on Discord. I cannot find any c# libraries for decoding AMF0, AMF3 which is needed to proxy RTMP for lobby revealer. For reference here is a [good article](https://web-xbaank.vercel.app/blog/Reversing-engineering-lol) that describes how a lobby revealer works.
