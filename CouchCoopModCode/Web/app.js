let ws;
let state = null;
let selectedCard = null;
let selectedPotion = null;
let lastStateType = null;
let session = null;
let lobby = null;
let lobbyNotice = '';
let selectedPlayerCount = Number(localStorage.getItem('couch_player_count') || 2);
let selectedSeed = localStorage.getItem('couch_run_seed') || '';
let autoReturnedGameOver = false;
const openPanels = new Set();

const COMBAT_TYPES = ['monster', 'elite', 'boss'];
const TARGET_ENEMY_TYPES = ['anyenemy', 'any_enemy', 'single_enemy'];

function connect() {
    ws = new WebSocket(`ws://${location.host}/ws`);
    ws.onopen = () => {
        document.getElementById('connecting').style.display = 'none';
        document.getElementById('game').style.display = 'flex';
        setConnectionLabel('');
        const name = localStorage.getItem('couch_player_name') || defaultPlayerName();
        const savedSessionId = localStorage.getItem('couch_session_id');
        if (savedSessionId)
            ws.send(JSON.stringify({ type: 'rejoin', session_id: savedSessionId }));
        else
            ws.send(JSON.stringify({ type: 'join', name }));
    };
    ws.onclose = () => {
        if (state) {
            setConnectionLabel('Reconnecting...');
        } else {
            document.getElementById('connecting').style.display = '';
            document.getElementById('connecting').querySelector('p').textContent = 'Reconnecting...';
            document.getElementById('game').style.display = 'none';
        }
        setTimeout(connect, 2000);
    };
    ws.onerror = () => ws.close();
    ws.onmessage = (e) => {
        try {
            const message = JSON.parse(e.data);
            if (message.type === 'session') {
                session = message;
                localStorage.setItem('couch_session_id', session.session_id);
                if (session.name) localStorage.setItem('couch_player_name', session.name);
                lobbyNotice = '';
                render();
                return;
            }
            if (message.type === 'lobby') {
                lobby = message;
                render();
                return;
            }
            if (message.type === 'notice') {
                lobbyNotice = message.message || '';
                setConnectionLabel(lobbyNotice);
                render();
                return;
            }
            if (message.type === 'error') {
                lobbyNotice = message.message || 'Error';
                setConnectionLabel(message.message || 'Error');
                render();
                return;
            }

            const nextState = message.type === 'state' ? message.state : message;
            if (lastStateType !== null && nextState?.state_type !== lastStateType)
                clearTransientSelection();
            state = nextState;
            lastStateType = state?.state_type;
            if (state?.state_type !== 'game_over')
                autoReturnedGameOver = false;
            render();
        } catch { /* ignore non-json */ }
    };
}

function send(action) {
    if (!ws || ws.readyState !== WebSocket.OPEN) return;
    if (action.type)
        ws.send(JSON.stringify(action));
    else
        ws.send(JSON.stringify({ type: 'action', ...action }));
}

function setConnectionLabel(text) {
    const bar = document.getElementById('status-bar');
    bar.dataset.connection = text || '';
}

function clearTransientSelection() {
    selectedCard = null;
    selectedPotion = null;
}

function escapeHtml(value) {
    return cleanText(value)
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#39;');
}

function attr(value) {
    return String(value ?? '')
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#39;');
}

function cleanText(value) {
    return String(value ?? '')
        .replace(/\[[^\]]*_energy_icon\.png\]/gi, 'E')
        .replace(/\[[^\]]*\.png\]/gi, '')
        .replace(/\s+/g, ' ')
        .trim();
}

function titleCase(value) {
    return String(value || 'unknown')
        .replaceAll('_', ' ')
        .replace(/\b\w/g, c => c.toUpperCase());
}

function defaultPlayerName() {
    return `Player ${Math.floor(Math.random() * 90) + 10}`;
}

function isCombatState() {
    return COMBAT_TYPES.includes(state?.state_type);
}

// --- Rendering ---

function render() {
    if (!session || !state || shouldShowCouchLobby()) {
        renderCouchLobby();
        return;
    }
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
        renderShop(state.shop || {}, 'Shop');
    else if (state.state_type === 'fake_merchant')
        renderFakeMerchant();
    else if (state.state_type === 'hand_select')
        renderHandSelect();
    else if (state.state_type === 'card_select')
        renderCardSelect();
    else if (state.state_type === 'bundle_select')
        renderBundleSelect();
    else if (state.state_type === 'relic_select')
        renderRelicSelect();
    else if (state.state_type === 'crystal_sphere')
        renderCrystalSphere();
    else if (state.state_type === 'treasure')
        renderTreasure();
    else if (state.state_type === 'menu')
        renderMenu();
    else if (state.state_type === 'game_over')
        renderGameOver();
    else if (state.state_type === 'overlay')
        renderOverlay();
    else
        renderGeneric();
}

function shouldShowCouchLobby() {
    if (!session) return true;
    if (state?.state_type === 'menu')
        return lobby?.couch_initialized === true;
    if (state?.state_type === 'couch_lobby') return true;
    if (session.player_slot === null || session.player_slot === undefined) return true;
    return false;
}

