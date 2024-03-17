'use strict';
class Payload {
    get aura_modifiers() {
        return {
            stepInterval: 1,
        };
    }

    markFoundIf(prop, classNameSuffix) {
        if (prop(this) > 0) {
            for (const container of document.getElementsByClassName(`received-${classNameSuffix}`)) {
                container.classList.remove('not-found');
            }
        }
    }

    markPathOpenIf(prop, classNameSuffix) {
        if (prop(this)) {
            for (const container of document.getElementsByClassName(`before-${classNameSuffix}`)) {
                container.classList.remove('not-open');
            }
        }
    }

    markLocationOpenIf(prop, classNameSuffix) {
        if (prop(this)) {
        }
    }

    markCheckedIf(prop, classNameSuffix) {
        if (prop(this)) {
            for (const container of document.getElementsByClassName(`checked-${classNameSuffix}`)) {
                container.classList.remove('not-checked');
            }
        }
    }
}

const connection = new signalR.HubConnectionBuilder().withUrl('/gameStateHub').build();
const already_found = new Set();
const already_checked = new Set();
connection.on('Updated', function (slotName, state) {
    if (slotName !== 'Ratthew') {
        return;
    }

    let completedGoal = false;
    try {
        const parsed = Object.assign(new Payload(), state);
        document.getElementById('title').text = `${slotName}'s Tracker`;
        document.getElementById('rat-count').textContent = `${parsed.rat_count}`;
        for (const [item, count] of Object.entries(parsed.inventory)) {
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

        for (const loc of parsed.checked_locations) {
            if (already_checked.has(loc)) {
                continue;
            }

            for (const container of document.getElementsByClassName(loc.replaceAll('_', '-'))) {
                container.classList.remove('not-checked');
            }

            already_checked.add(loc);
        }

        for (const reg of parsed.open_regions) {
            for (const container of document.getElementsByClassName(reg.replaceAll('_', '-'))) {
                container.classList.remove('not-open');
            }
        }

        const { stepInterval } = parsed.aura_modifiers;
        for (const container of document.getElementsByClassName('movement-speed-text')) {
            container.textContent = `${1 / stepInterval}x`;
        }

        for (const container of document.getElementsByClassName('current-region')) {
            container.classList.remove('current-region');
        }

        for (const container of document.getElementsByClassName(parsed.current_region.replaceAll('_', '-'))) {
            container.classList.add('current-region');
        }
    } catch (error) {
        // log it, but don't let that stop the next interval
        console.error(error);
    }

    if (!completedGoal) {
        connection.invoke('GetUpdate', slotName);
    }
});

connection
    .start()
    .then(() => connection.invoke('GetUpdate', 'Ratthew'));
