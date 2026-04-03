const state = {
    token: null,
    serverUrl: '',
    pollTimer: null,
    positions: [],
    detections: []
};

document.getElementById('loginButton').addEventListener('click', login);
document.getElementById('refreshButton').addEventListener('click', refreshData);
document.getElementById('historyUser').addEventListener('change', loadHistory);

async function login() {
    state.serverUrl = document.getElementById('serverUrl').value.replace(/\/$/, '');
    const login = document.getElementById('login').value;
    const password = document.getElementById('password').value;

    const response = await fetch(`${state.serverUrl}/api/auth/login`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ login, password })
    });

    const payload = await response.json();
    if (!response.ok || !payload.success) {
        document.getElementById('authStatus').textContent = payload.message || 'Ошибка входа';
        return;
    }

    state.token = payload.token;
    document.getElementById('authStatus').textContent = `Вход выполнен: ${payload.role}, userId=${payload.userId}`;
    await refreshData();
    clearInterval(state.pollTimer);
    state.pollTimer = setInterval(refreshData, 2000);
}

async function refreshData() {
    if (!state.token) {
        return;
    }

    const [positionsResponse, detectionsResponse] = await Promise.all([
        authorizedFetch('/api/gps/current'),
        authorizedFetch('/api/detections/recent?take=100')
    ]);

    state.positions = await positionsResponse.json();
    state.detections = await detectionsResponse.json();
    fillHistorySelector();
    renderMap();
    document.getElementById('detectionsOutput').textContent = JSON.stringify(state.detections, null, 2);
    await loadHistory();
}

async function loadHistory() {
    const selector = document.getElementById('historyUser');
    if (!selector.value) {
        document.getElementById('historyOutput').textContent = 'Нет выбранного пользователя.';
        return;
    }

    const response = await authorizedFetch(`/api/gps/history/${selector.value}?take=200`);
    const payload = await response.json();
    document.getElementById('historyOutput').textContent = JSON.stringify(payload, null, 2);
}

function fillHistorySelector() {
    const selector = document.getElementById('historyUser');
    const currentValue = selector.value;
    selector.innerHTML = '';
    state.positions.forEach(position => {
        const option = document.createElement('option');
        option.value = position.userId;
        option.textContent = `${position.displayName} (${position.login})`;
        selector.appendChild(option);
    });

    if (currentValue) {
        selector.value = currentValue;
    }
}

function renderMap() {
    const mapCanvas = document.getElementById('mapCanvas');
    mapCanvas.innerHTML = '';

    const latitudes = state.positions.map(item => item.latitude)
        .concat(state.detections.map(item => item.latitude).filter(Boolean));
    const longitudes = state.positions.map(item => item.longitude)
        .concat(state.detections.map(item => item.longitude).filter(Boolean));

    if (!latitudes.length || !longitudes.length) {
        mapCanvas.textContent = 'Данные GPS ещё не поступили.';
        return;
    }

    const bounds = {
        minLat: Math.min(...latitudes),
        maxLat: Math.max(...latitudes),
        minLon: Math.min(...longitudes),
        maxLon: Math.max(...longitudes)
    };

    state.positions.forEach(position => {
        mapCanvas.appendChild(createMarker(bounds, position.longitude, position.latitude, `${position.displayName}`, 'position'));
    });

    state.detections.forEach(detection => {
        if (typeof detection.longitude !== 'number' || typeof detection.latitude !== 'number') {
            return;
        }

        const kind = detection.isAlly ? 'ally' : 'enemy';
        const text = `${detection.label}: ${detection.reporterLogin}`;
        mapCanvas.appendChild(createMarker(bounds, detection.longitude, detection.latitude, text, kind));
    });
}

function createMarker(bounds, longitude, latitude, text, kind) {
    const width = 1000;
    const height = 560;
    const marker = document.createElement('div');
    marker.className = `marker ${kind}`;
    marker.textContent = text;

    const lonSpan = Math.max(0.0001, bounds.maxLon - bounds.minLon);
    const latSpan = Math.max(0.0001, bounds.maxLat - bounds.minLat);
    const left = ((longitude - bounds.minLon) / lonSpan) * width;
    const top = height - (((latitude - bounds.minLat) / latSpan) * height);
    marker.style.left = `${left}px`;
    marker.style.top = `${top}px`;
    return marker;
}

function authorizedFetch(path) {
    return fetch(`${state.serverUrl}${path}`, {
        headers: {
            Authorization: `Bearer ${state.token}`
        }
    });
}