function renderCouchLobby() {
    const game = document.getElementById('game');
    if (game.style.display === 'none') return;
    renderStatusBar();
    const slots = getVisibleLobbySlots();
    const currentSlot = session?.player_slot;
    const playerName = session?.name || localStorage.getItem('couch_player_name') || defaultPlayerName();
    const hasSlot = currentSlot !== null && currentSlot !== undefined;
    const slotLabel = hasSlot ? `Player ${Number(currentSlot) + 1}${session?.is_host ? ' / Host' : ''}` : 'No slot';
    const canStart = slots.filter(s => s.claimed).length > 0 || hasSlot;
    const isHost = session?.is_host === true || lobby?.host_session_id === session?.session_id;
    document.getElementById('content').innerHTML = `
        <section class="couch-lobby">
            <div class="screen-title">Couch Co-op Lobby</div>
            <div class="lobby-card">
                <label class="field-label" for="player-name">Name</label>
                <div class="name-row">
                    <input id="player-name" class="text-input" maxlength="24" value="${attr(playerName)}" autocomplete="off">
                    <button class="icon-btn" title="Save name" onclick="savePlayerName()">Save</button>
                </div>
                <div class="lobby-status">
                    <span>${escapeHtml(slotLabel)}</span>
                    ${state?.state_type ? `<span>${escapeHtml(titleCase(state.state_type))}</span>` : ''}
                </div>
            </div>
            ${lobbyNotice ? `<div class="notice">${escapeHtml(lobbyNotice)}</div>` : ''}
            <div class="player-count-row" role="group" aria-label="Player count">
                ${[2, 3, 4].map(count => `
                    <button class="seg-btn ${selectedPlayerCount === count ? 'active' : ''}" ${isHost ? '' : 'disabled'} onclick="setPlayerCount(${count})">${count}P</button>
                `).join('')}
            </div>
            ${isHost ? `
                <div class="lobby-card">
                    <label class="field-label" for="run-seed">Seed</label>
                    <input id="run-seed" class="text-input" maxlength="32" value="${attr(selectedSeed)}" autocomplete="off" placeholder="Random" oninput="selectedSeed=this.value">
                </div>
            ` : ''}
            <div class="slot-grid">
                ${slots.map(s => `
                    <button class="slot-card ${s.claimed ? 'claimed' : ''} ${hasSlot && Number(currentSlot) === Number(s.slot) ? 'mine' : ''}"
                        ${s.claimed && (!hasSlot || Number(currentSlot) !== Number(s.slot)) ? 'disabled' : ''}
                        onclick="claimSlot(${Number(s.slot)})">
                        <strong>Player ${Number(s.slot) + 1}</strong>
                        <span>${slotSubtitle(s, hasSlot, currentSlot)}</span>
                    </button>
                `).join('')}
            </div>
            <button class="btn start-run" ${canStart && isHost ? '' : 'disabled'} onclick="startCouchRun()">Start Local Run</button>
            ${lobby?.players?.length ? `
                <div class="phone-list">
                    ${lobby.players.map(p => `
                        <div class="phone-row">
                            <span>${escapeHtml(p.name || 'Player')}</span>
                            <span>${escapeHtml(phoneRoleLabel(p))}</span>
                        </div>
                    `).join('')}
                </div>
            ` : ''}
        </section>
    `;
    document.getElementById('action-bar').innerHTML = '';
}

function getVisibleLobbySlots() {
    const known = lobby?.slots || [];
    const bySlot = new Map(known.map(s => [Number(s.slot), s]));
    const count = Math.max(2, Math.min(4, selectedPlayerCount || 2));
    return Array.from({ length: count }, (_, slot) => bySlot.get(slot) || { slot, claimed: false });
}

function slotSubtitle(slot, hasSlot, currentSlot) {
    if (hasSlot && Number(currentSlot) === Number(slot.slot)) return 'Claimed by you';
    if (slot.claimed) return escapeHtml(slot.name || 'Claimed');
    return 'Open';
}

function phoneRoleLabel(player) {
    if (player.role === 'spectator') return 'Spectator';
    if (player.player_slot !== null && player.player_slot !== undefined) return `Player ${Number(player.player_slot) + 1}`;
    return 'Unclaimed';
}

function savePlayerName() {
    const input = document.getElementById('player-name');
    const name = (input?.value || defaultPlayerName()).trim().slice(0, 24) || defaultPlayerName();
    localStorage.setItem('couch_player_name', name);
    send({ type: 'join', name });
}

function setPlayerCount(count) {
    selectedPlayerCount = count;
    localStorage.setItem('couch_player_count', String(count));
    renderCouchLobby();
}

function saveRunSeed() {
    const input = document.getElementById('run-seed');
    selectedSeed = (input?.value || '').trim().slice(0, 32);
    if (selectedSeed)
        localStorage.setItem('couch_run_seed', selectedSeed);
    else
        localStorage.removeItem('couch_run_seed');
    return selectedSeed;
}

function initCouchSession() {
    savePlayerName();
    saveRunSeed();
    send({ type: 'init_couch', player_count: selectedPlayerCount });
}

function claimSlot(slot) {
    savePlayerName();
    send({ type: 'claim_slot', slot });
}

function startCouchRun() {
    savePlayerName();
    const seed = saveRunSeed();
    const message = { type: 'start_couch', player_count: selectedPlayerCount };
    if (seed)
        message.seed = seed;
    send(message);
}

function renderStatusBar() {
    const p = state?.player;
    const bar = document.getElementById('status-bar');
    const connection = bar.dataset.connection ? `<span class="conn">${escapeHtml(bar.dataset.connection)}</span>` : '';

    if (!p) {
        bar.innerHTML = `
            <span class="screen-chip">${escapeHtml(titleCase(state?.menu_screen || state?.state_type || 'Lobby'))}</span>
            ${session?.player_slot !== undefined && session?.player_slot !== null ? `<span class="floor">P${Number(session.player_slot) + 1}</span>` : ''}
            ${connection}
        `;
        return;
    }

    const energy = p.energy !== undefined ? `<span class="energy">${escapeHtml(p.energy)}/${escapeHtml(p.max_energy)}</span>` : '';
    const stars = p.stars !== undefined ? `<span class="stars">${escapeHtml(p.stars)} stars</span>` : '';
    const block = p.block ? `<span class="block">${escapeHtml(p.block)} BLK</span>` : '';
    bar.innerHTML = `
        <span class="hp">${escapeHtml(p.hp)}/${escapeHtml(p.max_hp)} HP</span>
        ${energy}
        ${stars}
        ${block}
        <span class="gold">${escapeHtml(p.gold)}g</span>
        ${connection}
        ${session?.player_slot !== undefined && session?.player_slot !== null ? `<span>P${Number(session.player_slot) + 1}</span>` : ''}
        <span class="floor">F${escapeHtml(state.run?.floor || '?')}</span>
    `;
}

