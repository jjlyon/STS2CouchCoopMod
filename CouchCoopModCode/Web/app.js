const status = document.getElementById('status');
const messages = document.getElementById('messages');
const input = document.getElementById('input');
const send = document.getElementById('send');

let ws;

function connect() {
    ws = new WebSocket(`ws://${location.host}/ws`);

    ws.onopen = () => {
        status.textContent = 'Connected';
        status.className = 'connected';
    };

    ws.onclose = () => {
        status.textContent = 'Disconnected — reconnecting...';
        status.className = '';
        setTimeout(connect, 2000);
    };

    ws.onerror = () => {
        ws.close();
    };

    ws.onmessage = (e) => {
        const div = document.createElement('div');
        div.textContent = e.data;
        messages.appendChild(div);
        messages.scrollTop = messages.scrollHeight;
    };
}

send.onclick = () => {
    if (input.value && ws.readyState === WebSocket.OPEN) {
        ws.send(input.value);
        input.value = '';
    }
};

input.onkeydown = (e) => {
    if (e.key === 'Enter') send.click();
};

connect();
