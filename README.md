# Autopelago

A game that's so easy, it plays itself!

Intended to integrate with [Archipelago](https://archipelago.gg) to help people practice by themselves in a realistic(ish) setting.

## Get Started

### Download + Launch
x86-64 only. There's no "installer" or anything like that, just a zipped-up single executable file. Put it somewhere and run it.

- Windows (10+): easy. just download the `win-x64` file from the latest [release](https://github.com/airbreather/Autopelago/releases) and run it.
- Linux: pretty easy. make sure [these packages](https://github.com/dotnet/core/blob/v9.0.0/release-notes/9.0/os-packages.md) are installed, download the `linux-x64` file from the latest [release](https://github.com/airbreather/Autopelago/releases), and run it.
- macOS / FreeBSD: harder and untested. install .NET 9.0 SDK, clone this repository, go to `src/Autopelago`, and `dotnet run -c Release`.
   - macOS users: apparently you can get the SDK from here https://dotnet.microsoft.com/en-us/download/dotnet/9.0
   - FreeBSD users: apparently the SDK comes from the `lang/dotnet` port. This is kind of new, see [the wiki](https://wiki.freebsd.org/.NET) for any updates.
- Anything else: probably impossible.

### First Steps

There are the usual Archipelago parameters, plus a couple more described below in the "The Game" section. For host/port, you can either fill the separate text boxes or paste the complete `host:port` format into the "Host" box.

Of course, you need to use an Archipelago server that supports this game (probably via an `apworld`) to actually get this going. Instructions for that are outside the scope of this document for now.

## The Game
![The MOST excellent picture of the MOST excellent game that plays itself.](doc/game-screen.webp)

Once connected, a rat will autonomously move across the game world, sending location checks along the way. Its own items that it receives will be more-or-less what you expect:

- It has its own "progression"-tier items that are required to unblock progression through certain gated checkpoints.
- It also has "helpful"- / "trap"-tier items that apply certain buffs / debuffs.
- Finally, there are many "filler"-tier items that do nothing when received.

For the most part, the rat just moves around the map every several seconds, trying to complete a check at each location it reaches. These attempts get easier the more "rats" that they've received, but locations further along the path will be harder, so it balances out.

### Menu Screen

![An excellent picture of an even more excellent settings screen.](doc/menu-screen.webp)

- Slot / Host / Port / Password: Standard inputs that you need for any Archipelago game. See [the official docs](https://archipelago.gg/tutorial/Archipelago/setup/en#connection-info) for details.
- Time Between Steps (sec.): Number of seconds that the rat spends between actions. Smaller numbers will send location checks faster.
- Send unprompted messages using Archipelago chat: Whether or not the rat will send messages using Archipelago chat to let you know when something changes. This setting does not affect responses to commands or hints that you click to request.
- Enable tile animations: Whether or not to animate the landmark tiles on the map background. Some clients running on slower hardware may wish to disable this, especially if they don't have a dedicated GPU.
- Enable rat animations: Whether or not to animate the rat's wiggle on the map screen. Unlike "tile animations", this only has a modest performance impact.

## Commands

The rat accepts commands telling it to focus on making specific location checks. To do this, send a chat message like `@RatName go "Before Basketball #5"`, where `RatName` is the rat's slot name and `"Before Basketball #5"` is the name of the location to check.

If you no longer want it to focus a location, you can `@RatName stop "Before Basketball #5"` to have it remove that from the queue.

`@RatName list` will show a list of locations that it's been asked to focus (up to the first 5).

You can always run `@RatName help` to get a list of all commands.

### How "Focus" Works

The rat can't reach every location on the map at all times — sometimes, it needs to make certain other location checks in order to get there. In fact, until it receives a few "rats", all it can reach are the "Before Basketball" items!

If you tell it to "focus" a location that it physically can't reach, then it won't change the rat's priorities until that location actually opens up. Don't worry, though, the rat will head that way as soon as it can.

Sometimes, though, the rat will have its own ideas about what's important, depending on what buffs (or traps) it receives. You might need to wait your turn...

## Map Window

### Buff / Debuff Reference

The game screen has some information about the rat's situation. Here's what they mean:

|Bar Label|Name|Effect|
|:-|:-|:-|
|`NOM` (> 0)|Well Fed|The next time it acts, the rat gets a little bit more done|
|`NOM` (< 0)|Upset Tummy|The next time it acts, the rat gets a little bit less done|
|`LCK` (> 0)|Lucky|The next attempt at a location check will automatically succeed|
|`LCK` (< 0)|Unlucky|The next attempt at a location check will be quite a bit harder|
|`NRG` (> 0)|Energized|The rat's next movement is free|
|`NRG` (< 0)|Sluggish|The rat's next movement costs twice as much as usual|
|`STY`|Stylish|The next attempt at a location check will be quite a bit easier|
|`DIS`|Distracted|The next time it goes to act, the rat will get absolutely nothing done|
|`STT`|Startled|The next time it goes to act, the rat will move a little bit closer to the start of the map|
|`CNF`|Confident|The next negative effect that the rat would receive is ignored|
|(none)|Smart|The rat will start moving towards a location with a "progression"-tier item|
|(none)|Conspiratorial|The rat will start moving towards a location with a "trap"-tier item|

### Items Panel

Down the left-hand side of the screen are icons that quickly show which (progression) items the rat has / has not received based on whether or not the icon is lit up.

You can click any of these icons to send a `!hint` command for it:
![An excellent picture of the map screen dimmed slightly behind a confirmation box asking, "Request a hint for Ninja Rat?", with "OK" and "Cancel" buttons.](doc/request-hint-confirm.webp)

### The Map Itself

Each landmark location has its own icon that you can mouse over to see what the rat needs in order to clear it. Some require a specific item (or two), while others just need your pack to be big enough to push through.

Locations with a gray exclamation point above them are missing requirements. Locations with a yellow exclamation mark over them can be completed once the rat makes it over to there. Locations without anything above them have already been checked and will be lit up.

You can pause the game at any time using the pause button in the bottom-left. Click it again to resume.
