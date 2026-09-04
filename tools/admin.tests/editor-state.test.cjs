const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname, '../admin/app.js'), 'utf8');
function section(start, end) {
  const first = source.indexOf(start);
  const last = source.indexOf(end, first + start.length);
  assert(first >= 0 && last > first);
  return source.slice(first, last);
}
function run(context, start, end) {
  vm.runInContext(section(start, end), context);
}
function deferred() {
  let resolve;
  const promise = new Promise(done => { resolve = done; });
  return { promise, resolve };
}
class Element {
  constructor() { this.children = []; this.dataset = {}; this.listeners = {}; }
  replaceChildren(...children) { this.children = children; }
  append(...children) { this.children.push(...children); }
  querySelector() { return new Element(); }
  addEventListener(name, callback) { this.listeners[name] = callback; }
}
function documentStub() {
  const nodes = {};
  return {
    getElementById(id) {
      nodes[id] ||= new Element();
      if (id === 'statTemplate') nodes[id].content = { cloneNode: () => new Element() };
      return nodes[id];
    },
    createElement: () => new Element(),
    querySelectorAll: () => nodes.roleNameSettings.children.map(row => row.children[1])
  };
}

for (const [kind, start, end, method, modelName, dirtyName] of [
  ['catalog', 'async function _saveCatalogData(', 'async function saveConfiguration(', '_saveCatalogData', 'model', 'dirty'],
  ['profile', 'async function saveSystemProfile(', 'function resetSystemProfile(', 'saveSystemProfile', 'systemProfile', 'profileDirty'],
  ['overlay', 'async function saveOverlayConfig(', 'function switchView(', 'saveOverlayConfig', 'overlayConfig', 'overlayDirty']
]) {
  for (const editDuringSave of [true, false]) test(`${kind} save ${editDuringSave ? 'preserves newer edits' : 'marks its submitted edits clean'}`, async () => {
    const pending = deferred();
    let persisted;
    const context = vm.createContext({
      document: documentStub(), saveButton: {}, dirty: false, profileDirty: false, overlayDirty: false,
      profilesData: { activeProfileId: 'first' },
      serializeModel: () => context.model,
      fetch: async (url, options) => {
        if (options?.method === 'POST') {
          persisted = JSON.parse(options.body);
          await pending.promise;
          return { ok: true, json: async () => ({ ok: true }) };
        }
        return { ok: false };
      },
      markClean: () => { context.dirty = false; },
      renderAll() {}, updateTopbarSave() {}, updateProfileStatus() {}, showNotice() {},
      validateProfileClient: () => [], refreshBackupIndex: async () => {}
    });
    context[modelName] = { name: 'submitted' };
    context[dirtyName] = true;
    run(context, start, end);
    const saving = vm.runInContext(`${method}()`, context);
    if (editDuringSave) context[modelName].name = 'newer';
    pending.resolve();
    await saving;
    assert.equal((persisted.config || persisted).name, 'submitted');
    assert.equal(context[dirtyName], editDuringSave);
    assert.equal(context[modelName].name, editDuringSave ? 'newer' : 'submitted');
    if (kind === 'overlay') {
      assert.equal(context.document.getElementById('saveOverlayButton').disabled, !editDuringSave);
      if (editDuringSave) assert.equal(context.document.getElementById('overlayPreviewFrame').src, undefined,
        'Do not replace the preview of newer edits with the older saved configuration');
    }
  });
}

function roleContext() {
  const context = vm.createContext({
    document: documentStub(), roleNamesDirty: false,
    roleAwards: { roleNames: { example: 'Original' }, awards: [] }, collections: [],
    element: () => new Element(), renderRoleAwardList() {}, renderOverview() {},
    showNotice() {}, refreshBackupIndex: async () => {}
  });
  run(context, 'function renderRoleAwards(', 'function renderRoleAwardList(');
  vm.runInContext('renderRoleAwards()', context);
  return context;
}
test('role refresh preserves unsaved input text and the focused input element', () => {
  const context = roleContext();
  const input = context.document.querySelectorAll()[0];
  input.value = 'Edited';
  input.listeners.input?.();
  context.roleAwards.roleNames.example = 'Server value';
  vm.runInContext('renderRoleAwards()', context);
  assert.equal(context.document.querySelectorAll()[0].value, 'Edited');
  assert.equal(context.document.querySelectorAll()[0], input);
  assert.equal(context.roleNamesDirty, true);
});
test('role save preserves a further edit made while the request is pending', async () => {
  const context = roleContext();
  const input = context.document.querySelectorAll()[0];
  input.value = 'Submitted';
  input.listeners.input?.();
  const pending = deferred();
  context.fetch = async (url, options) => {
    if (options?.method === 'POST') {
      await pending.promise;
      return { ok: true, json: async () => ({ ok: true }) };
    }
    return { ok: true, json: async () => ({ roleNames: { example: 'Submitted' }, awards: [] }) };
  };
  run(context, 'async function refreshRoleAwards(', 'async function setRoleAssigned(');
  run(context, 'async function saveRoleNames(', 'function simulationModel(');
  const saving = vm.runInContext('saveRoleNames()', context);
  input.value = 'Newer';
  input.listeners.input?.();
  pending.resolve();
  await saving;
  assert.equal(context.document.querySelectorAll()[0].value, 'Newer');
  assert.equal(context.roleNamesDirty, true);
});

for (const flag of ['overlayDirty', 'roleNamesDirty']) {
  test(`refresh asks before discarding ${flag}`, async () => {
    let prompted = false;
    const context = vm.createContext({ dirty: false, profileDirty: false, overlayDirty: false, roleNamesDirty: false,
      window: { confirm: () => { prompted = true; return false; } }, clearNotice() {},
      fetch: () => { throw new Error('Refresh must not load after discard is declined'); }
    });
    context[flag] = true;
    run(context, 'async function loadConfiguration(', 'async function refreshOperationalData(');
    assert.equal(await vm.runInContext('loadConfiguration()', context), false);
    assert.equal(prompted, true);
  });
  test(`closing warns about ${flag}`, () => {
    let listener, prevented = false;
    const context = vm.createContext({ dirty: false, profileDirty: false, overlayDirty: false, roleNamesDirty: false,
      window: { addEventListener: (name, callback) => { listener = callback; } }
    });
    context[flag] = true;
    run(context, 'window.addEventListener("beforeunload",', 'window.setInterval(() =>');
    listener({ preventDefault: () => { prevented = true; } });
    assert.equal(prevented, true);
  });
}

test('Rate Lab excludes empty tiers and retains equal collection odds', () => {
  const context = vm.createContext({
    collections: [
      { key: 'a', value: { parts: [{ name: 'One', tier: 'common' }], tiers: [{ id: 'common', weight: 1 }, { id: 'empty', weight: 9 }] } },
      { key: 'b', value: { parts: [{ name: 'Two' }] } }
    ],
    effectiveRates: () => ({ rows: [{ key: 'a', name: 'A', percent: 50 }, { key: 'b', name: 'B', percent: 50 }] })
  });
  run(context, 'function simulationModel(', 'function formatOneIn(');
  const model = vm.runInContext('simulationModel()', context);
  assert.equal(model.parts[0].probability, 0.5);
  assert.equal(model.parts[1].probability, 0.5);
});
