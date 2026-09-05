const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname, '../admin/app.js'), 'utf8');
test('creating a game opens the stream setup guide without claiming the channel is live', async () => {
  let destination, notice, guideOpened = false;
  const context = vm.createContext({
    document: { getElementById: () => ({}) },
    wizardSetError() {}, buildWizardProfile: () => ({}), buildWizardConfiguration: () => ({}),
    fetch: async () => ({ ok: true, json: async () => ({}) }),
    closeFirstRunWizard() {}, loadConfiguration: async () => {},
    openStreamSetupGuide() { guideOpened = true; },
    switchView: view => { destination = view; }, showNotice: text => { notice = text; }
  });
  run(context, 'async function completeFirstRun(', 'function normalizeModel(');
  await vm.runInContext('completeFirstRun()', context);
  assert.equal(destination, 'overview');
  assert.equal(guideOpened, true);
  assert.match(notice, /Get ready to stream/);
});

function guideContext(storage) {
  const guide = { open: false, addEventListener(type, listener) { this[type] = listener; } };
  const context = vm.createContext({ document: { getElementById: () => guide }, localStorage: storage });
  run(context, 'function saveStreamSetupGuidePreference(', 'async function completeFirstRun(');
  vm.runInContext('initializeStreamSetupGuide()', context);
  return { context, guide };
}

test('stream setup is collapsed on upgrade and remembers opening and closing across restarts', () => {
  const values = new Map();
  const storage = { getItem: key => values.get(key), setItem: (key, value) => values.set(key, value) };
  let current = guideContext(storage);
  assert.equal(current.guide.open, false);
  current.guide.open = true;
  current.guide.toggle();
  current = guideContext(storage);
  assert.equal(current.guide.open, true);
  current.guide.open = false;
  current.guide.toggle();
  current = guideContext(storage);
  assert.equal(current.guide.open, false);
  vm.runInContext('openStreamSetupGuide()', current.context);
  assert.equal(current.guide.open, true, 'successful first-run overrides a previous collapsed preference');
  assert.equal(guideContext(storage).guide.open, true);
});

test('stream setup remains usable when reading or saving preferences fails', () => {
  const storage = { getItem() { throw new Error('blocked'); }, setItem() { throw new Error('quota'); } };
  const { context, guide } = guideContext(storage);
  assert.equal(guide.open, false);
  vm.runInContext('openStreamSetupGuide()', context);
  assert.equal(guide.open, true);
  guide.open = false;
  assert.doesNotThrow(() => guide.toggle());
});

test('failed first-run creation leaves the setup guide preference alone', async () => {
  let error, guideOpened = false;
  const context = vm.createContext({
    document: { getElementById: () => ({}) },
    wizardSetError(message) { error = message; },
    buildWizardProfile: () => ({}), buildWizardConfiguration: () => ({}),
    fetch: async () => ({ ok: false, json: async () => ({ errors: ['Cannot create game'] }) }),
    openStreamSetupGuide() { guideOpened = true; }
  });
  run(context, 'async function completeFirstRun(', 'function normalizeModel(');
  await vm.runInContext('completeFirstRun()', context);
  assert.equal(guideOpened, false);
  assert.equal(error, 'Cannot create game');
});
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

