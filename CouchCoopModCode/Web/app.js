let ws;
let state = null;
let selectedCard = null;

const COMBAT_TYPES = ['monster', 'elite', 'boss'];

function connect() {
    ws = new WebSocket(`ws://${location.host}/ws`);
    ws.onopen = () => {
        document.getElementById('connecting').style.display = 'none';
        document.getElementById('game').style.display = 'flex';
    };
    ws.onclose = () => {
        document.getElementById('connecting').style.display = '';
        document.getElementById('connecting').querySelector('p').textContent = 'Reconnecting...';
        document.getElementById('game').style.display = 'none';
        setTimeout(connect, 2000);
    };
    ws.onerror = () => ws.close();
    ws.onmessage = (e) => {
        try {
            state = JSON.parse(e.data);
            render();
        } catch { /* ignore non-json */ }
    };
}

function send(action) {
    if (ws.readyState === WebSocket.OPEN)
        ws.send(JSON.stringify(action));
}

// --- Rendering ---

function render() {
    if (!state) return;
    renderStatusBar();

    if (COMBAT_TYPES.includes(state.state_type))
        renderCombat();
    else if (state.state_type === 'map')
        renderMap();
    else if (state.state_type === 'event')
        renderEvent();
    else if (state.state_type === 'rest_site')
        renderRestSite();
    else if (state.state_type === 'rewards')
        renderRewards();
    else if (state.state_type === 'card_reward')
        renderCardReward();
    else if (state.state_type === 'shop')
        renderShop();
    else if (state.state_type === 'hand_select')
        renderHandSelect();
    else
        renderGeneric();
}

function renderStatusBar() {
    const p = state.player;
    if (!p) return;
    const bar = document.getElementById('status-bar');
    const energy = p.energy !== undefined ? `<span class="energy">${p.energy}/${p.max_energy}</span>` : '';
    const block = p.block ? `<span class="block">${p.block} BLK</span>` : '';
    bar.innerHTML = `
        <span class="hp">${p.hp}/${p.max_hp} HP</span>
        ${energy}
        ${block}
        <span class="gold">${p.gold}g</span>
        <span class="floor">F${state.run?.floor || '?'}</span>
    `;
}

// --- Combat ---

function renderCombat() {
    const content = document.getElementById('content');
    const enemies = state.enemies || [];
    const hand = state.player?.hand || [];

    content.innerHTML = `
        <div class="enemies">
            ${enemies.map(e => renderEnemy(e)).join('')}
        </div>
        <div class="hand">
            ${hand.map((c, i) => renderCard(c, i)).join('')}
        </div>
    `;

    const actionBar = document.getElementById('action-bar');
    const canEnd = state.player?.can_end_turn !== false;
    actionBar.innerHTML = `
        ${selectedCard !== null ? `<button class="btn cancel" onclick="deselectCard()">Cancel</button>` : ''}
        <button class="btn end-turn" ${canEnd ? '' : 'disabled'} onclick="endTurn()">End Turn</button>
    `;

    content.querySelectorAll('.card').forEach(el => {
        el.addEventListener('click', () => selectCard(parseInt(el.dataset.index)));
    });
    content.querySelectorAll('.enemy').forEach(el => {
        el.addEventListener('click', () => targetEnemy(parseInt(el.dataset.combatId)));
    });
}

function renderEnemy(e) {
    const hpPct = Math.round((e.hp / e.max_hp) * 100);
    const intents = (e.intents || []).map(i => `<span class="intent intent-${i.type || 'unknown'}">${i.label || i.type}</span>`).join(' ');
    const powers = (e.powers || []).map(p => `<span class="power">${p.name} ${p.amount || ''}</span>`).join(' ');
    const targetable = selectedCard !== null ? 'targetable' : '';

    return `
        <div class="enemy ${targetable}" data-combat-id="${e.combat_id}">
            <div class="enemy-name">${e.name}</div>
            <div class="enemy-intents">${intents}</div>
            <div class="hp-bar"><div class="hp-fill" style="width:${hpPct}%"></div></div>
            <div class="enemy-stats">
                <span>${e.hp}/${e.max_hp}</span>
                ${e.block ? `<span class="block">${e.block} BLK</span>` : ''}
            </div>
            ${powers ? `<div class="enemy-powers">${powers}</div>` : ''}
        </div>
    `;
}

