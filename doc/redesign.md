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

Tricky...