for (const flag of ['overlayDirty', 'roleNamesDirty', 'achievementsDirty']) {
  test(`refresh asks before discarding ${flag}`, async () => {
    let prompted = false;
    const context = vm.createContext({ dirty: false, profileDirty: false, overlayDirty: false, roleNamesDirty: false, achievementsDirty: false,
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
    const context = vm.createContext({ dirty: false, profileDirty: false, overlayDirty: false, roleNamesDirty: false, achievementsDirty: false,
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

test('achievement save preserves edits made while its request is pending', async () => {
  const pending = deferred();
  let posted;
  const context = vm.createContext({
    document: documentStub(), achievementsDirty: true, achievementsSaving: false,
    achievementsData: { settings: { enabled: true, definitions: [{ id: 'first', name: 'First' }] } },
    achievementSavedIds: new Set(), profilesData: { activeProfileId: 'one' },
    validateAchievementSettings: () => [], updateAchievementStatus() {}, renderAchievements() {}, showNotice() {}, refreshAchievementAnalytics: async () => {},
    fetch: async (_, options) => { posted = JSON.parse(options.body); await pending.promise; return { ok: true, json: async () => ({ ok: true }) }; }
  });
  run(context, 'async function saveAchievements(', 'function renderViewerAchievements(');
  const saving = vm.runInContext('saveAchievements()', context);
  context.achievementsData.settings.definitions[0].name = 'Newer';
  pending.resolve();
  await saving;
  assert.equal(posted.definitions[0].name, 'First');
  assert.equal(context.achievementsData.settings.definitions[0].name, 'Newer');
  assert.equal(context.achievementsDirty, true);
  assert.equal(context.achievementSavedIds.has('first'), true);
});

test('achievement builder rejects duplicate IDs and invalid percent targets or rewards', () => {
  const context = vm.createContext({});
  run(context, 'function validateAchievementSettings(', 'function updateAchievementStatus(');
  const valid = { defaultReward: 0, definitions: [{ id: 'first', name: 'First', description: '', metric: 'collectionPercent', collectionKey: 'a', threshold: 50, reward: 10 }] };
  context.settings = valid;
  assert.equal(vm.runInContext('validateAchievementSettings(settings).length', context), 0);
  valid.definitions.push({ ...valid.definitions[0], threshold: 101, reward: -1 });
  const errors = vm.runInContext('validateAchievementSettings(settings)', context).join(' ');
  assert.match(errors, /unique/i);
  assert.match(errors, /100/);
  assert.match(errors, /reward/i);
});

test('profile switching waits for a pending achievement save', async () => {
  let requests = 0;
  const context = vm.createContext({ pendingSwitchId: 'next', achievementsSaving: true, showNotice() {}, closeProfileSwitchConfirm() {}, fetch: async () => { requests++; throw new Error('Must not switch yet'); } });
  run(context, 'async function confirmProfileSwitch(', 'async function toggleProfileLive(');
  await vm.runInContext('confirmProfileSwitch()', context);
  assert.equal(requests, 0);
});

test('achievement save cannot mark a refreshed editor clean', async () => {
  const pending = deferred();
  const context = vm.createContext({ achievementsDirty: true, achievementsSaving: false,
    achievementsData: { settings: { definitions: [{ id: 'first' }] } }, achievementSavedIds: new Set(), profilesData: { activeProfileId: 'one' },
    validateAchievementSettings: () => [], updateAchievementStatus() {}, renderAchievements() {}, showNotice() {}, refreshAchievementAnalytics: async () => {},
    fetch: async () => { await pending.promise; return { ok: true, json: async () => ({ ok: true }) }; }
  });
  run(context, 'async function saveAchievements(', 'function renderViewerAchievements(');
  const saving = vm.runInContext('saveAchievements()', context);
  context.achievementsData.settings = { definitions: [{ id: 'first' }] };
  pending.resolve();
  await saving;
  assert.equal(context.achievementsDirty, true);
  assert.equal(context.achievementSavedIds.size, 0);
});

test('overlay achievement stack bounds visible awards and clears on the next ordinary pull', () => {
  const overlaySource = fs.readFileSync(path.join(__dirname, '../../overlays/lower-quarter/overlay.js'), 'utf8');
  const panel = new Element();
  const context = vm.createContext({ achievementStack: panel, document: { createElement: () => new Element() } });
  const first = overlaySource.indexOf('function renderAchievementStack(');
  assert(first >= 0, 'Overlay needs an achievement stack');
  vm.runInContext(overlaySource.slice(first, overlaySource.indexOf('function renderState(', first)), context);
  context.awards = Array.from({ length: 20 }, (_, index) => ({ name: index === 0 ? '<img src=x onerror=alert(1)>' : `Badge ${index}`, description: 'Goal complete', reward: 5 }));
  vm.runInContext('renderAchievementStack(awards)', context);
  assert.equal(panel.children.length, 4, 'Three visible awards plus an overflow count');
  assert.equal(panel.children[0].children[0].textContent, '<img src=x onerror=alert(1)>');
  assert.match(panel.children[3].textContent, /17/);
  vm.runInContext('renderAchievementStack([])', context);
  assert.equal(panel.children.length, 0);
  assert.equal(panel.hidden, true);
});

test('saving achievements refreshes viewer awards and rewards without clearing other editors', async () => {
  let rendered;
  const context = vm.createContext({ achievementsDirty: true, achievementsSaving: false,
    dirty: true, profileDirty: true, overlayDirty: true, roleNamesDirty: true,
    achievementsData: { settings: { definitions: [{ id: 'first' }] } }, achievementSavedIds: new Set(), profilesData: { activeProfileId: 'one' },
    analytics: { viewers: [{ id: 'viewer', scrap: 0, achievements: [] }] }, selectedViewerId: 'viewer',
    validateAchievementSettings: () => [], updateAchievementStatus() {}, renderAchievements() {}, showNotice() {}, renderOverview() {}, renderEconomy() {},
    renderViewerInspector: () => { rendered = context.analytics.viewers[0]; },
    fetch: async url => ({ ok: true, json: async () => url === '/api/analytics'
      ? { viewers: [{ id: 'viewer', scrap: 25, achievements: [{ id: 'first', earned: true }] }] }
      : { ok: true } })
  });
  run(context, 'async function refreshAchievementAnalytics(', 'function renderViewerAchievements(');
  await vm.runInContext('saveAchievements()', context);
  assert.equal(rendered.scrap, 25);
  assert.equal(rendered.achievements[0].earned, true);
  for (const flag of ['dirty', 'profileDirty', 'overlayDirty', 'roleNamesDirty']) assert.equal(context[flag], true);
  assert.equal(context.achievementsDirty, false);
});

for (const change of ['profile', 'editor']) test(`late achievement analytics cannot replace a changed ${change}`, async () => {
  const pending = deferred();
  const original = { viewers: [{ id: 'current' }] };
  const context = vm.createContext({ analytics: original, profilesData: { activeProfileId: 'one' }, achievementsData: { settings: {} },
    fetch: async () => ({ ok: true, json: async () => { await pending.promise; return { viewers: [{ id: 'stale' }] }; } }),
    renderViewerInspector() { throw new Error('Must not render stale data'); }
  });
  run(context, 'async function refreshAchievementAnalytics(', 'async function saveAchievements(');
  const refreshing = vm.runInContext('refreshAchievementAnalytics(profilesData.activeProfileId, achievementsData.settings)', context);
  if (change === 'profile') context.profilesData.activeProfileId = 'two';
  else context.achievementsData.settings = {};
  pending.resolve();
  await refreshing;
  assert.equal(context.analytics, original);
});

test('viewer achievements start collapsed with an earned count and keep all progress available', () => {
  const detail = new Element();
  const context = vm.createContext({ viewerAchievementsOpen: false, systemProfile: { currencyName: 'Points' },
    element: (tag, className, text) => Object.assign(new Element(), { tagName: tag, className, textContent: text, open: false }),
    viewer: { achievements: [{ name: 'In progress', current: 2, target: 10 }, { name: 'Earned badge', earned: true }] }, detail
  });
  run(context, 'function renderViewerAchievements(', 'function renderViewerInspector(');
  vm.runInContext('renderViewerAchievements(viewer, detail)', context);
  const section = detail.children[0];
  assert.equal(section.tagName, 'details');
  assert.equal(section.open, false);
  assert.equal(section.children[0].tagName, 'summary');
  assert.equal(section.children[0].textContent, 'Achievements · 1 earned');
  const grid = section.children.find(node => node.className === 'viewer-achievement-grid');
  assert.equal(grid.children.length, 2);
  assert.equal(grid.children[0].children[0].textContent, 'Earned badge');
  assert.equal(grid.children[1].children[0].textContent, 'In progress');
  assert.equal(context.viewer.achievements[0].name, 'In progress', 'Sorting must not mutate analytics');
  section.open = true;
  section.listeners.toggle();
  detail.replaceChildren();
  vm.runInContext('renderViewerAchievements(viewer, detail)', context);
  assert.equal(detail.children[0].open, true, 'A live refresh keeps the inspector open');
});