function setMainContent(markup, includePanels = true) {
    rememberOpenPanels();
    const panels = includePanels ? renderPlayerPanels() : '';
    document.getElementById('content').innerHTML = `${panels}${markup}`;
    restoreOpenPanels();
}

function setActionBar(markup = '') {
    document.getElementById('action-bar').innerHTML = markup;
}

function renderPlayerPanels() {
    if (!state.player) return '';
    const potions = state.player.potions || [];
    const relics = state.player.relics || [];
    const piles = [
        { key: 'draw_pile', label: 'Draw', count: state.player.draw_pile_count },
        { key: 'discard_pile', label: 'Discard', count: state.player.discard_pile_count },
        { key: 'exhaust_pile', label: 'Exhaust', count: state.player.exhaust_pile_count }
    ].filter(p => p.count !== undefined);

    return `
        <section class="player-panels">
            ${renderPotionsPanel(potions)}
            ${renderPilesPanel(piles)}
            ${renderRelicsPanel(relics)}
        </section>
    `;
}

function renderPotionsPanel(potions) {
    const maxSlots = state.player?.max_potion_slots || potions.length;
    const slots = [];
    for (let i = 0; i < maxSlots; i++) {
        const potion = potions.find(p => p.slot === i);
        slots.push(potion ? `
            <details class="mini-panel potion-slot" data-panel-id="potion-${Number(potion.slot)}">
                <summary>${escapeHtml(potion.name)} <span>${escapeHtml(potion.slot)}</span></summary>
                <div class="mini-desc">${escapeHtml(potion.description || '')}</div>
                <div class="mini-actions">
                    <button class="mini-btn" onclick="usePotion(${Number(potion.slot)})">Use</button>
                    <button class="mini-btn danger" onclick="discardPotion(${Number(potion.slot)})">Discard</button>
                </div>
            </details>
        ` : `
            <div class="mini-panel empty-slot">Empty</div>
        `);
    }
    if (slots.length === 0) return '';
    return `<div class="panel-row">${slots.join('')}</div>`;
}

function renderPilesPanel(piles) {
    if (piles.length === 0) return '';
    return `
        <div class="panel-row">
            ${piles.map(p => `
                <details class="mini-panel pile-panel" data-panel-id="pile-${attr(p.key)}">
                    <summary>${escapeHtml(p.label)} <span>${escapeHtml(p.count)}</span></summary>
                    ${renderPileCards(state.player?.[p.key] || [])}
                </details>
            `).join('')}
        </div>
    `;
}

function renderPileCards(cards) {
    if (!cards.length) return '<div class="mini-desc">No cards.</div>';
    return `
        <div class="pile-cards">
            ${cards.map(c => `
                <div class="pile-card">
                    <strong>${escapeHtml(c.name)}</strong>
                    <span>${escapeHtml(c.cost ?? '?')}</span>
                    <p>${escapeHtml(c.description || '')}</p>
                </div>
            `).join('')}
        </div>
    `;
}

function renderRelicsPanel(relics) {
    if (!relics.length) return '';
    return `
        <details class="wide-panel" data-panel-id="relics">
            <summary>Relics <span>${relics.length}</span></summary>
            <div class="relic-list">
                ${relics.map(r => `
                    <div class="relic">
                        <strong>${escapeHtml(r.name)}</strong>
                        ${r.counter !== null && r.counter !== undefined ? `<span>${escapeHtml(r.counter)}</span>` : ''}
                        <p>${escapeHtml(r.description || '')}</p>
                    </div>
                `).join('')}
            </div>
        </details>
    `;
}

function rememberOpenPanels() {
    document.querySelectorAll('details[data-panel-id]').forEach(details => {
        if (details.open)
            openPanels.add(details.dataset.panelId);
        else
            openPanels.delete(details.dataset.panelId);
    });
}

function restoreOpenPanels() {
    document.querySelectorAll('details[data-panel-id]').forEach(details => {
        details.open = openPanels.has(details.dataset.panelId);
        details.addEventListener('toggle', () => {
            if (details.open)
                openPanels.add(details.dataset.panelId);
            else
                openPanels.delete(details.dataset.panelId);
        });
    });
}

// --- Combat ---

function renderCombat() {
    const enemies = state.battle?.enemies || state.enemies || [];
    const hand = state.player?.hand || [];

    setMainContent(`
        ${selectedPotion !== null ? `<div class="target-banner">Choose an enemy for ${escapeHtml(selectedPotionName())}</div>` : ''}
        <div class="enemies">
            ${enemies.map(e => renderEnemy(e)).join('')}
        </div>
        <div class="hand">
            ${hand.map((c, i) => renderCard(c, i)).join('')}
        </div>
    `);

    const isReady = state.player?.is_ready_to_end_turn === true;
    const canEnd = state.player?.can_end_turn !== false;
    setActionBar(`
        ${selectedCard !== null || selectedPotion !== null ? `<button class="btn cancel" onclick="cancelTargeting()">Cancel</button>` : ''}
        <button class="btn end-turn" ${canEnd || isReady ? '' : 'disabled'} onclick="${isReady ? 'undoEndTurn()' : 'endTurn()'}">${isReady ? 'Undo End Turn' : 'End Turn'}</button>
    `);

    const content = document.getElementById('content');
    content.querySelectorAll('.card[data-index]').forEach(el => {
        el.addEventListener('click', () => selectCard(parseInt(el.dataset.index)));
    });
    content.querySelectorAll('.enemy').forEach(el => {
        el.addEventListener('click', () => targetEnemy(el.dataset.entityId || el.dataset.combatId));
    });
}