function renderCard(c, index) {
    const selected = selectedCard === index ? 'selected' : '';
    const playable = c.can_play ? 'playable' : 'unplayable';
    const typeClass = (c.type || '').toLowerCase();

    return `
        <div class="card ${selected} ${playable} ${typeClass}" data-index="${index}">
            <div class="card-cost">${c.cost ?? '?'}</div>
            <div class="card-name">${c.name}</div>
            <div class="card-desc">${c.description || ''}</div>
        </div>
    `;
}

function selectCard(index) {
    const hand = state.player?.hand || [];
    const card = hand[index];
    if (!card || !card.can_play) return;

    if (selectedCard === index) {
        if (card.target_type !== 'single_enemy') {
            playCard(index, null);
            return;
        }
        deselectCard();
        return;
    }

    selectedCard = index;

    if (card.target_type !== 'single_enemy') {
        playCard(index, null);
        return;
    }

    renderCombat();
}

function deselectCard() {
    selectedCard = null;
    renderCombat();
}

function targetEnemy(combatId) {
    if (selectedCard === null) return;
    playCard(selectedCard, combatId);
}

function playCard(cardIndex, targetCombatId) {
    const action = { action: 'play_card', card_index: cardIndex };
    if (targetCombatId !== null && targetCombatId !== undefined)
        action.target_combat_id = targetCombatId;
    send(action);
    selectedCard = null;
}

function endTurn() {
    send({ action: 'end_turn' });
}

// --- Map ---

function renderMap() {
    const content = document.getElementById('content');
    const map = state.map || {};
    const travelable = map.travelable || [];

    content.innerHTML = `
        <div class="screen-title">Choose Your Path</div>
        <div class="choices">
            ${travelable.map((node, i) => `
                <button class="btn choice" onclick="chooseMapNode(${i})">
                    <span class="choice-icon">${mapIcon(node.node_type)}</span>
                    <span>${node.node_type || 'unknown'}</span>
                    ${node.lookahead ? `<span class="lookahead">${node.lookahead}</span>` : ''}
                </button>
            `).join('')}
        </div>
    `;
    document.getElementById('action-bar').innerHTML = '';
}

function mapIcon(type) {
    const icons = {
        monster: '?', elite: '!', boss: '!!',
        rest_site: 'R', shop: '$', event: '?',
        treasure: 'T'
    };
    return icons[type] || '?';
}

function chooseMapNode(index) {
    send({ action: 'choose_map_node', index: index });
}

// --- Events ---

function renderEvent() {
    const content = document.getElementById('content');
    const evt = state.event || {};
    const options = evt.options || [];

    content.innerHTML = `
        <div class="screen-title">${evt.name || 'Event'}</div>
        <div class="event-body">${evt.body || ''}</div>
        <div class="choices">
            ${options.map(o => `
                <button class="btn choice ${o.locked ? 'disabled' : ''}"
                    ${o.locked ? 'disabled' : ''}
                    onclick="chooseEventOption(${o.index})">
                    ${o.title || o.description || `Option ${o.index}`}
                </button>
            `).join('')}
        </div>
    `;

    const actionBar = document.getElementById('action-bar');
    if (evt.in_dialogue) {
        actionBar.innerHTML = `<button class="btn" onclick="send({action:'advance_dialogue'})">Continue</button>`;
    } else {
        actionBar.innerHTML = '';
    }
}

function chooseEventOption(index) {
    send({ action: 'choose_event_option', index: index });
}

// --- Rest Site ---

