import * as signalR from '@microsoft/signalr';

interface GameState {
    epoch: number;
    current_region: string;
    rat_count: number;
    checked_locations: string[];
    open_regions: string[];
    completed_goal: boolean;
    inventory: { [item: string]: number };
}

const connection = new signalR.HubConnectionBuilder().withUrl('/gameStateHub').build();
let removePreviousEventListener = () => { };
connection.on('GotSlots', (slots: string[]) => {
    const slotDropdown = (<HTMLSelectElement>document.getElementById('slot-dropdown'));
    removePreviousEventListener();
    const onChange = async () => {
        try {
            await connection.invoke('GetUpdate', slots[slotDropdown.selectedIndex], -1);
        } catch (err) {
            console.error(err);
        }
    };
    slotDropdown.addEventListener('change', onChange);
    removePreviousEventListener = () => slotDropdown.removeEventListener('change', onChange);
    slotDropdown.replaceChildren(...slots.map(slot => new Option(slot)));
    setTimeout(onChange);
});

const already_found = new Set();
const already_checked = new Set();
connection.on('Updated', (slotName: string, state: GameState) => async () => {
    try {
        (<HTMLSpanElement>document.getElementById('rat-count')).textContent = `${state.rat_count}`;
        for (const [item, count] of Object.entries(state.inventory)) {
            if (count > 0) {
                if (already_found.has(item)) {
                    continue;
                }

                for (const container of document.getElementsByClassName('received-' + item.replaceAll(' ', '-').replaceAll('\'s', '').toLowerCase())) {
                    container.classList.remove('not-found');
                }

                already_found.add(item);
            }
        }

        for (const loc of state.checked_locations) {
            if (already_checked.has(loc)) {
                continue;
            }

            for (const container of document.getElementsByClassName(loc.replaceAll('_', '-'))) {
                container.classList.remove('not-checked');
            }

            already_checked.add(loc);
        }

        for (const reg of state.open_regions) {
            for (const container of document.getElementsByClassName(reg.replaceAll('_', '-'))) {
                container.classList.remove('not-open');
            }
        }

        for (const container of document.getElementsByClassName('current-region')) {
            container.classList.remove('current-region');
        }

        for (const container of document.getElementsByClassName(state.current_region.replaceAll('_', '-'))) {
            container.classList.add('current-region');
        }
    } catch (error) {
        // log it, but don't let that stop the next interval
        console.error(error);
    }

    if (!state.completed_goal) {
        try {
            await connection.invoke('GetUpdate', slotName, state.epoch);
        } catch (err) {
            console.error(err);
        }
    }
});

(async () => {
    try {
        await connection.start();
        await connection.invoke('GetSlots');
    } catch (err) {
        console.error(err);
    }
})();