function renderEnemy(e) {
    const hpPct = e.max_hp ? Math.max(0, Math.min(100, Math.round((e.hp / e.max_hp) * 100))) : 0;
    const intents = (e.intents || []).map(i => `<span class="intent intent-${attr(String(i.type || 'unknown').toLowerCase())}">${escapeHtml(i.label || i.type)}</span>`).join(' ');
    const powers = (e.powers || e.status || []).map(p => `<span class="power">${escapeHtml(p.name)} ${escapeHtml(p.amount || '')}</span>`).join(' ');
    const targetable = selectedCard !== null || selectedPotion !== null ? 'targetable' : '';

    return `
        <div class="enemy ${targetable}" data-combat-id="${attr(e.combat_id)}" data-entity-id="${attr(e.entity_id || e.combat_id)}">
            <div class="enemy-name">${escapeHtml(e.name)}</div>
            <div class="enemy-intents">${intents}</div>
            <div class="hp-bar"><div class="hp-fill" style="width:${hpPct}%"></div></div>
            <div class="enemy-stats">
                <span>${escapeHtml(e.hp)}/${escapeHtml(e.max_hp)}</span>
                ${e.block ? `<span class="block">${escapeHtml(e.block)} BLK</span>` : ''}
            </div>
            ${powers ? `<div class="enemy-powers">${powers}</div>` : ''}
        </div>
    `;
}

function renderCard(c, index, options = {}) {
    const selected = selectedCard === index ? 'selected' : '';
    const playable = options.forcePlayable || c.can_play ? 'playable' : 'unplayable';
    const typeClass = String(c.type || '').toLowerCase();

    return `
        <div class="card ${selected} ${playable} ${attr(typeClass)}" data-index="${index}">
            <div class="card-cost">${escapeHtml(c.cost ?? '?')}${c.star_cost ? ` / ${escapeHtml(c.star_cost)}*` : ''}</div>
            <div class="card-name">${escapeHtml(c.name)}</div>
            <div class="card-desc">${escapeHtml(c.description || '')}</div>
        </div>
    `;
}

function selectCard(index) {
    const hand = state.player?.hand || [];
    const card = hand[index];
    if (!card || !card.can_play) return;

    selectedPotion = null;
    if (selectedCard === index) {
        if (!requiresEnemyTarget(card)) {
            playCard(index, null);
            return;
        }
        deselectCard();
        return;
    }

    selectedCard = index;
    if (!requiresEnemyTarget(card)) {
        playCard(index, null);
        return;
    }
    renderCombat();
}

function requiresEnemyTarget(item) {
    const targetType = String(item.target_type || '').toLowerCase();
    return TARGET_ENEMY_TYPES.includes(targetType);
}

function deselectCard() {
    selectedCard = null;
    renderCombat();
}

function cancelTargeting() {
    clearTransientSelection();
    render();
}

function targetEnemy(targetId) {
    if (selectedPotion !== null) {
        send({ action: 'use_potion', slot: selectedPotion, target: String(targetId) });
        selectedPotion = null;
        return;
    }
    if (selectedCard === null) return;
    playCard(selectedCard, targetId);
}

function playCard(cardIndex, targetId) {
    const action = { action: 'play_card', card_index: cardIndex };
    if (targetId !== null && targetId !== undefined)
        action.target = String(targetId);
    send(action);
    selectedCard = null;
}

function endTurn() {
    send({ action: 'end_turn' });
}

function undoEndTurn() {
    send({ action: 'undo_end_turn' });
}

function usePotion(slot) {
    const potion = (state.player?.potions || []).find(p => p.slot === slot);
    if (!potion) return;
    selectedCard = null;
    if (requiresEnemyTarget(potion) && isCombatState()) {
        selectedPotion = slot;
        renderCombat();
        return;
    }
    send({ action: 'use_potion', slot });
}

function selectedPotionName() {
    return (state.player?.potions || []).find(p => p.slot === selectedPotion)?.name || 'potion';
}

function discardPotion(slot) {
    const potion = (state.player?.potions || []).find(p => p.slot === slot);
    if (!potion) return;
    if (confirm(`Discard ${potion.name}?`))
        send({ action: 'discard_potion', slot });
}

// --- Map ---

function renderMap() {
    const map = state.map || {};
    const travelable = map.next_options || [];
    const bossNames = (map.bosses || []).map(b => b.name).filter(Boolean).join(' / ');
    const nodes = map.nodes || [];

    setMainContent(`
        <div class="screen-title">Choose Your Path</div>
        ${bossNames ? `<div class="subtle-line">Boss: ${escapeHtml(bossNames)}</div>` : ''}
        ${renderMapBoard(nodes, travelable)}
        <div class="choices">
            ${travelable.map((node, i) => `
                <button class="btn choice" onclick="chooseMapNode(${Number(node.index ?? i)})">
                    <span class="choice-icon">${escapeHtml(mapIcon(node.type))}</span>
                    <span>${escapeHtml(titleCase(node.type))}</span>
                    ${node.leads_to?.length ? `<span class="lookahead">${escapeHtml(node.leads_to.map(n => titleCase(n.type)).join(' / '))}</span>` : ''}
                </button>
            `).join('')}
        </div>
    `);
    setActionBar('');
}

