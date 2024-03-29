# Redesign

Game transitions after any of the following events:

1. Send it an item
2. Send it a request to change its priorities
3. Ask it to advance itself

There should be a "smart" way for the game to play itself. Some aspects of this could be gated behind receiving particular "smart" rats, but others might not actually need the rats to be quite so "smart"

Regardless, I must write down the rules assuming that the rats are as "smart" as they can possibly be, since the game must be able to implement all of these rules.

## When we send it an item

Classify the item:

1. Does it unlock anything, either immediately or in the future?
2. Does it grant any auras?

If either of those happened, then re-check its route to see if it should be doing anything differently.

### Reasons why auras may change things

1. If the aura is "+10 modifier until you succeed a check" and the bowling ball door is accessible (for example), then it may be a good idea to stop what it's doing and rush that check ASAP because all successful runs require completing that high-DC check.
2. If the aura is "triple movement speed for blah-blah-blah time", then it's potentially worth running to an area with easier checks (whereas the more robust calculations coming soon might have ruled this out because the travel time is too high for how much time it would actually save doing the easier location checks).
3. And of course, a theoretical aura could literally just be "your pre-Minotaur checks have +5 DC until you get past the Prawn Stars"... unlikely that this would ever show up, but the pathing logic should be robust enough to detect this.

## When we request it to change its priorities

This is a bit vague because the first version didn't have anything of the sort. Things we really "should" be able to do:

1. Ask it to push hard for a specific location check.
2. Suggest that a specific item is expected to be sent to it either:

    - Pretty soon
    - Quite a bit later
    - Effectively never

### "Push Hard"

Immediately:

1. Tell us what is blocking that location check, if anything
2. Tell us where those blocking items are, if they've already been hinted

    - I guess we should also be able to tell it to hint for things, but SEPARATELY.

3. De-prioritize paths that bring it further away from that location check

Only the last "push hard" request will be honored.

### "Specific item coming pretty soon"

Immediately, if the current best path is materially different from what the best path would be if it had that item, then it will:

1. Start treating that "best path, if only..." path as the new "best path", at least for a while.
2. If that new "best path" is a "go mode" path, then:

    - It will indeed follow "go mode" rules and start traveling to and focusing on clearing the specific blockers that it can along that path.
    - Once its path is blocked by **only** that specific item, it will focus on making checks in the closest region to the specific blocker until it either receives the item or runs out of locations in that region it can test.
    - From there, it may wander to other nearby regions to do location checks there, or... maybe it just chooses to demote the call to "quite a bit later".

I think regardless of what happens above, it needs re-confirmation a few minutes after a "pretty soon" call that it's still coming "pretty soon", since it can't play inefficiently forever just because of a single mistaken (or else overly optimistic) call.

If the "pretty soon" call expires, forget everything about "pretty soon", but retain the persistent effects that "quite a bit later" would do.

### "Specific item coming quite a bit later"

If that specific item would put it into "go mode", then immediately clear blockers like it would in the "pretty soon" case, but as soon as it runs out of those blockers that it can clear, immediately go back to the default pessimistic strategy instead of hanging out in the area.

However, it remembers that this item is coming. Receiving an item (or a combination of "specific item coming" calls) could put us into fake-"go mode" when combined with this call from a while back, so if those happen, then we can prod the player who gave us the original call to let them know that we're in "go mode" as soon as they send us that item.

### "Specific item is basically never coming"

**Probably** doesn't affect us too much. I feel like all that it realistically should do is counteract any optimism that we might get from the other calls.

## When we ask it to advance itself

This is the meat of the code, and the overwhelming majority of what I've written so far in the pilot implements a bit of a cringe version of it. It's time to think it through for real. I think I want to use something resembling the PF2e Encounter Mode rules for this.

