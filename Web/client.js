(function () {
    'use strict';

    if (window.__jellyfinDanmakuLoaded) {
        return;
    }
    window.__jellyfinDanmakuLoaded = true;

    const STORAGE_KEY = 'jellyfin-danmaku-enabled';
    const state = {
        apiClient: null,
        options: null,
        video: null,
        container: null,
        overlay: null,
        toggleButton: null,
        matchButton: null,
        itemId: null,
        match: null,
        candidates: [],
        comments: [],
        commentIndex: 0,
        lastTime: 0,
        activeComments: new Set(),
        laneAvailability: [],
        fixedLaneAvailability: { top: [], bottom: [] },
        enabled: true,
        identifyToken: 0,
        loadToken: 0,
        attachTimer: 0,
        frame: 0,
        dialog: null
    };

    const listeners = {
        loadstart: function () {
            resetTimeline();
            scheduleIdentification();
        },
        loadedmetadata: scheduleIdentification,
        playing: function () {
            updatePauseState();
            scheduleIdentification();
        },
        pause: updatePauseState,
        seeking: function () {
            clearActiveComments();
        },
        seeked: function () {
            resetTimeline();
            updatePauseState();
        },
        ratechange: function () {
            clearActiveComments();
            resetTimeline();
        },
        emptied: function () {
            clearActiveComments();
            scheduleIdentification();
        }
    };

    function injectStyles() {
        if (document.querySelector('#jfDanmakuStyles')) {
            return;
        }

        const style = document.createElement('style');
        style.id = 'jfDanmakuStyles';
        style.textContent = `
            .jf-danmaku-overlay {
                position: absolute;
                inset: 0;
                z-index: 2;
                overflow: hidden;
                pointer-events: none;
                contain: layout paint;
            }
            .jf-danmaku-overlay.jf-danmaku-hidden { display: none; }
            .jf-danmaku-overlay.jf-danmaku-paused .jf-danmaku-comment { animation-play-state: paused !important; }
            .jf-danmaku-comment {
                position: absolute;
                display: block;
                width: max-content;
                max-width: 90%;
                overflow: hidden;
                color: #fff;
                white-space: nowrap;
                font-family: sans-serif;
                font-weight: 600;
                line-height: 1.25;
                text-shadow: -1px -1px 1px #000, 1px -1px 1px #000, -1px 1px 1px #000, 1px 1px 1px #000;
                will-change: transform, opacity;
            }
            .jf-danmaku-scroll { left: 100%; animation-name: jf-danmaku-scroll; animation-timing-function: linear; animation-fill-mode: forwards; }
            .jf-danmaku-fixed { left: 50%; transform: translateX(-50%); text-align: center; animation-name: jf-danmaku-fixed; animation-timing-function: linear; animation-fill-mode: forwards; }
            @keyframes jf-danmaku-scroll {
                from { transform: translate3d(0, 0, 0); }
                to { transform: translate3d(calc(-100vw - 100%), 0, 0); }
            }
            @keyframes jf-danmaku-fixed {
                0%, 80% { opacity: 1; }
                100% { opacity: 0; }
            }
            .jf-danmaku-control-active .material-icons { color: #00a4dc; }
            .jf-danmaku-dialog-backdrop {
                position: fixed;
                inset: 0;
                z-index: 100000;
                display: flex;
                align-items: center;
                justify-content: center;
                padding: 1.5rem;
                background: rgba(0, 0, 0, .72);
            }
            .jf-danmaku-dialog {
                box-sizing: border-box;
                width: min(44rem, 100%);
                max-height: min(80vh, 48rem);
                overflow: auto;
                padding: 1.4rem;
                border-radius: .45rem;
                background: #202020;
                color: #fff;
                box-shadow: 0 .6rem 2.5rem rgba(0, 0, 0, .55);
            }
            .jf-danmaku-dialog h2 { margin: 0 0 1rem; }
            .jf-danmaku-dialog-row { display: flex; gap: .7rem; margin: .7rem 0; }
            .jf-danmaku-dialog input, .jf-danmaku-dialog select {
                box-sizing: border-box;
                min-width: 0;
                flex: 1;
                padding: .75rem;
                border: 1px solid #777;
                border-radius: .25rem;
                background: #111;
                color: #fff;
            }
            .jf-danmaku-dialog button {
                padding: .7rem 1rem;
                border: 0;
                border-radius: .25rem;
                cursor: pointer;
                background: #00a4dc;
                color: #fff;
                font-weight: 600;
            }
            .jf-danmaku-dialog button.secondary { background: #555; }
            .jf-danmaku-dialog button:disabled { cursor: default; opacity: .55; }
            .jf-danmaku-results { display: grid; gap: .55rem; margin-top: 1rem; }
            .jf-danmaku-result {
                display: block;
                width: 100%;
                text-align: left;
                background: #333 !important;
            }
            .jf-danmaku-result:hover, .jf-danmaku-result:focus { background: #444 !important; }
            .jf-danmaku-result-title { display: block; font-size: 1rem; }
            .jf-danmaku-result-subtitle { display: block; margin-top: .2rem; opacity: .75; font-size: .86rem; }
            .jf-danmaku-dialog-status { min-height: 1.4rem; margin-top: .7rem; opacity: .8; }
            .jf-danmaku-toast {
                position: fixed;
                left: 50%;
                bottom: 15%;
                z-index: 100001;
                max-width: min(34rem, 85vw);
                transform: translateX(-50%);
                padding: .7rem 1rem;
                border-radius: .3rem;
                background: rgba(20, 20, 20, .92);
                color: #fff;
                text-align: center;
                pointer-events: none;
                animation: jf-danmaku-toast 3.2s ease forwards;
            }
            @keyframes jf-danmaku-toast { 0%, 82% { opacity: 1; } 100% { opacity: 0; } }
        `;
        document.head.appendChild(style);
    }

    function waitForApiClient() {
        return new Promise(function (resolve, reject) {
            let attempts = 0;
            const timer = window.setInterval(function () {
                const apiClient = window.ApiClient;
                if (apiClient && typeof apiClient.ajax === 'function' && typeof apiClient.getUrl === 'function') {
                    window.clearInterval(timer);
                    resolve(apiClient);
                    return;
                }
                attempts += 1;
                if (attempts >= 120) {
                    window.clearInterval(timer);
                    reject(new Error('Jellyfin ApiClient is unavailable.'));
                }
            }, 250);
        });
    }

    function apiRequest(method, path, body) {
        const request = {
            type: method,
            url: state.apiClient.getUrl(path),
            dataType: 'json'
        };
        if (body !== undefined) {
            request.contentType = 'application/json';
            request.data = JSON.stringify(body);
        }
        return state.apiClient.ajax(request);
    }

    function readLocalEnabled() {
        try {
            return window.localStorage.getItem(STORAGE_KEY) !== 'false';
        } catch (error) {
            return true;
        }
    }

    function writeLocalEnabled(enabled) {
        try {
            window.localStorage.setItem(STORAGE_KEY, enabled ? 'true' : 'false');
        } catch (error) {
            // Storage can be unavailable in private embedded web views.
        }
    }

    function scheduleAttach() {
        window.clearTimeout(state.attachTimer);
        state.attachTimer = window.setTimeout(attachToPlayer, 80);
    }

    function attachToPlayer() {
        injectControls();
        const video = document.querySelector('.videoPlayerContainer video.htmlvideoplayer, .videoPlayerContainer video');
        if (!video) {
            detachVideo();
            return;
        }

        const container = video.closest('.videoPlayerContainer');
        if (!container) {
            return;
        }

        let playerChanged = false;
        if (state.video !== video) {
            detachVideo();
            state.video = video;
            state.container = container;
            playerChanged = true;
            Object.keys(listeners).forEach(function (name) {
                video.addEventListener(name, listeners[name]);
            });
        }

        if (!state.overlay || !state.overlay.isConnected || state.overlay.parentNode !== container) {
            const overlay = document.createElement('div');
            overlay.className = 'jf-danmaku-overlay';
            overlay.setAttribute('aria-hidden', 'true');
            container.appendChild(overlay);
            state.overlay = overlay;
            playerChanged = true;
            updateOverlayOptions();
            updatePauseState();
        }

        updateControlState();
        if (playerChanged || !state.itemId) {
            scheduleIdentification();
        }
        if (!state.frame) {
            state.frame = window.requestAnimationFrame(renderFrame);
        }
    }

    function detachVideo() {
        if (state.video) {
            Object.keys(listeners).forEach(function (name) {
                state.video.removeEventListener(name, listeners[name]);
            });
        }
        clearActiveComments();
        state.overlay?.remove();
        state.video = null;
        state.container = null;
        state.overlay = null;
        state.itemId = null;
        state.match = null;
        state.comments = [];
        state.candidates = [];
        state.commentIndex = 0;
        state.identifyToken += 1;
        state.loadToken += 1;
    }

    function injectControls() {
        const buttonRow = document.querySelector('#videoOsdPage .buttons');
        if (!buttonRow) {
            return;
        }

        if (!state.toggleButton || !state.toggleButton.isConnected) {
            state.toggleButton = makeControlButton('jf-danmaku-toggle', 'comment', '开关弹幕');
            state.toggleButton.addEventListener('click', function (event) {
                event.preventDefault();
                event.stopPropagation();
                state.enabled = !state.enabled;
                writeLocalEnabled(state.enabled);
                if (!state.enabled) {
                    clearActiveComments();
                } else if (state.itemId && state.comments.length === 0) {
                    loadItem(state.itemId);
                }
                updateControlState();
            });
        }

        if (!state.matchButton || !state.matchButton.isConnected) {
            state.matchButton = makeControlButton('jf-danmaku-match', 'manage_search', '手动匹配弹幕');
            state.matchButton.addEventListener('click', function (event) {
                event.preventDefault();
                event.stopPropagation();
                openMatchDialog();
            });
        }

        const anchor = buttonRow.querySelector('.btnVideoOsdSettings');
        if (anchor) {
            if (!state.toggleButton.isConnected) {
                buttonRow.insertBefore(state.toggleButton, anchor);
            }
            if (!state.matchButton.isConnected) {
                buttonRow.insertBefore(state.matchButton, anchor);
            }
        } else {
            if (!state.toggleButton.isConnected) {
                buttonRow.appendChild(state.toggleButton);
            }
            if (!state.matchButton.isConnected) {
                buttonRow.appendChild(state.matchButton);
            }
        }
        updateControlState();
    }

    function makeControlButton(className, iconName, title) {
        const button = document.createElement('button');
        button.type = 'button';
        button.setAttribute('is', 'paper-icon-button-light');
        button.className = className + ' autoSize paper-icon-button-light';
        button.title = title;
        button.setAttribute('aria-label', title);
        const icon = document.createElement('span');
        icon.className = 'xlargePaperIconButton material-icons ' + iconName;
        icon.setAttribute('aria-hidden', 'true');
        icon.textContent = iconName;
        button.appendChild(icon);
        return button;
    }

    function updateControlState() {
        const pluginEnabled = Boolean(state.options && state.options.enabled);
        [state.toggleButton, state.matchButton].forEach(function (button) {
            if (button) {
                button.style.display = pluginEnabled ? '' : 'none';
            }
        });
        if (state.toggleButton) {
            state.toggleButton.classList.toggle('jf-danmaku-control-active', pluginEnabled && state.enabled);
            state.toggleButton.title = state.match
                ? (state.enabled ? '关闭弹幕' : '开启弹幕') + ' · ' + state.match.animeTitle + ' ' + state.match.episodeTitle
                : (state.enabled ? '关闭弹幕' : '开启弹幕');
        }
        if (state.matchButton) {
            state.matchButton.classList.toggle('jf-danmaku-control-active', Boolean(state.match));
        }
        if (state.overlay) {
            state.overlay.classList.toggle('jf-danmaku-hidden', !pluginEnabled || !state.enabled);
        }
    }

    function updateOverlayOptions() {
        if (!state.overlay || !state.options) {
            return;
        }
        state.overlay.style.opacity = String(state.options.opacity || 0.9);
    }

    function scheduleIdentification() {
        if (!state.video || !state.options || !state.options.enabled) {
            return;
        }
        const token = ++state.identifyToken;
        window.setTimeout(function () {
            identifyCurrentItem(token);
        }, 180);
    }

    async function identifyCurrentItem(token) {
        const sampledVideo = state.video;
        for (let attempt = 0; attempt < 7; attempt += 1) {
            if (token !== state.identifyToken || !sampledVideo || sampledVideo !== state.video) {
                return;
            }
            let itemId = parseItemIdFromVideo(sampledVideo) || parseItemIdFromLocation();
            if (!itemId) {
                itemId = await probeOwnSessionItem();
            }
            if (itemId) {
                if (itemId !== state.itemId) {
                    if (state.enabled) {
                        await loadItem(itemId);
                    } else {
                        state.itemId = itemId;
                        state.match = null;
                        state.comments = [];
                        state.candidates = [];
                        resetTimeline();
                        updateControlState();
                    }
                }
                return;
            }
            await delay(400);
        }
    }

    function parseItemIdFromVideo(video) {
        const source = video.currentSrc || video.src || '';
        if (!source) {
            return null;
        }
        const videoMatch = source.match(/\/Videos\/([0-9a-f-]{32,36})(?:\/|\?|$)/i);
        if (videoMatch) {
            return normalizeItemId(videoMatch[1]);
        }
        try {
            const url = new URL(source, window.location.href);
            return normalizeItemId(url.searchParams.get('ItemId') || url.searchParams.get('itemId'));
        } catch (error) {
            return null;
        }
    }

    function parseItemIdFromLocation() {
        const combined = window.location.search + '&' + window.location.hash.replace(/^#/, '');
        const match = combined.match(/[?&](?:id|itemId)=([0-9a-f-]{32,36})/i);
        return match ? normalizeItemId(match[1]) : null;
    }

    function normalizeItemId(value) {
        if (!value) {
            return null;
        }
        const normalized = String(value).trim();
        return /^[0-9a-f]{32}$/i.test(normalized) || /^[0-9a-f-]{36}$/i.test(normalized)
            ? normalized
            : null;
    }

    async function probeOwnSessionItem() {
        try {
            const userId = state.apiClient.getCurrentUserId();
            const deviceId = typeof state.apiClient.deviceId === 'function' ? state.apiClient.deviceId() : '';
            if (!userId || !deviceId) {
                return null;
            }
            const sessions = await apiRequest(
                'GET',
                '/Sessions?ControllableByUserId=' + encodeURIComponent(userId));
            if (!Array.isArray(sessions)) {
                return null;
            }
            const matches = sessions.filter(function (session) {
                return session && session.DeviceId === deviceId && session.NowPlayingItem && session.NowPlayingItem.Id;
            });
            return matches.length === 1 ? normalizeItemId(matches[0].NowPlayingItem.Id) : null;
        } catch (error) {
            return null;
        }
    }

    async function loadItem(itemId) {
        const token = ++state.loadToken;
        state.itemId = itemId;
        state.match = null;
        state.comments = [];
        state.candidates = [];
        clearActiveComments();
        resetTimeline();
        updateControlState();
        try {
            const source = state.options.defaultSource || 'dandanplay';
            const result = await apiRequest(
                'GET',
                '/Danmaku/items/' + encodeURIComponent(itemId)
                    + '/comments?source=' + encodeURIComponent(source)
                    + '&autoMatch=true');
            if (token !== state.loadToken || itemId !== state.itemId) {
                return;
            }
            applyLoadResult(result);
            if (result.message) {
                showToast(result.message);
            } else if (result.matched) {
                showToast('已加载 ' + state.comments.length + ' 条弹幕');
            }
        } catch (error) {
            if (token === state.loadToken) {
                showToast(getErrorMessage(error, '弹幕加载失败，请检查插件设置。'));
            }
        }
    }

    function applyLoadResult(result) {
        state.match = result && result.matched ? result.match : null;
        state.candidates = Array.isArray(result && result.candidates) ? result.candidates : [];
        state.comments = normalizeComments(result && result.comments);
        resetTimeline();
        updateControlState();
    }

    function normalizeComments(comments) {
        if (!Array.isArray(comments)) {
            return [];
        }
        return comments.map(function (comment) {
            return {
                time: Number(comment.time) || 0,
                mode: [4, 5].includes(Number(comment.mode)) ? Number(comment.mode) : 1,
                color: Math.max(0, Math.min(0xFFFFFF, Number(comment.color) || 0xFFFFFF)),
                text: String(comment.text || '').slice(0, 300)
            };
        }).filter(function (comment) {
            return comment.text && Number.isFinite(comment.time);
        }).sort(function (left, right) {
            return left.time - right.time;
        });
    }

    function renderFrame() {
        state.frame = window.requestAnimationFrame(renderFrame);
        const video = state.video;
        if (!video || !state.overlay || !state.enabled || !state.options || !state.options.enabled
            || video.paused || video.seeking || state.comments.length === 0) {
            return;
        }
        const currentTime = video.currentTime || 0;
        if (currentTime < state.lastTime - 0.35 || currentTime > state.lastTime + 2.5) {
            clearActiveComments();
            state.commentIndex = lowerBound(state.comments, Math.max(0, currentTime - 0.1));
        }
        const earliest = currentTime - 0.35;
        const latest = currentTime + 0.08;
        while (state.commentIndex < state.comments.length
            && state.comments[state.commentIndex].time <= latest) {
            const comment = state.comments[state.commentIndex];
            if (comment.time >= earliest) {
                spawnComment(comment, currentTime);
            }
            state.commentIndex += 1;
        }
        state.lastTime = currentTime;
    }

    function spawnComment(comment, currentTime) {
        if (!state.overlay || state.activeComments.size >= (state.options.maxOnScreen || 80)) {
            return;
        }
        const node = document.createElement('span');
        node.className = 'jf-danmaku-comment';
        node.textContent = comment.text;
        node.style.color = '#' + comment.color.toString(16).padStart(6, '0');
        const fontSize = Math.max(14, 24 * (state.options.fontScale || 1));
        node.style.fontSize = fontSize + 'px';
        const playbackRate = Math.max(0.25, state.video.playbackRate || 1);
        if (comment.mode === 5 || comment.mode === 4) {
            const position = comment.mode === 5 ? 'top' : 'bottom';
            const lane = chooseFixedLane(position, currentTime, fontSize);
            node.classList.add('jf-danmaku-fixed');
            node.style[position] = (5 + lane * fontSize * 1.35) + 'px';
            node.style.animationDuration = (4 / playbackRate) + 's';
        } else {
            const mediaDuration = Math.max(3, state.options.speedSeconds || 8);
            const lane = chooseScrollLane(currentTime, mediaDuration, fontSize);
            node.classList.add('jf-danmaku-scroll');
            node.style.top = (5 + lane * fontSize * 1.35) + 'px';
            node.style.animationDuration = (mediaDuration / playbackRate) + 's';
        }
        const remove = function () {
            state.activeComments.delete(node);
            node.remove();
        };
        node.addEventListener('animationend', remove, { once: true });
        state.activeComments.add(node);
        state.overlay.appendChild(node);
    }

    function chooseScrollLane(currentTime, duration, fontSize) {
        const laneCount = Math.max(1, Math.floor((state.overlay.clientHeight * 0.68) / (fontSize * 1.35)));
        if (state.laneAvailability.length !== laneCount) {
            state.laneAvailability = new Array(laneCount).fill(0);
        }
        let selected = 0;
        for (let index = 0; index < laneCount; index += 1) {
            if (state.laneAvailability[index] <= currentTime) {
                selected = index;
                break;
            }
            if (state.laneAvailability[index] < state.laneAvailability[selected]) {
                selected = index;
            }
        }
        state.laneAvailability[selected] = currentTime + duration * 0.42;
        return selected;
    }

    function chooseFixedLane(position, currentTime, fontSize) {
        const laneCount = Math.max(1, Math.floor((state.overlay.clientHeight * 0.3) / (fontSize * 1.35)));
        const availability = state.fixedLaneAvailability[position];
        if (availability.length !== laneCount) {
            state.fixedLaneAvailability[position] = new Array(laneCount).fill(0);
        }
        const lanes = state.fixedLaneAvailability[position];
        let selected = 0;
        for (let index = 0; index < lanes.length; index += 1) {
            if (lanes[index] <= currentTime) {
                selected = index;
                break;
            }
            if (lanes[index] < lanes[selected]) {
                selected = index;
            }
        }
        lanes[selected] = currentTime + 4;
        return selected;
    }

    function lowerBound(comments, time) {
        let low = 0;
        let high = comments.length;
        while (low < high) {
            const middle = Math.floor((low + high) / 2);
            if (comments[middle].time < time) {
                low = middle + 1;
            } else {
                high = middle;
            }
        }
        return low;
    }

    function resetTimeline() {
        const currentTime = state.video ? state.video.currentTime || 0 : 0;
        state.commentIndex = lowerBound(state.comments, Math.max(0, currentTime - 0.1));
        state.lastTime = currentTime;
        state.laneAvailability = [];
        state.fixedLaneAvailability = { top: [], bottom: [] };
    }

    function clearActiveComments() {
        state.activeComments.forEach(function (node) {
            node.remove();
        });
        state.activeComments.clear();
        state.laneAvailability = [];
        state.fixedLaneAvailability = { top: [], bottom: [] };
    }

    function updatePauseState() {
        if (state.overlay && state.video) {
            state.overlay.classList.toggle('jf-danmaku-paused', state.video.paused);
        }
    }

    function openMatchDialog() {
        if (!state.itemId) {
            showToast('暂时无法识别当前播放项目，请稍后再试。');
            scheduleIdentification();
            return;
        }
        closeMatchDialog();
        const backdrop = document.createElement('div');
        backdrop.className = 'jf-danmaku-dialog-backdrop';
        const dialog = document.createElement('div');
        dialog.className = 'jf-danmaku-dialog';
        dialog.setAttribute('role', 'dialog');
        dialog.setAttribute('aria-modal', 'true');

        const title = document.createElement('h2');
        title.textContent = '手动匹配弹幕';
        dialog.appendChild(title);

        const sourceRow = document.createElement('div');
        sourceRow.className = 'jf-danmaku-dialog-row';
        const sourceSelect = document.createElement('select');
        (state.options.sources || []).forEach(function (source) {
            const option = document.createElement('option');
            option.value = source.id;
            option.textContent = source.name + (source.configured ? '' : '（未配置）');
            option.disabled = !source.configured;
            sourceSelect.appendChild(option);
        });
        sourceSelect.value = (state.match && state.match.source) || state.options.defaultSource || 'dandanplay';
        sourceRow.appendChild(sourceSelect);
        dialog.appendChild(sourceRow);

        const searchRow = document.createElement('div');
        searchRow.className = 'jf-danmaku-dialog-row';
        const input = document.createElement('input');
        input.type = 'search';
        input.placeholder = '作品标题（留空则按媒体信息匹配）';
        input.setAttribute('aria-label', '搜索作品标题');
        const searchButton = document.createElement('button');
        searchButton.type = 'button';
        searchButton.textContent = '搜索';
        searchRow.appendChild(input);
        searchRow.appendChild(searchButton);
        dialog.appendChild(searchRow);

        const status = document.createElement('div');
        status.className = 'jf-danmaku-dialog-status';
        dialog.appendChild(status);
        const results = document.createElement('div');
        results.className = 'jf-danmaku-results';
        dialog.appendChild(results);

        const footer = document.createElement('div');
        footer.className = 'jf-danmaku-dialog-row';
        const closeButton = document.createElement('button');
        closeButton.type = 'button';
        closeButton.className = 'secondary';
        closeButton.textContent = '关闭';
        footer.appendChild(closeButton);
        dialog.appendChild(footer);
        backdrop.appendChild(dialog);
        document.body.appendChild(backdrop);
        state.dialog = backdrop;

        function showResults(matches) {
            results.replaceChildren();
            if (!matches || matches.length === 0) {
                status.textContent = '没有找到匹配结果。';
                return;
            }
            status.textContent = '找到 ' + matches.length + ' 个结果，请选择正确剧集：';
            matches.forEach(function (match) {
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'jf-danmaku-result';
                const main = document.createElement('span');
                main.className = 'jf-danmaku-result-title';
                main.textContent = match.animeTitle || '未命名作品';
                const sub = document.createElement('span');
                sub.className = 'jf-danmaku-result-subtitle';
                sub.textContent = (match.episodeTitle || '未命名剧集') + ' · ID ' + match.episodeId;
                button.appendChild(main);
                button.appendChild(sub);
                button.addEventListener('click', function () {
                    selectManualMatch(match, status, button);
                });
                results.appendChild(button);
            });
        }

        async function search() {
            searchButton.disabled = true;
            status.textContent = '正在搜索…';
            results.replaceChildren();
            try {
                const query = input.value.trim();
                const path = '/Danmaku/items/' + encodeURIComponent(state.itemId)
                    + '/matches?source=' + encodeURIComponent(sourceSelect.value)
                    + (query ? '&query=' + encodeURIComponent(query) : '');
                const response = await apiRequest('GET', path);
                showResults(response.matches || []);
            } catch (error) {
                status.textContent = getErrorMessage(error, '搜索失败，请检查弹幕源设置。');
            } finally {
                searchButton.disabled = false;
            }
        }

        searchButton.addEventListener('click', search);
        input.addEventListener('keydown', function (event) {
            if (event.key === 'Enter') {
                event.preventDefault();
                search();
            }
        });
        closeButton.addEventListener('click', closeMatchDialog);
        backdrop.addEventListener('click', function (event) {
            if (event.target === backdrop) {
                closeMatchDialog();
            }
        });
        backdrop.addEventListener('keydown', function (event) {
            if (event.key === 'Escape') {
                closeMatchDialog();
            }
        });

        prefillSearch(input);
        if (state.candidates.length > 0) {
            showResults(state.candidates);
        }
        window.setTimeout(function () { input.focus(); }, 0);
    }

    async function prefillSearch(input) {
        try {
            const item = await state.apiClient.getItem(state.apiClient.getCurrentUserId(), state.itemId);
            if (state.dialog && !input.value) {
                input.value = item.SeriesName || item.Name || '';
            }
        } catch (error) {
            // Metadata is only a convenience for the search field.
        }
    }

    async function selectManualMatch(match, status, button) {
        button.disabled = true;
        status.textContent = '正在加载弹幕…';
        const itemId = state.itemId;
        try {
            const response = await apiRequest(
                'POST',
                '/Danmaku/items/' + encodeURIComponent(itemId) + '/match',
                {
                    source: match.source,
                    episodeId: match.episodeId,
                    animeTitle: match.animeTitle || '',
                    episodeTitle: match.episodeTitle || '',
                    shift: Number(match.shift) || 0
                });
            if (itemId === state.itemId) {
                applyLoadResult(response);
            }
            closeMatchDialog();
            showToast('弹幕匹配已更新，共 ' + state.comments.length + ' 条');
        } catch (error) {
            status.textContent = getErrorMessage(error, '保存匹配失败。');
            button.disabled = false;
        }
    }

    function closeMatchDialog() {
        if (state.dialog) {
            state.dialog.remove();
            state.dialog = null;
        }
    }

    function showToast(message) {
        if (!message) {
            return;
        }
        const toast = document.createElement('div');
        toast.className = 'jf-danmaku-toast';
        toast.textContent = message;
        document.body.appendChild(toast);
        window.setTimeout(function () { toast.remove(); }, 3300);
    }

    function getErrorMessage(error, fallback) {
        if (!error) {
            return fallback;
        }
        if (error.responseJSON && (error.responseJSON.detail || error.responseJSON.Detail)) {
            return error.responseJSON.detail || error.responseJSON.Detail;
        }
        if (error.detail || error.Detail) {
            return error.detail || error.Detail;
        }
        return fallback;
    }

    function delay(milliseconds) {
        return new Promise(function (resolve) {
            window.setTimeout(resolve, milliseconds);
        });
    }

    async function waitForOptions() {
        let authenticatedFailures = 0;
        while (authenticatedFailures < 6) {
            const userId = typeof state.apiClient.getCurrentUserId === 'function'
                ? state.apiClient.getCurrentUserId()
                : null;
            if (!userId) {
                await delay(1000);
                continue;
            }
            try {
                return await apiRequest('GET', '/Danmaku/options');
            } catch (error) {
                authenticatedFailures += 1;
                await delay(1500);
            }
        }
        throw new Error('Danmaku options are unavailable.');
    }

    async function start() {
        injectStyles();
        try {
            state.apiClient = await waitForApiClient();
            state.options = await waitForOptions();
            state.enabled = readLocalEnabled();
            const observer = new MutationObserver(function (mutations) {
                const needsAttach = mutations.some(function (mutation) {
                    const target = mutation.target instanceof Element ? mutation.target : mutation.target.parentElement;
                    return !target?.closest('.jf-danmaku-overlay, .jf-danmaku-dialog-backdrop');
                });
                if (needsAttach) {
                    scheduleAttach();
                }
            });
            observer.observe(document.documentElement, {
                childList: true,
                subtree: true,
                attributes: true,
                attributeFilter: ['src']
            });
            window.addEventListener('resize', resetTimeline, { passive: true });
            scheduleAttach();
        } catch (error) {
            console.warn('[Danmaku] Unable to initialize the web client.', error);
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start, { once: true });
    } else {
        start();
    }
}());
