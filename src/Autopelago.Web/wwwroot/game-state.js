'use strict';
class Payload {
    get current_region_classes() {
        switch (this.current_region) {
            case 'Menu':
                return ['before-basketball'];

            case 'basketball':
                return ['checked-basketball'];

            case 'before_minotaur':
                return ['before-minotaur'];

            case 'before_prawn_stars':
                return ['before-prawn-stars'];

            case 'minotaur':
                return ['checked-minotaur'];

            case 'prawn_stars':
                return ['checked-prawn-stars'];

            case 'before_restaurant':
                return ['before-restaurant'];

            case 'before_pirate_bake_sale':
                return ['before-pirate-bake-sale'];

            case 'restaurant':
                return ['checked-restaurant'];

            case 'pirate_bake_sale':
                return ['checked-pirate-bake-sale'];

            case 'after_restaurant':
                return ['after-restaurant'];

            case 'after_pirate_bake_sale':
                return ['after-pirate-bake-sale'];

            case 'bowling_ball_door':
                return ['checked-bowling-ball-door'];

            case 'before_captured_goldfish':
                return ['before-captured-goldfish'];

            case 'captured_goldfish':
                return ['checked-captured-goldfish'];

            default:
                return ['checked-captured-goldfish'];
        }
    }

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
            for (const container of document.getElementsByClassName(`checked-${classNameSuffix}`)) {
                container.classList.remove('not-open');
            }
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
connection.on('Updated', function (slotName, state) {
    if (slotName !== 'Ratthew') {
        return;
    }

    let completedGoal = false;
    try {
        const parsed = Object.assign(new Payload(), state);
        document.getElementById('title').text = `${slotName}'s Tracker`;
        document.getElementById('rat-count').textContent = `${parsed.rat_count}`;
        parsed.markFoundIf(x => x.rat_count, 'normal-rat');
        parsed.markFoundIf(x => x.inventory['Pack Rat'], 'pack-rat');
        parsed.markFoundIf(x => x.inventory['Pizza Rat'], 'pizza-rat');
        parsed.markFoundIf(x => x.inventory['Chef Rat'], 'chef-rat');
        parsed.markFoundIf(x => x.inventory['Ninja Rat'], 'ninja-rat');
        parsed.markFoundIf(x => x.inventory['Gym Rat'], 'gym-rat');
        parsed.markFoundIf(x => x.inventory['Computer Rat'], 'computer-rat');
        parsed.markFoundIf(x => x.inventory['Pie Rat'], 'pie-rat');
        parsed.markFoundIf(x => x.inventory['Ziggu Rat'], 'ziggu-rat');
        parsed.markFoundIf(x => x.inventory['Acro Rat'], 'acro-rat');
        parsed.markFoundIf(x => x.inventory['Lab Rat'], 'lab-rat');
        parsed.markFoundIf(x => x.inventory['Soc-Rat-es'], 'soc-rat-es');
        parsed.markFoundIf(x => x.inventory['A Cookie'], 'a-cookie');
        parsed.markFoundIf(x => x.inventory['Fresh Banana Peel'], 'fresh-banana-peel');
        parsed.markFoundIf(x => x.inventory['MacGuffin'], 'macguffin');
        parsed.markFoundIf(x => x.inventory['Blue Turtle Shell'], 'blue-turtle-shell');
        parsed.markFoundIf(x => x.inventory['Red Matador\'s Cape'], 'red-matador-cape');
        parsed.markFoundIf(x => x.inventory['Pair of Fake Mouse Ears'], 'pair-of-fake-mouse-ears');
        parsed.markFoundIf(x => x.inventory['Bribe'], 'bribe');
        parsed.markFoundIf(x => x.inventory['Masterful Longsword'], 'masterful-longsword');
        parsed.markFoundIf(x => x.inventory['Legally Binding Contract'], 'legally-binding-contract');
        parsed.markFoundIf(x => x.inventory['Priceless Antique'], 'priceless-antique');
        parsed.markFoundIf(x => x.inventory['Premium Can of Prawn Food'], 'premium-can-of-prawn-food');

        const checked_locations = new Set(parsed.checked_locations);
        parsed.markCheckedIf(() => checked_locations.has('basketball'), 'basketball');
        parsed.markCheckedIf(() => checked_locations.has('minotaur'), 'minotaur');
        parsed.markCheckedIf(() => checked_locations.has('prawn_stars'), 'prawn-stars');
        parsed.markCheckedIf(() => checked_locations.has('restaurant'), 'restaurant');
        parsed.markCheckedIf(() => checked_locations.has('pirate_bake_sale'), 'pirate-bake-sale');
        parsed.markCheckedIf(() => checked_locations.has('bowling_ball_door'), 'bowling-ball-door');
        parsed.markCheckedIf(() => checked_locations.has('captured_goldfish'), 'captured-goldfish');

        parsed.markLocationOpenIf(x => x.rat_count >= 5, 'basketball');
        parsed.markLocationOpenIf(x => checked_locations.has('basketball') && x.inventory['Red Matador\'s Cape'] > 0, 'minotaur');
        parsed.markLocationOpenIf(x => checked_locations.has('basketball') && x.inventory['Premium Can of Prawn Food'] > 0, 'prawn-stars');
        parsed.markLocationOpenIf(x => checked_locations.has('minotaur') && x.inventory['A Cookie'] > 0, 'restaurant');
        parsed.markLocationOpenIf(x => checked_locations.has('prawn_stars') && x.inventory['Bribe'] > 0, 'pirate-bake-sale');
        parsed.markLocationOpenIf(x => x.rat_count >= 20 && (checked_locations.has('restaurant') || checked_locations.has('pirate_bake_sale')), 'bowling-ball-door');
        parsed.markLocationOpenIf(x => checked_locations.has('bowling_ball_door') && x.inventory['Masterful Longsword'] > 0, 'captured-goldfish');

        parsed.markPathOpenIf(x => checked_locations.has('basketball'), 'minotaur');
        parsed.markPathOpenIf(x => checked_locations.has('basketball'), 'prawn-stars');
        parsed.markPathOpenIf(x => checked_locations.has('minotaur'), 'restaurant');
        parsed.markPathOpenIf(x => checked_locations.has('prawn_stars'), 'pirate-bake-sale');
        parsed.markPathOpenIf(x => checked_locations.has('restaurant'), 'bowling-ball-door after-restaurant');
        parsed.markPathOpenIf(x => checked_locations.has('pirate_bake_sale'), 'bowling-ball-door after-pirate-bake-sale');
        parsed.markPathOpenIf(x => checked_locations.has('bowling_ball_door'), 'captured-goldfish');

        const { stepInterval } = parsed.aura_modifiers;
        for (const container of document.getElementsByClassName('movement-speed-text')) {
            container.textContent = `${1 / stepInterval}x`;
        }

        const currentRegionClasses = parsed.current_region_classes;
        let needsChange = false;
        for (const container of document.getElementsByClassName('current-region')) {
            for (const currentRegionClass of currentRegionClasses) {
                if (!container.classList.contains(currentRegionClass)) {
                    container.classList.remove('current-region');
                    needsChange = true;
                }
            }
        }

        if (needsChange) {
            for (const container of document.getElementsByClassName(currentRegionClasses.join(' '))) {
                container.classList.add('current-region');
            }
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