1. Figure out what our path should be. This is considered a free action.
2. If we're not on the correct path right now, then spend 1 action point to switch.
3. If we're currently at a non-"filler" location check, then spend 2 action points, simulating some sort of dynamic encounter-specific thing you'd need to do.
4. Spend remaining action points on movement / attempts at the location check.

### "Mercy rule" / On the concept of "difficulty"

The pilot implements what I'll call a "mercy rule", where every consecutive failure increases the next check's modifier by +1. The intent of that was to make sure that we can simulate location checks that get progressively more "difficult" to collect as the player progresses through the game by cranking up the DC, without creating a situation where each playthrough can **only** succeed by hitting a lucky streak.

Playtesting shows that this strategy is probably serving its intended purpose, as we never have to wait **too** long in BK mode, but when you examine it for more than a few minutes, you see that it does a very poor job at simulating how a realistic player will experience their game.

---

**In general**, within a single run, a player won't **strictly** get **significantly**, **progressively** better at solving any given challenge that they face, **just** by virtue of trying. There's **some shadow** of this **overall** concept, but by the time you accept a player into your multi-world, there's an expectation that they're going to be competent enough at their chosen game that they won't have a 5% chance of beating the final boss on their first try, and then a 10% chance on their second try, and then a 15% chance on their third try, and so on.

---

This strategy **also** has the fundamental flaw of making every playthrough feel basically the same. The **only** thing that can govern which path you take is just whether or not you **happen** to have the single key item that unlocks the next specific blocker that stands in the way of that path.

But consider my usual OoT sphere 0 run. Right away, the path can diverge significantly based on whether or not I receive a Deku Shield before exhausting the accessible Kokiri Forest and Lost Woods checks, even though there are plenty of very easy sphere 0 checks in the Deku Tree. This is because the Deku Tree region is so close to the menu — and there are so many ways to get a Deku Shield, which is the only item that my logic requires me to have for a full clear — that it's a definite waste of time to go in there without one.

The pilot version **completely** misses this element.

---

Finally, while I can't say that I know how this **usually** plays out in Archipelago multi-worlds (I haven't seen anyone else play this for real outside my friend group), I feel like it's not a stretch to guess that with most players in most of their chosen games, the game doesn't get **monotonically** more difficult as the player goes to "later" regions just because the numbers are bigger. Personally, given that I have all the requirements to complete either one, I find the Spirit Temple not tremendously harder than the Fire Temple — especially if I've gotten Biggoron's Sword or a Giant's Knife for crouch-stab abuse.

What sets the "later" Spirit Temple apart from the "earlier" Fire Temple is:

- Its complexity, in terms of:

  - how many items are required to enter it at all
  - split between child / adult segments
  - how many items are required to **fully** clear **all** location checks while inside

- Its "difficulty", in terms of:

  - how many hazards there are that might cause random damage to an unprepared / careless player
  - the amount of damage that the various enemies can deal, especially the final / sub-bosses
  - the absolute size of the thing

So if we assume that I have received **all** the items to complete both the Fire Temple **and** the Spirit Temple to the fullest extent that the seed allows me to — all else equal! — then I will feel more inclined to hit the Spirit Temple first, because:

- It has more locations that I can check
- I haven't counted, but it feels like there are far more location checks that I can do in the Spirit Temple without randomly getting blocked because the seed didn't happen to put enough small keys early enough.
- I'm a skilled enough player that even though this dungeon throws at least one enemy with an Adult Link-sized health bar and damage output at a Child Link-sized toolkit (sometimes two such enemies) — which is supposed to be a major part of this dungeon's "difficulty" — this doesn't really slow me down substantially.

And here's the kicker: on top of all this, keeping those same assumptions (i.e., barring any external information or hints that would justify prioritizing one of the two), if I happened to have very few heart containers and no double defense, then I wouldn't prioritize **either** of these dungeons, because I tend to be careless enough that **both** will probably usually hit me for at least 3 hearts worth of damage between the guaranteed recovery hearts, and so I should prioritize other regions (probably mostly Overworld) with similar location densities but lower or sparser damage output... but the overall difficulty strategy I implemented for the pilot will pretty much always say that I should go for the Fire Temple before the Adult Link-only checks in Lake Hylia, because the latter ones have the smallest travel time to the blocker that comes "next" in the original game.

