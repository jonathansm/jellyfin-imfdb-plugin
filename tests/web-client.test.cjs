const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');

function loadClient(pages = []) {
    const timers = new Map();
    let nextTimer = 1;
    const context = {
        console,
        Date,
        MutationObserver: class { observe() {} },
        document: {
            body: {},
            querySelectorAll: () => pages,
            getElementById: () => null
        },
        window: {
            location: { href: 'https://example.com/jellyfin/web/' },
            ApiClient: {
                accessToken: () => 'test-token',
                getUrl: path => 'https://example.com/jellyfin/' + path
            },
            history: {},
            addEventListener() {},
            setInterval() {},
            setTimeout(callback) { const id = nextTimer++; timers.set(id, callback); return id; },
            clearTimeout(id) { timers.delete(id); }
        }
    };
    let source = fs.readFileSync('Jellyfin.Plugin.Imfdb/Web/imfdbClient.js', 'utf8');
    source = source.replace(/\}\)\(\);\s*$/, 'globalThis.hooks = { getAuthHeaders, withImageAuth, findPeopleAnchor, scheduleUpdate, getItemIdFromUrl, openExternalUrl };})();');
    vm.runInNewContext(source, context);
    return { ...context, timers };
}

test('requests use Jellyfin 12 Authorization header', () => {
    const { hooks } = loadClient();
    assert.equal(hooks.getAuthHeaders().Authorization, 'MediaBrowser Token="test-token"');
    assert.equal(hooks.getAuthHeaders()['X-Emby-Token'], undefined);
});

test('cached images preserve base URL and use supported ApiKey parameter', () => {
    const { hooks } = loadClient();
    assert.equal(hooks.withImageAuth('/Imfdb/Image?itemId=1'), 'https://example.com/jellyfin/Imfdb/Image?itemId=1&ApiKey=test-token');
    assert.equal(hooks.withImageAuth('https://www.imfdb.org/images/a.jpg'), 'https://www.imfdb.org/images/a.jpg');
});

test('cast lookup uses the visible detail page, including localized or empty cast', () => {
    const anchor = {};
    const hidden = { classList: { contains: () => true }, getClientRects: () => [], querySelector: () => { throw Error('hidden page'); } };
    const visible = { classList: { contains: () => false }, getClientRects: () => [{}], querySelector: selector => { assert.ok(selector.includes('#castCollapsible')); return anchor; } };
    assert.equal(loadClient([hidden, visible]).hooks.findPeopleAnchor(), anchor);
});

test('continuous DOM mutations do not postpone a scheduled update', () => {
    const { hooks, timers } = loadClient();
    const initialTimer = [...timers.keys()][0];
    for (let i = 0; i < 100; i++) hooks.scheduleUpdate();
    assert.deepEqual([...timers.keys()], [initialTimer]);
});

test('supports hash and path detail routes', () => {
    const { hooks } = loadClient();
    const id = '123456781234123412341234567890ab';
    assert.equal(hooks.getItemIdFromUrl('#/details?id=' + id), id);
    assert.equal(hooks.getItemIdFromUrl('/details/' + id), id);
    assert.equal(hooks.getItemIdFromUrl('#/home'), null);
});

test('external links reject executable schemes', () => {
    const { hooks, window } = loadClient();
    window.open = () => { throw Error('must not open'); };
    hooks.openExternalUrl('javascript:alert(1)');
});
