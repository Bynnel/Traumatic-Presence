# Traumatic Presence

A client-side C# Barotrauma mod that adds Rich Presence support to various bits of the aforementioned game via [DiscordRPC](https://github.com/Lachee/discord-rpc-csharp). May or may not also group players in the Steam friends tab via Steamworks. I don't have enough people to test that right now.

## Displays:
- Boss encounters
- Sub name
- Biome
- Gamemode
- Missions
- Player count
- PvP Score/Chosen submarines
- Character info such as the role icon, name of the role, cause of death, chosen faction(PvP)
- Whether you're spectating or not

Modded roles are supported but the icons have to be added in manually as discord doesn't seem to return anything if an image key is invalid. I'd really like to be wrong though. Oh and the icons have to be 512x512.

# Building
This is based off of MapleWheels/VSProjectSkeleton template.
- Clone the repo, exclude **Server** projects if they're not excluded already.
- Change deploy directories to match wherever *your* barotrauma is.
- Build the thingamajig. Don't forget to build Assets since it contains the Texts & necessary libraries.

The code's probably miserable but it functions.