function renderMapBoard(nodes, travelable) {
    if (!nodes.length) return '';
    const travelKeyToIndex = new Map(travelable.map((n, i) => [`${n.col},${n.row}`, n.index ?? i]));
    const visited = new Set((state.map?.visited || []).map(n => `${n.col},${n.row}`));
    const byRow = new Map();
    let maxCol = 0;
    for (const node of nodes) {
        const row = Number(node.row ?? 0);
        const col = Number(node.col ?? 0);
        maxCol = Math.max(maxCol, col);
        if (!byRow.has(row)) byRow.set(row, []);
        byRow.get(row).push(node);
    }
    const rows = [...byRow.keys()].sort((a, b) => b - a);
    return `
        <div class="map-board" style="--map-cols:${maxCol + 1}">
            ${rows.map(row => `
                <div class="map-row">
                    ${Array.from({ length: maxCol + 1 }, (_, col) => {
                        const node = (byRow.get(row) || []).find(n => Number(n.col ?? 0) === col);
                        if (!node) return '<span class="map-cell empty"></span>';
                        const key = `${node.col},${node.row}`;
                        const index = travelKeyToIndex.get(key);
                        const selectable = index !== undefined;
                        return `
                            <button class="map-cell ${selectable ? 'travelable' : ''} ${visited.has(key) ? 'visited' : ''}"
                                ${selectable ? `onclick="chooseMapNode(${Number(index)})"` : 'disabled'}
                                title="${attr(titleCase(node.type))}">
                                ${escapeHtml(mapIcon(node.type))}
                            </button>
                        `;
                    }).join('')}
                </div>
            `).join('')}
        </div>
    `;
}

function mapIcon(type) {
    const icons = {
        monster: 'M', elite: 'E', boss: 'B',
        rest_site: 'R', shop: '$', event: '?',
        treasure: 'T'
    };
    return icons[String(type || '').toLowerCase()] || '?';
}

function chooseMapNode(index) {
    send({ action: 'choose_map_node', index });
}

// --- Events ---

function renderEvent() {
    const evt = state.event || {};
    const options = evt.options || [];
    const body = evt.body && !String(evt.body).includes('.pages.DONE.description')
        ? evt.body
        : (evt.can_proceed ? 'Done. Waiting to continue.' : '');

    setMainContent(`
        <div class="screen-title">${escapeHtml(evt.event_name || 'Event')}</div>
        <div class="event-body">${escapeHtml(body)}</div>
        ${evt.waiting_for_all_players ? `<div class="notice">Waiting for the other players.</div>` : ''}
        <div class="choices">
            ${options.map(o => `
                <button class="btn choice ${o.is_locked ? 'disabled' : ''}"
                    ${o.is_locked ? 'disabled' : ''}
                    onclick="chooseEventOption(${Number(o.index)})">
                    <span>${escapeHtml(o.title || o.description || `Option ${o.index}`)}</span>
                    ${o.title && o.description ? `<span class="choice-desc">${escapeHtml(o.description)}</span>` : ''}
                    ${o.relic_name ? `<span class="choice-desc">${escapeHtml(o.relic_name)}: ${escapeHtml(o.relic_description || '')}</span>` : ''}
                </button>
            `).join('')}
        </div>
    `);

    setActionBar(evt.in_dialogue
        ? `<button class="btn" onclick="send({action:'advance_dialogue'})">Continue</button>`
        : (evt.can_proceed
            ? `<button class="btn" ${evt.waiting_for_all_players ? 'disabled' : ''} onclick="send({action:'proceed'})">Continue</button>`
            : ''));
}

function chooseEventOption(index) {
    send({ action: 'choose_event_option', index });
}

// --- Rest Site ---

function renderRestSite() {
    const rest = state.rest_site || {};
    const options = rest.options || [];

    setMainContent(`
        <div class="screen-title">Rest Site</div>
        <div class="choices">
            ${options.map(o => `
                <button class="btn choice ${o.is_enabled === false ? 'disabled' : ''}"
                    ${o.is_enabled === false ? 'disabled' : ''}
                    onclick="send({action:'choose_rest_option',index:${Number(o.index)}})">
                    <span>${escapeHtml(o.name || o.id)}</span>
                    ${o.description ? `<span class="choice-desc">${escapeHtml(o.description)}</span>` : ''}
                </button>
            `).join('')}
        </div>
    `);
    setActionBar(rest.can_proceed ? `<button class="btn" onclick="send({action:'proceed'})">Proceed</button>` : '');
}

// --- Rewards ---

function renderRewards() {
    const rewardsState = state.rewards || {};
    const rewards = rewardsState.items || [];

    setMainContent(`
        <div class="screen-title">Rewards</div>
        <div class="choices">
            ${rewards.map((r, i) => `
                <button class="btn choice" onclick="send({action:'claim_reward',index:${Number(r.index ?? i)}})">
                    <span>${escapeHtml(rewardLabel(r))}</span>
                    ${r.description ? `<span class="choice-desc">${escapeHtml(r.description)}</span>` : ''}
                </button>
            `).join('')}
        </div>
    `);
    setActionBar(`<button class="btn" ${rewardsState.can_proceed === false ? 'disabled' : ''} onclick="send({action:'proceed'})">Proceed</button>`);
}

function rewardLabel(r) {
    if (r.gold_amount) return `Gold: ${r.gold_amount}`;
    if (r.potion_name) return `Potion: ${r.potion_name}`;
    return `${titleCase(r.type)} ${r.name || ''}`.trim();
}

// --- Card Reward ---

