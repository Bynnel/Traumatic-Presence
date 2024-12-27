# Traumatic Presence

A client-side C# Barotrauma mod that adds Rich Presence support to various bits of the aforementioned game via [DiscordRPC](https://github.com/Lachee/discord-rpc-csharp).
May or may not also group players in the Steam friends tab via Steamworks. Steam's enhanced rich presence can't really be toyed around with as much as Discord's.

## Displays:
- Boss encounters
- Sub name
- Biome
- Gamemode
- Round missions
- Player count
- PvP Mode info (Team scores/Chosen subs if there is no score.)
- Character info such as the role icon, name of the role, cause of death, chosen faction(PvP)
- Whether you appear to be spectating or not
- Submarine editor to some extent.

Modded roles are supported but the icons have to be added in manually as discord doesn't seem to return anything if an image key is invalid. I'd really like to be wrong though. Oh and the icons have to be 512x512.

# Localization

Should be as simple as making a localization file and referencing it as Texts within filelist.xml. Some strings are fetched directly from basegame if they fit perfectly.
Some custom localization strings, if missing, fall back to a basegame equivalent if there's one that's good enough.

# Building
This project is based off of [MapleWheels/VSProjectSkeleton](https://github.com/MapleWheels/VSProjectSkeleton).
- Clone the repo, exclude **Server** projects if they're not excluded already.
- Change the deploy directories to match wherever *your* Barotrauma is.
- You'll probably have to add the Steamworks reference yourself. Just make it point to the one found in the game or something.
- Build the thingamajig. Don't forget to build Assets since it contains the Texts & necessary libraries.

The code's probably miserable but it functions.