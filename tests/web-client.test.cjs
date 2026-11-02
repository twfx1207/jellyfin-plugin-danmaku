const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname, '../Web/client.js'), 'utf8');

function createClient(ajax) {
    const window = {
        setTimeout: (callback, milliseconds) => setTimeout(callback, milliseconds === 35000 ? 30 : milliseconds),
        clearTimeout
    };
    const context = vm.createContext({
        window,
        document: { readyState: 'loading', addEventListener() {} },
        console,
        Error,
        SyntaxError
    });
    const instrumented = source.replace(
        "    if (document.readyState === 'loading') {",
        "    window.testClient = { apiRequest, getErrorMessage, state };\n    if (document.readyState === 'loading') {"
    );
    vm.runInContext(instrumented, context);
    const client = window.testClient;
    client.state.apiClient = { ajax, getUrl: value => value };
    return client;
}

test('successful JSON response is returned unchanged', async () => {
    const expected = { matches: [{ episodeId: '1' }] };
    const client = createClient(async () => expected);
    assert.equal(await client.apiRequest('GET', '/Danmaku/test'), expected);
});

test('Fetch problem response displays the upstream timeout detail', async () => {
    const client = createClient(async () => {
        throw { status: 502, json: async () => ({ detail: '弹弹play请求超时：/api/v2/search/episodes' }) };
    });
    await assert.rejects(client.apiRequest('GET', '/Danmaku/test'), /弹弹play请求超时/);
});

test('a stalled response stops waiting and produces an actionable error', async () => {
    const client = createClient(() => new Promise(() => {}));
    await assert.rejects(client.apiRequest('GET', '/Danmaku/test'), /弹幕请求超时/);
});

test('a stalled error body is also bounded by the request deadline', async () => {
    const client = createClient(async () => {
        throw { status: 502, json: () => new Promise(() => {}) };
    });
    await assert.rejects(client.apiRequest('GET', '/Danmaku/test'), /弹幕请求超时/);
});

test('non-JSON proxy errors retain the fallback error handling', async () => {
    const response = { status: 500, json: async () => { throw new SyntaxError('HTML'); } };
    const client = createClient(async () => { throw response; });
    await assert.rejects(client.apiRequest('GET', '/Danmaku/test'), error => error === response);
    assert.equal(client.getErrorMessage(response, '搜索失败'), '搜索失败');
});