function renderRestSite() {
    const content = document.getElementById('content');
    const options = state.rest_site?.options || [];

    content.innerHTML = `
        <div class="screen-title">Rest Site</div>
        <div class="choices">
            ${options.map(o => `
                <button class="btn choice" onclick="send({action:'choose_rest_option',option:'${o.id || o.name}'})">
                    ${o.name || o.id}
                    ${o.description ? `<span class="choice-desc">${o.description}</span>` : ''}
                </button>
            `).join('')}
        </div>
    `;
    document.getElementById('action-bar').innerHTML = '';
}

// --- Rewards ---

function renderRewards() {
    const content = document.getElementById('content');
    const rewards = state.rewards || [];

    content.innerHTML = `
        <div class="screen-title">Rewards</div>
        <div class="choices">
            ${rewards.map((r, i) => `
                <button class="btn choice" onclick="send({action:'claim_reward',index:${i}})">
                    ${r.type}: ${r.description || r.name || ''}
                </button>
            `).join('')}
        </div>
    `;
    document.getElementById('action-bar').innerHTML = `
        <button class="btn" onclick="send({action:'proceed'})">Proceed</button>
    `;
}

// --- Card Reward ---

function renderCardReward() {
    const content = document.getElementById('content');
    const cards = state.card_reward?.cards || [];

    content.innerHTML = `
        <div class="screen-title">Choose a Card</div>
        <div class="hand">
            ${cards.map((c, i) => `
                <div class="card playable ${(c.type || '').toLowerCase()}" onclick="send({action:'select_card_reward',index:${i}})">
                    <div class="card-cost">${c.cost ?? '?'}</div>
                    <div class="card-name">${c.name}</div>
                    <div class="card-desc">${c.description || ''}</div>
                </div>
            `).join('')}
        </div>
    `;
    document.getElementById('action-bar').innerHTML = `
        <button class="btn cancel" onclick="send({action:'skip_card_reward'})">Skip</button>
    `;
}

// --- Shop ---

function renderShop() {
    const content = document.getElementById('content');
    const shop = state.shop || {};
    const items = [...(shop.cards || []), ...(shop.relics || []), ...(shop.potions || [])];

    content.innerHTML = `
        <div class="screen-title">Shop</div>
        <div class="choices">
            ${items.map((item, i) => `
                <button class="btn choice ${item.can_afford === false ? 'disabled' : ''}"
                    ${item.can_afford === false ? 'disabled' : ''}
                    onclick="send({action:'shop_purchase',index:${item.index ?? i}})">
                    ${item.name} - ${item.price}g
                </button>
            `).join('')}
        </div>
    `;
    document.getElementById('action-bar').innerHTML = `
        <button class="btn" onclick="send({action:'proceed'})">Leave</button>
    `;
}

// --- Hand Select ---

function renderHandSelect() {
    const content = document.getElementById('content');
    const cards = state.hand_select?.cards || state.player?.hand || [];
    const prompt = state.hand_select?.prompt || 'Select a card';

    content.innerHTML = `
        <div class="screen-title">${prompt}</div>
        <div class="hand">
            ${cards.map((c, i) => `
                <div class="card playable ${(c.type || '').toLowerCase()}" onclick="send({action:'combat_select_card',index:${c.index ?? i}})">
                    <div class="card-cost">${c.cost ?? '?'}</div>
                    <div class="card-name">${c.name}</div>
                    <div class="card-desc">${c.description || ''}</div>
                </div>
            `).join('')}
        </div>
    `;
    document.getElementById('action-bar').innerHTML = `
        <button class="btn" onclick="send({action:'combat_confirm_selection'})">Confirm</button>
        <button class="btn cancel" onclick="send({action:'cancel_selection'})">Cancel</button>
    `;
}

// --- Generic Fallback ---

function renderGeneric() {
    const content = document.getElementById('content');
    content.innerHTML = `
        <div class="screen-title">${state.state_type || 'Unknown'}</div>
        <pre class="debug">${JSON.stringify(state, null, 2)}</pre>
    `;
    document.getElementById('action-bar').innerHTML = `
        <button class="btn" onclick="send({action:'proceed'})">Proceed</button>
    `;
}

connect();