function renderCardReward() {
    const reward = state.card_reward || {};
    const cards = reward.cards || [];

    setMainContent(`
        <div class="screen-title">Choose a Card</div>
        <div class="hand">
            ${cards.map((c, i) => `
                <div class="card playable ${attr(String(c.type || '').toLowerCase())}" onclick="send({action:'select_card_reward',card_index:${Number(c.index ?? i)}})">
                    <div class="card-cost">${escapeHtml(c.cost ?? '?')}</div>
                    <div class="card-name">${escapeHtml(c.name)}</div>
                    <div class="card-desc">${escapeHtml(c.description || '')}</div>
                </div>
            `).join('')}
        </div>
    `);
    setActionBar(reward.can_skip === false ? '' : `<button class="btn cancel" onclick="send({action:'skip_card_reward'})">Skip</button>`);
}

// --- Shop / Fake Merchant ---

function renderShop(shop, title) {
    const items = shop.items || [];

    setMainContent(`
        <div class="screen-title">${escapeHtml(title)}</div>
        ${shop.error ? `<div class="notice">${escapeHtml(shop.error)}</div>` : ''}
        <div class="choices">
            ${items.map((item, i) => {
                const disabled = item.can_afford === false || item.is_stocked === false;
                return `
                    <button class="btn choice ${disabled ? 'disabled' : ''}"
                        ${disabled ? 'disabled' : ''}
                        onclick="send({action:'shop_purchase',index:${Number(item.index ?? i)}})">
                        <span>${escapeHtml(shopItemName(item))}${item.price !== undefined ? ` - ${escapeHtml(item.price)}g` : ''}</span>
                        ${shopItemDescription(item)}
                    </button>
                `;
            }).join('')}
        </div>
    `);
    const leaveLabel = shop.can_proceed === false ? 'Close Shop' : 'Leave';
    setActionBar(`<button class="btn" onclick="send({action:'proceed'})">${leaveLabel}</button>`);
}

function renderFakeMerchant() {
    const fm = state.fake_merchant || {};
    if (fm.started_fight) {
        setMainContent(`
            <div class="screen-title">${escapeHtml(fm.event_name || 'Fake Merchant')}</div>
            <div class="notice">${escapeHtml(fm.message || 'Proceed when ready.')}</div>
        `);
        setActionBar(`<button class="btn" onclick="send({action:'proceed'})">Proceed</button>`);
        return;
    }
    renderShop(fm.shop || {}, fm.event_name || 'Fake Merchant');
}

function shopItemName(item) {
    return item.name || item.card_name || item.relic_name || item.potion_name || titleCase(item.category) || 'Item';
}

function shopItemDescription(item) {
    const description = item.card_description || item.relic_description || item.potion_description || '';
    const badges = [
        item.category ? titleCase(item.category) : '',
        item.on_sale ? 'Sale' : '',
        item.is_stocked === false ? 'Sold out' : '',
        item.can_afford === false ? 'Need gold' : ''
    ].filter(Boolean).join(' / ');
    return `${badges ? `<span class="choice-desc">${escapeHtml(badges)}</span>` : ''}${description ? `<span class="choice-desc">${escapeHtml(description)}</span>` : ''}`;
}

// --- Selection Screens ---

function renderHandSelect() {
    const hs = state.hand_select || {};
    const cards = hs.cards || state.player?.hand || [];
    const selected = hs.selected_cards || cards.filter(c => c.selected);

    setMainContent(`
        <div class="screen-title">${escapeHtml(hs.prompt || 'Select a card')}</div>
        ${selected.length ? `<div class="subtle-line">Selected: ${escapeHtml(selected.map(c => c.name).join(', '))}</div>` : ''}
        <div class="hand">
            ${cards.map((c, i) => `
                <div class="card playable ${c.selected ? 'selected' : ''} ${attr(String(c.type || '').toLowerCase())}" onclick="selectHandPromptCard(${Number(c.index ?? i)})">
                    <div class="card-cost">${escapeHtml(c.cost ?? '?')}</div>
                    <div class="card-name">${escapeHtml(c.name)}</div>
                    <div class="card-desc">${escapeHtml(c.description || '')}</div>
                </div>
            `).join('')}
        </div>
    `);
    setActionBar(`
        ${hs.can_cancel ? `<button class="btn cancel" onclick="cancelHandPromptSelection()">Cancel</button>` : ''}
        <button class="btn" ${hs.can_confirm === false ? 'disabled' : ''} onclick="confirmHandPromptSelection()">Confirm</button>
    `);
}

function selectHandPromptCard(index) {
    const hs = state.hand_select || {};
    if (hs.remote_choice)
        send({ action: 'select_card', index });
    else
        send({ action: 'combat_select_card', card_index: index });
}

function confirmHandPromptSelection() {
    const hs = state.hand_select || {};
    send({ action: hs.remote_choice ? 'confirm_selection' : 'combat_confirm_selection' });
}

function cancelHandPromptSelection() {
    send({ action: 'cancel_selection' });
}

function renderCardSelect() {
    const cs = state.card_select || {};
    const cards = cs.cards || [];

    setMainContent(`
        <div class="screen-title">${escapeHtml(cs.prompt || titleCase(cs.screen_type || 'Select a card'))}</div>
        ${cs.preview_showing ? `<div class="subtle-line">Preview ready. Confirm or cancel.</div>` : ''}
        <div class="hand">
            ${cards.map((c, i) => `
                <div class="card playable ${attr(String(c.type || '').toLowerCase())}" onclick="send({action:'select_card',index:${Number(c.index ?? i)}})">
                    <div class="card-cost">${escapeHtml(c.cost ?? '?')}</div>
                    <div class="card-name">${escapeHtml(c.name)}</div>
                    <div class="card-desc">${escapeHtml(c.description || '')}</div>
                </div>
            `).join('')}
        </div>
    `);
    setActionBar(`
        ${cs.can_cancel ? `<button class="btn cancel" onclick="send({action:'cancel_selection'})">${cs.screen_type === 'choose' ? 'Skip' : 'Cancel'}</button>` : ''}
        ${cs.can_confirm ? `<button class="btn" onclick="send({action:'confirm_selection'})">Confirm</button>` : ''}
    `);
}

