/**
 * The only module that touches chrome.storage.
 *
 * Uses storage.session, not storage.local: session is memory-backed and
 * survives service-worker restarts, which is the only durability this data
 * needs. Captured requests carry Cookie headers, and storage.local would
 * persist them unencrypted on disk across browser restarts indefinitely.
 */

const RECENT_KEY = 'recent_streams';
const VARIANTS_CACHE_PREFIX = 'variants_';
const DISMISSED_PREFIX = 'dismissed_';
const FAILURE_TTL_MS = 30 * 1000; // 30 seconds for failed probes
const SUCCESS_TTL_MS = 60 * 60 * 1000; // 1 hour for successful probes
const MAX_PER_TAB = 25;
const MAX_RECENT = 30;
const MAX_DISMISSED_PER_TAB = 200;

const tabKeyFor = (tabId) => `tab_${tabId}`;
const dismissedKeyFor = (tabId) => `${DISMISSED_PREFIX}${tabId}`;

/** Kinds that are a download target in their own right. A tab holding one of
 *  these is watching a stream, so anything media-shaped alongside it is one of
 *  that stream's segments — not a separate video. Abyss is excluded: it is a
 *  player page, which says nothing about what else on the tab is a segment. */
const MANIFEST_KINDS = new Set(['HLS', 'DASH', 'MSS']);

const isManifest = (item) => item && MANIFEST_KINDS.has(item.kind);

// Every mutation runs through this chain, so concurrent detections cannot
// read-modify-write over each other. A rejection must not poison the chain.
let writeChain = Promise.resolve();

function serialize(task) {
  const result = writeChain.then(task, task);
  writeChain = result.then(
    () => undefined,
    () => undefined
  );
  return result;
}

/**
 * Returns the set of dismissed URLs for a tab.
 */
export async function getDismissed(tabId) {
  if (!tabId || tabId <= 0) return new Set();
  const key = dismissedKeyFor(tabId);
  const data = await chrome.storage.session.get([key]);
  return new Set(data[key] || []);
}

/**
 * Dismisses a set of URLs for a specific tab so they do not immediately
 * reappear on subsequent requests while playback continues.
 * Also removes the dismissed streams from the active tab list and recent list.
 */
export function dismissStreams(tabId, urls) {
  if (!tabId || tabId <= 0 || !Array.isArray(urls) || urls.length === 0) {
    return Promise.resolve();
  }
  return serialize(async () => {
    const key = dismissedKeyFor(tabId);
    const tabKey = tabKeyFor(tabId);
    const data = await chrome.storage.session.get([key, tabKey, RECENT_KEY]);

    const existing = data[key] || [];
    const set = new Set(existing);
    const urlSet = new Set();
    for (const u of urls) {
      if (u) {
        set.add(u);
        urlSet.add(u);
      }
    }
    let array = Array.from(set);
    if (array.length > MAX_DISMISSED_PER_TAB) {
      array = array.slice(-MAX_DISMISSED_PER_TAB);
    }

    const currentTabList = data[tabKey] || [];
    const updatedTabList = currentTabList.filter((s) => !urlSet.has(s.url));

    const currentRecent = data[RECENT_KEY] || [];
    const updatedRecent = currentRecent.filter((s) => !(s.tabId === tabId && urlSet.has(s.url)));

    await chrome.storage.session.set({
      [key]: array,
      [tabKey]: updatedTabList,
      [RECENT_KEY]: updatedRecent
    });
  });
}

/**
 * Records a stream against its tab and in the global recent list.
 * Returns the new length of the tab's list, for the badge.
 */
