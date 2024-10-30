# Autopelago

A game that's so easy, it plays itself!

Intended to integrate with [Archipelago](https://archipelago.gg) to help people practice by themselves in a realistic(ish) setting.

## How this works

Once connected, the "player" (represented by a rat icon) will autonomously move across the game world, sending location checks along the way. Its own items that it receives will be more-or-less what you expect:

- It has its own "progression"-tier items that are required to unblock progression through certain gated checkpoints.
- It also has "helpful"- / "trap"-tier items that apply certain buffs / debuffs.
- Finally, there are many "filler"-tier items that do nothing when received.

The player acts on a "step interval": at each interval (determined by a random roll between the min and max each time), the player will advance the game little bit more and then wait for the next interval to pass. For the most part, the player just moves around the map making attempts at each location it reaches. These attempts get easier the more "rats" that the player has received, but locations further along the path will be harder to compensate.

### Menu Screen

In addition to the inputs needed for all Archipelago playthroughs, the menu also lets you configure the min and max values for the step interval so that the player will move at whatever pace makes sense in your multiworld.

### Buff / Debuff Reference
|Bar Label|Name|Effect|
|:-|:-|:-|
|`NOM` (> 0)|Well Fed|The next time it acts, the player gets a little bit more done|
|`NOM` (< 0)|Upset Tummy|The next time it acts, the player gets a little bit less done|
|`LCK` (> 0)|Lucky|The next attempt at a location check will automatically succeed|
|`LCK` (< 0)|Unlucky|The next attempt at a location check will be quite a bit harder|
|`NRG` (> 0)|Energized|The player's next movement is free|
|`NRG` (< 0)|Sluggish|The player's next movement costs twice as much as usual|
|`STY`|Stylish|The next attempt at a location check will be quite a bit easier|
|`DIS`|Distracted|The next time it goes to act, the player will get absolutely nothing done|
|`STT`|Startled|The next time it goes to act, the player will move a little bit closer to the start of the map|
|`CNF`|Confident|The next negative effect that the player would receive is ignored|
|(none)|Smart|The player will start moving towards a location with a "progression"-tier item|
|(none)|Conspiratorial|The player will start moving towards a location with a "trap"-tier item|