### How better to implement "difficulty"

Some observations:

1. Some OoT players [can](https://www.zeldaspeedruns.com/oot/tech/superslide) increase their travel time dramatically once they have a bomb bag.
2. Getting a Hylian Shield and either Giant's Knife or Biggoron's Sword will allow me to significantly increase my damage output against enemies where it's worthwhile.
3. Some "later" enemies do enough damage to one- or two-shot Link if he has gotten no heart container or defense upgrades, but even on the very unusual seeds where they show up much earlier than usual, I've always had plenty of buffer to fight them fearlessly.

    - And even if I were faced with a Stalfos right outside Link's house at the start of the game, I could still probably defeat it most of the time if I just play it safe.

4. Despite the fact that not all playthroughs strictly require it, at least one bottle is a pseudo-requirement for me because of how much extra damage it lets me do to Ganondorf.

From those observations and considering the flaws enumerated above, an appropriate response starts to take form:

1. Regardless of anything else, **in general**, all built-in increases in the difficulty of "later" stages should be substantially counterbalanced by items that the player is "expected" to have acquired in the "earlier" stages.
2. A "key" item that is required to clear a certain blocker could also sensibly make some other unrelated location checks easier.
3. Rather than only incrementing the DC — which increases both the mean and the standard deviation without increasing the min or max — a "more difficult" check could primarily require additional successes.

    - Nothing fundamentally conflicts with the notion of having the final boss require 100 successes at DC 15.
    - For any given check, if it feels too "easy" by requiring `x` successes at DC `y`, but too "hard" when it requires `x + 1` successes at DC `y` / `x` successes at DC `y + 1`, then we can always multiply the number of required successes on everything by a factor of `n` and the interval between steps by a factor of `1 / n` to expand our options without meaningfully affecting all the other balancing up to that point.

        - I should mention here that I'm pretty sure that combining the effects of `n` and `1 / n` will probably cause a net effect to the mean time to complete any given location check (especially if its DC is relatively high), but I feel like the magnitude of the net effect is probably generally low enough that I can treat it as nearly zero until I feel good enough with the overall numbers to take the game's layout to a final balancing phase where I can use a Monte Carlo method to get feedback and compensate for anything that gets too far out-of-whack.

4. It's still fun to sometimes throw a player into a situation where they have to do something a little too difficult, a little too early for how the game is "normally" played.

    - e.g., I can't **always** single-cycle Gohma without the Slingshot, but I can **usually** do it, and it's always two-cycle when I fail, and that's good enough for me to completely ignore the Slingshot.
    - e.g., sometimes the Iron Knuckle on the Child Link side of the Spirit Temple really does need to be done as Child Link (or as Adult Link without Biggoron's Sword or Giant's Knife), in which case the fight becomes nontrivial because it has enough time to make an initial swing against me (though I'm sure if I cared enough, I could find a setup to turn it from "nontrivial but easy" into "trivial" under these circumstances as well).

## Code Layout

With all that written down, let's do some actual object modeling...

### Game Definitions

Static immutable singleton record type with:

- List of APMW regions, each with

  - List of APMW locations it contains, each with what is required to complete its check.

- List of connections between APMW regions, each with the relative time to traverse
- List of items, each with

  - List of aura(s) it grants
  - Associated APMW game, if any

### Game State

Immutable record type with:

- Epoch

  - Starts at 0
  - Each time we create a new instance based on an existing one, the new instance gets an Epoch that's 1 greater than the previous one's
  - This **could** be implemented by having a list of all previous game states so far. That is excessively chunky for storing it in Archipelago, but doing it locally could enable a "save replay" feature

- Received items
- Checked locations
- Known hints
- Current GPS location
- Target APMW location, if any
- Successes so far against target APMW location
- Demands such as "push hard for APMW location X"
- History of calls such as "item X is coming pretty soon"
- History of aura-related events

  - This could **possibly** be inferred exactly from the history of received items, but I think I want to allow auras to be complex enough that there's no reasonable way to avoid storing some of this history separately.
  - Plus, if "save replay" ever happens, then on playback, it might be cool to show a scene of a buffed-up rat tearing through the sewers.

- PRNG state

Not listed are any indexes that are fully derived from the above data members which will allow querying and transitioning the state more efficiently.

There is a baseline state that depends only on a PRNG seed and nothing else (not even the details of Game Definitions):

- Epoch: 0
- Received items: none
- Checked locations: none
- Known hints: none
- Current GPS location: "menu" / "start", however you want to word it
- Target APMW location: none
- Successes so far against target APMW location: none
- Demands such as "push hard for APMW location X": none
- History of calls such as "item X is coming pretty soon": never received any such calls
- History of aura-related events from items: none
- PRNG state: derived from the seed

### Player

Class, responsible for taking a Game State and transitioning it to the next Game State by following the rules from Game Definitions. All (or **very nearly** all) of its important state should be read-only and immutable.

Two different Player instances could sensibly have different opinions about the best way to transition a Game State, but it's the combination of Game State and Game Definitions that determines what the **available** choices are. All **available** choices, by definition, will be completable by any Player instance, regardless of its own idiosyncrasies.

Overall things it can do (probably not exhaustive):

- Change the Target APMW location (if we're not currently targeting the best one)

  - "the best one" is something that Player also must determine, based on the rest of the state and the Game Definitions

- Change the current GPS location (if we're not already at the Target APMW location)
- Change the number of successes so far against the target APMW location (if we're there and we succeed)
- Update the history of aura-related events (if there's an active aura that influences or is influenced by something else that we've done)
- Transition the PRNG state to the next one (if we used it at all)

I believe that there should **always** be at least one thing to do in each transition. There are two things that look like exceptions to that rule:

1. You'd think that we shouldn't ever transition anything if the goal is already completed. I agree, and that's why I think that Player should enforce a pre-condition that we not ask it to transition a completed state.
2. It also appears that we shouldn't ever transition anything in BK mode. I agree with that too, and that's why I think Player should also enforce a pre-condition that the state is not in BK mode.

### Game

Class, responsible for taking a Game State through all the stages of a dynamic game.

Its responsibilities include those of actually "playing" the game — which it definitely should delegate to a Player that it also manages — but it also has other sub-responsibilities, which are probably not worth delegating further:

- Initialize the Game State at startup time, potentially resuming from an earlier run of the process
- Directly transition the Game State when the multiworld server tells us about things that should influence it

  - A [`ReceivedItems`](https://github.com/ArchipelagoMW/Archipelago/blob/0.4.4/docs/network%20protocol.md#ReceivedItems) packet is the main one
  - [`PrintJSON`](https://github.com/ArchipelagoMW/Archipelago/blob/0.4.4/docs/network%20protocol.md#PrintJSON) can do it too, though, since that's how someone will make a call or a demand

- Compare the pre- and post-transition Game States and tell the multiworld server about any important things that have changed
- Store the post-transition Game State on the multiworld server so that we can resume later without relying on client-local storage
- Ensure that we only make requests to the Player at appropriate time-based intervals

  - Stretch goal: if we receive an item with an aura that influences the interval duration while we are already waiting for an interval to elapse (which is how it's usually going to be), then it would be quite nice to have it apply immediately instead of waiting for that interval to lapse. This makes balancing tricky, but I can say that it feels quite weird that buffs and traps can take such a long time to "kick in".

### Program

The usual, responsible for running one or more `Game` instances and connecting them to whatever multiworld server implementation is in use.
