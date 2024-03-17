'use strict';

var connection = new signalR.HubConnectionBuilder().withUrl('/gameStateHub').build();

connection.on('Updated', function (slotName, state) {
    if (slotName !== 'Ratthew') {
        return;
    }

    document.getElementById('curr').innerHTML = `
    <ul>
        <li>Current location: ${state.currentLocation}</li>
        <li>Target location:  ${state.targetLocation}</li>
    </ul>
    Received Items
    <ul>
        ${state.receivedItems.map(item => `<li>${item}</li>`).join('')}
    </ul>
    Checked Locations
    <ul>
        ${state.checkedLocations.map(loc => `<li>${loc}</li>`).join('')}
    </ul>
    `;

    connection.invoke('GetUpdate', slotName);
});

connection
    .start()
    .then(() => connection.invoke('GetUpdate', 'Ratthew'));