export function addStream(tabId, item) {
  return serialize(async () => {
    const effectiveTabId = tabId && tabId > 0 ? tabId : null;
    const dismissedKey = effectiveTabId ? dismissedKeyFor(effectiveTabId) : null;
    const keys = effectiveTabId
      ? [tabKeyFor(effectiveTabId), RECENT_KEY, dismissedKey]
      : [RECENT_KEY];

    // Scoped read: pulling the whole area back on every detection is O(all tabs).
    const data = await chrome.storage.session.get(keys);
    const patch = {};
    let tabCount = 0;

    if (effectiveTabId) {
      const dismissed = new Set(data[dismissedKey] || []);
      if (dismissed.has(item.url)) {
        const currentList = data[tabKeyFor(effectiveTabId)] || [];
        return currentList.length;
      }

      const key = tabKeyFor(effectiveTabId);
      let list = data[key] || [];

      const incomingIsManifest = isManifest(item);
      const listHasManifest = list.some(isManifest);

      if (!incomingIsManifest && listHasManifest) {
        // A manifest is already the download target for this tab; this is one
        // of its segments. Dropping it is what keeps the manifest visible.
        return list.length;
      }

      if (incomingIsManifest && !listHasManifest) {
        // First manifest for the tab — evict segments captured before it.
        list = list.filter((s) => s.kind !== 'Media' && s.kind !== 'Audio');
      }

      if (!list.some((s) => s.url === item.url)) {
        list.unshift(item);
        if (list.length > MAX_PER_TAB) list.length = MAX_PER_TAB;
      }

      patch[key] = list;
      tabCount = list.length;
    }

    let recent = data[RECENT_KEY] || [];
    const incomingIsManifest = isManifest(item);

    if (effectiveTabId) {
      const dismissed = new Set(data[dismissedKey] || []);
      if (dismissed.has(item.url)) {
        return tabCount;
      }
    }

    // M3: When a manifest arrives on a tab, purge earlier media segments from that tab in recent_streams
    if (incomingIsManifest && effectiveTabId) {
      recent = recent.filter((s) => !(s.tabId === effectiveTabId && (s.kind === 'Media' || s.kind === 'Audio')));
    }

    if (!recent.some((s) => s.url === item.url)) {
      recent.unshift(item);
      if (recent.length > MAX_RECENT) recent.length = MAX_RECENT;
    }

    patch[RECENT_KEY] = recent;

    if (Object.keys(patch).length > 0) {
      await chrome.storage.session.set(patch);
    }

    return tabCount;
  });
}

export async function getTabStreams(tabId) {
  if (!tabId || tabId <= 0) return [];
  const key = tabKeyFor(tabId);
  const data = await chrome.storage.session.get([key]);
  return data[key] || [];
}

export async function getRecentStreams() {
  const data = await chrome.storage.session.get([RECENT_KEY]);
  return data[RECENT_KEY] || [];
}

export function clearTab(tabId) {
  if (!tabId || tabId <= 0) return Promise.resolve();
  return serialize(() => chrome.storage.session.remove([tabKeyFor(tabId), dismissedKeyFor(tabId)]));
}

/**
 * Retrieves cached probe variants for a manifest URL, respecting TTL.
 */
export async function getCachedVariants(url, now = Date.now()) {
  if (!url) return null;
  const key = `${VARIANTS_CACHE_PREFIX}${url}`;
  const data = await chrome.storage.session.get([key]);
  const entry = data[key];
  if (!entry) return null;

  const ttl = entry.error ? FAILURE_TTL_MS : SUCCESS_TTL_MS;
  if (now - (entry.timestamp || 0) > ttl) {
    return null;
  }

  return { variants: entry.variants || [], error: entry.error || null };
}

/**
 * Caches probe variants for a manifest URL in session storage.
 */
export function setCachedVariants(url, result, now = Date.now()) {
  if (!url || !result) return Promise.resolve();
  const key = `${VARIANTS_CACHE_PREFIX}${url}`;
  const entry = {
    variants: result.variants || [],
    error: result.error || null,
    timestamp: now
  };
  return serialize(async () => {
    await chrome.storage.session.set({ [key]: entry });
  });
}

/**
 * Clears all tab streams, recent streams list, and variant caches in a serialized transaction.
 */
export function clearAll() {
  return serialize(async () => {
    const all = await chrome.storage.session.get(null);
    const keys = Object.keys(all).filter(
      (k) =>
        k.startsWith('tab_') ||
        k.startsWith(VARIANTS_CACHE_PREFIX) ||
        k === RECENT_KEY
    );
    if (keys.length > 0) {
      await chrome.storage.session.remove(keys);
    }
  });
}

/**
 * Drops per-tab lists and dismissed sets whose tab no longer exists.
 * onRemoved only fires while the service worker is awake, so tabs closed
 * during an idle period leak keys.
 * Called from the popup, which is the one moment the full key list matters.
 */
export function sweepOrphanTabs(liveTabIds) {
  return serialize(async () => {
    const liveTabKeys = new Set(liveTabIds.map((id) => tabKeyFor(id)));
    const liveDismissedKeys = new Set(liveTabIds.map((id) => dismissedKeyFor(id)));
    const all = await chrome.storage.session.get(null);
    const stale = Object.keys(all).filter(
      (key) =>
        (key.startsWith('tab_') && !liveTabKeys.has(key)) ||
        (key.startsWith(DISMISSED_PREFIX) && !liveDismissedKeys.has(key))
    );
    if (stale.length > 0) {
      await chrome.storage.session.remove(stale);
    }
    return stale.length;
  });
}
