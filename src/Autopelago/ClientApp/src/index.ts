import * as signalR from '@microsoft/signalr';

interface GameState {
    epoch: number;
    rat_count: number;
    current_region_first_location: string;
    cleared_landmarks: string[];
    open_region_first_locations: string[];
    completed_goal: boolean;
    inventory: { [item: string]: number };
}

const loadingOverlay = <HTMLDivElement>document.getElementById('loading-overlay');
const ratCountSpan = <HTMLSpanElement>document.getElementById('rat-count');
const receivedItemElements = new Map<string, HTMLElement>();
for (const receivedItemElement of document.getElementsByClassName('received-item')) {
    if (receivedItemElement instanceof HTMLElement) {
        receivedItemElements.set(<string>receivedItemElement.dataset.exactItemName, receivedItemElement);
    }
}

const landmarkRegionElements = new Map<string, SVGElement>();
for (const landmarkRegionElement of document.getElementsByClassName('landmark')) {
    if (landmarkRegionElement instanceof SVGElement) {
        landmarkRegionElements.set(<string>landmarkRegionElement.dataset.exactLocationName, landmarkRegionElement);
    }
}

const fillerRegionElements = new Map<string, SVGElement>();
for (const fillerRegionElement of document.getElementsByClassName('filler-region')) {
    if (fillerRegionElement instanceof SVGElement) {
        fillerRegionElements.set(<string>fillerRegionElement.dataset.firstLocationName, fillerRegionElement);
    }
}

const connection = new signalR.HubConnectionBuilder()
    .withUrl('/gameStateHub')
    .withServerTimeout(600000)
    .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: ctx => {
            // default is to retry after 0, 2, 10, then 30 seconds, then never again.
            // howzabout let's do it 0, 2, 4, then 5 forever... this should be local-only anyway.
            return Math.min(5000, ctx.previousRetryCount * 2000);
        },
    })
    .build();
const slotDropdown = (<HTMLSelectElement>document.getElementById('slot-dropdown'));
let subscription: signalR.ISubscription<GameState> | null = null;

const update = (state: GameState) => {
    try {
        ratCountSpan.textContent = `${state.rat_count}`;
        const receivedItems = new Set<string>(Object.keys(state.inventory));
        for (const [itemName, itemElement] of receivedItemElements.entries()) {
            if (receivedItems.has(itemName)) {
                itemElement.classList.remove('not-found');
            } else {
                itemElement.classList.add('not-found');
            }
        }

        const openRegionFirstLocations = new Set<string>(state.open_region_first_locations);
        const clearedLandmarks = new Set<string>(state.cleared_landmarks);
        for (const [exactLocationName, landmarkRegionElement] of landmarkRegionElements.entries()) {
            if (state.current_region_first_location == exactLocationName) {
                landmarkRegionElement.classList.add('current-region');
            } else {
                landmarkRegionElement.classList.remove('current-region');
            }

            if (openRegionFirstLocations.has(exactLocationName)) {
                landmarkRegionElement.classList.remove('not-open');
                if (clearedLandmarks.has(exactLocationName)) {
                    landmarkRegionElement.classList.remove('not-cleared');
                } else {
                    landmarkRegionElement.classList.add('not-cleared');
                }
            } else {
                landmarkRegionElement.classList.add('not-open', 'not-cleared');
            }
        }

        for (const [firstLocationName, fillerRegionElement] of fillerRegionElements.entries()) {
            if (state.current_region_first_location == firstLocationName) {
                fillerRegionElement.classList.add('current-region');
            } else {
                fillerRegionElement.classList.remove('current-region');
            }

            if (openRegionFirstLocations.has(firstLocationName)) {
                fillerRegionElement.classList.remove('not-open');
            } else {
                fillerRegionElement.classList.add('not-open');
            }
        }
    } catch (error) {
        // log it, but don't let that stop the next interval
        console.error(error);
    }
};

let wiredReconnectedHandler = false;
const streamSlotUpdates = () => {
    const streamSlotUpdatesInner = () => {
        subscription?.dispose();
        subscription = connection.stream<GameState>('GetSlotUpdates', slotDropdown[slotDropdown.selectedIndex].textContent)
            .subscribe({
                next: update,
                error: (err) => {
                    console.error(err);
                    loadingOverlay.classList.remove('collapse');

                    // if the error is caused by a disconnect, then for some reason we get invoked
                    // at a point where connection.state is still Connected. work around that with a
                    // setTimeout to allow the rest of the disconnect to propagate through signalR
                    // before we check connection.state...
                    setTimeout(streamSlotUpdates);
                },
                complete: () => { },
            });
    }

    if (connection.state == signalR.HubConnectionState.Connected) {
        streamSlotUpdatesInner();
    } else if (!wiredReconnectedHandler) {
        wiredReconnectedHandler = true;
        connection.onreconnected(() => {
            streamSlotUpdatesInner();
            wiredReconnectedHandler = false;
        });
    }
};

(async () => {
    try {
        await connection.start();

        connection.on('GotSlots', async (slots: string[]) => {
            loadingOverlay.classList.add('collapse');
            slotDropdown.addEventListener('change', streamSlotUpdates);
            slotDropdown.replaceChildren(...slots.map(slot => new Option(slot)));
            streamSlotUpdates();
        });

        await connection.invoke('GetSlots');
        connection.onreconnected(() => {
            loadingOverlay.classList.add('collapse');
        });
    } catch (err) {
        console.error(err);
    }
})();