function renderBundleSelect() {
    const bs = state.bundle_select || {};
    const bundles = bs.bundles || [];

    setMainContent(`
        <div class="screen-title">${escapeHtml(bs.prompt || 'Choose a bundle')}</div>
        ${bs.preview_cards?.length ? `<div class="preview-strip">${bs.preview_cards.map(c => renderSmallCard(c)).join('')}</div>` : ''}
        <div class="bundle-list">
            ${bundles.map((bundle, i) => `
                <button class="bundle btn choice" onclick="send({action:'select_bundle',index:${Number(bundle.index ?? i)}})">
                    <span>Bundle ${Number(bundle.index ?? i) + 1}</span>
                    <span class="choice-desc">${escapeHtml((bundle.cards || []).map(c => c.name).join(', '))}</span>
                </button>
            `).join('')}
        </div>
    `);
    setActionBar(`
        ${bs.can_cancel ? `<button class="btn cancel" onclick="send({action:'cancel_bundle_selection'})">Cancel</button>` : ''}
        ${bs.can_confirm ? `<button class="btn" onclick="send({action:'confirm_bundle_selection'})">Confirm</button>` : ''}
    `);
}

function renderSmallCard(c) {
    return `
        <div class="small-card">
            <strong>${escapeHtml(c.name)}</strong>
            <p>${escapeHtml(c.description || '')}</p>
        </div>
    `;
}

function renderRelicSelect() {
    const rs = state.relic_select || {};
    const relics = rs.relics || [];

    setMainContent(`
        <div class="screen-title">${escapeHtml(rs.prompt || 'Choose a relic')}</div>
        <div class="choices">
            ${relics.map((r, i) => `
                <button class="btn choice relic-choice" onclick="send({action:'select_relic',index:${Number(r.index ?? i)}})">
                    <span>${escapeHtml(r.name)}</span>
                    <span class="choice-desc">${escapeHtml(r.rarity || '')}</span>
                    <span class="choice-desc">${escapeHtml(r.description || '')}</span>
                </button>
            `).join('')}
        </div>
    `);
    setActionBar(rs.can_skip ? `<button class="btn cancel" onclick="send({action:'skip_relic_selection'})">Skip</button>` : '');
}

function renderTreasure() {
    const tr = state.treasure || {};
    const relics = tr.relics || [];

    setMainContent(`
        <div class="screen-title">Treasure</div>
        ${tr.message ? `<div class="notice">${escapeHtml(tr.message)}</div>` : ''}
        <div class="choices">
            ${relics.map((r, i) => `
                <button class="btn choice relic-choice" onclick="send({action:'claim_treasure_relic',index:${Number(r.index ?? i)}})">
                    <span>${escapeHtml(r.name)}</span>
                    <span class="choice-desc">${escapeHtml(r.rarity || '')}</span>
                    <span class="choice-desc">${escapeHtml(r.description || '')}</span>
                </button>
            `).join('')}
        </div>
    `);
    setActionBar(tr.can_proceed ? `<button class="btn" onclick="send({action:'proceed'})">Proceed</button>` : '');
}

// --- Crystal Sphere ---

function renderCrystalSphere() {
    const sphere = state.crystal_sphere || {};
    const width = sphere.grid_width || 0;
    const cells = sphere.cells || [];

    setMainContent(`
        <div class="screen-title">${escapeHtml(sphere.instructions_title || 'Crystal Sphere')}</div>
        ${sphere.instructions_description ? `<div class="event-body">${escapeHtml(sphere.instructions_description)}</div>` : ''}
        ${sphere.divinations_left_text ? `<div class="subtle-line">${escapeHtml(sphere.divinations_left_text)}</div>` : ''}
        <div class="tool-row">
            <button class="btn ${sphere.tool === 'small' ? 'active' : ''}" ${sphere.can_use_small_tool ? '' : 'disabled'} onclick="send({action:'crystal_sphere_set_tool',tool:'small'})">Small</button>
            <button class="btn ${sphere.tool === 'big' ? 'active' : ''}" ${sphere.can_use_big_tool ? '' : 'disabled'} onclick="send({action:'crystal_sphere_set_tool',tool:'big'})">Big</button>
        </div>
        <div class="sphere-grid" style="grid-template-columns: repeat(${Number(width || 1)}, minmax(34px, 1fr));">
            ${cells.map(cell => `
                <button class="sphere-cell ${cell.is_hidden ? 'hidden-cell' : 'revealed-cell'} ${cell.is_clickable ? 'clickable-cell' : ''} ${cell.is_good ? 'good-cell' : ''}"
                    ${cell.is_clickable ? '' : 'disabled'}
                    onclick="send({action:'crystal_sphere_click_cell',x:${Number(cell.x)},y:${Number(cell.y)}})">
                    ${cell.is_hidden ? '' : escapeHtml(cell.item_type ? itemShortName(cell.item_type) : '')}
                </button>
            `).join('')}
        </div>
        ${sphere.revealed_items?.length ? `<div class="subtle-line">Revealed: ${escapeHtml(sphere.revealed_items.map(i => itemShortName(i.item_type)).join(', '))}</div>` : ''}
    `);
    setActionBar(sphere.can_proceed ? `<button class="btn" onclick="send({action:'crystal_sphere_proceed'})">Proceed</button>` : '');
}

function itemShortName(name) {
    return String(name || '').replace(/^CrystalSphere/, '').replace(/Item$/, '') || '?';
}

// --- Menu / Overlays ---

function renderMenu() {
    const screen = state.menu_screen || 'menu';
    const characterIds = new Set((state.characters || []).map(c => c.id));
    const options = normalizeOptions(state.options || []).filter(o => !characterIds.has(o.name || o.option || ''));
    const characters = state.characters || [];

    setMainContent(`
        <div class="screen-title">${escapeHtml(titleCase(screen))}</div>
        ${state.message ? `<div class="notice">${escapeHtml(state.message)}</div>` : ''}
        ${characters.length ? renderCharacters(characters) : ''}
        ${state.epochs ? renderEpochSummary() : ''}
        ${state.friends ? renderFriendSummary() : ''}
        ${state.lobby ? renderLobbySummary(state.lobby) : ''}
        ${renderLocalCoopEntry()}
        <div class="choices">
            ${options.map(o => renderMenuOption(o)).join('')}
        </div>
    `, false);
    setActionBar('');
}

function renderLocalCoopEntry() {
    if (lobby?.couch_initialized) return '';
    return `
        <section class="lobby-card">
            <div class="lobby-status">
                <span>Local Co-op</span>
                <span>${selectedPlayerCount} players</span>
            </div>
            <div class="player-count-row" role="group" aria-label="Local co-op player count">
                ${[2, 3, 4].map(count => `
                    <button class="seg-btn ${selectedPlayerCount === count ? 'active' : ''}" onclick="setPlayerCount(${count})">${count}P</button>
                `).join('')}
            </div>
            <label class="field-label" for="run-seed">Seed</label>
            <input id="run-seed" class="text-input" maxlength="32" value="${attr(selectedSeed)}" autocomplete="off" placeholder="Random" oninput="selectedSeed=this.value">
            <button class="btn start-run" onclick="initCouchSession()">Start Local Co-op</button>
        </section>
    `;
}

function normalizeOptions(options) {
    return options.map(o => typeof o === 'string' ? { name: o, enabled: true } : o);
}

function renderMenuOption(option) {
    const name = option.name || option.option || '';
    const enabled = option.enabled !== false;
    return `
        <button class="btn choice ${enabled ? '' : 'disabled'}"
            ${enabled ? '' : 'disabled'}
            onclick="menuSelect('${attr(name)}')">
            <span>${escapeHtml(titleCase(name))}</span>
            ${option.reason ? `<span class="choice-desc">${escapeHtml(titleCase(option.reason))}</span>` : ''}
        </button>
    `;
}

function menuSelect(option) {
    send({ action: 'menu_select', option });
}

function renderCharacters(characters) {
    return `
        <div class="character-grid">
            ${characters.map(c => `
                <button class="character-card ${c.locked ? 'locked' : ''}" ${c.locked ? 'disabled' : ''} onclick="menuSelect('${attr(c.id)}')">
                    <strong>${escapeHtml(c.name || c.id)}</strong>
                    <span>${escapeHtml(c.hp)} HP / ${escapeHtml(c.gold)}g / ${escapeHtml(c.energy)} energy</span>
                    <p>${escapeHtml(c.description || '')}</p>
                    ${c.starting_relics?.length ? `<p>Relic: ${escapeHtml(c.starting_relics.map(r => r.name).join(', '))}</p>` : ''}
                    ${c.starting_deck?.length ? `<p>Deck: ${escapeHtml(c.starting_deck.join(', '))}</p>` : ''}
                </button>
            `).join('')}
        </div>
    `;
}

function renderEpochSummary() {
    return `<div class="subtle-line">Timeline: ${escapeHtml(state.revealed_count || 0)} revealed, ${escapeHtml(state.obtained_unrevealed_count || 0)} pending, ${escapeHtml(state.locked_count || 0)} locked</div>`;
}

function renderFriendSummary() {
    if (state.loading) return '<div class="subtle-line">Loading friends...</div>';
    if (state.no_friends) return '<div class="subtle-line">No joinable friends found.</div>';
    return '';
}

function renderLobbySummary(lobby) {
    return `<div class="subtle-line">Lobby: ${escapeHtml(lobby.player_count || 0)}/${escapeHtml(lobby.max_players || '?')} players, ${lobby.all_ready ? 'ready' : 'waiting'}</div>`;
}

function renderGameOver() {
    if (!autoReturnedGameOver) {
        autoReturnedGameOver = true;
        setTimeout(() => menuSelect('main_menu'), 250);
    }
    setMainContent(`
        <div class="screen-title">Run Ended</div>
        <div class="notice">${escapeHtml(state.game_over?.message || 'Run ended. Returning to main menu...')}</div>
    `, false);
    setActionBar(`<button class="btn" onclick="menuSelect('main_menu')">Main Menu</button>`);
}

function renderOverlay() {
    const overlay = state.overlay || {};
    setMainContent(`
        <div class="screen-title">${escapeHtml(titleCase(overlay.screen_type || 'Overlay'))}</div>
        <div class="notice">${escapeHtml(overlay.message || 'Manual interaction may be required in-game.')}</div>
    `);
    setActionBar('');
}

function renderGeneric() {
    setMainContent(`
        <div class="screen-title">${escapeHtml(titleCase(state.state_type || 'Unknown'))}</div>
        <div class="notice">${escapeHtml(state.message || 'Waiting for an actionable game state.')}</div>
        <details class="debug-details">
            <summary>Raw state</summary>
            <pre class="debug">${escapeHtml(JSON.stringify(state, null, 2))}</pre>
        </details>
    `);
    setActionBar('');
}

connect();
