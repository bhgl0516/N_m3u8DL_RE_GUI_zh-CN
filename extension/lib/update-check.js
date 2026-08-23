/**
 * Pure policy and transport functions for extension update checking.
 */

export function compareVersions(a, b) {
  const partsA = (a || "").replace(/^v/i, "").split(".").map(Number);
  const partsB = (b || "").replace(/^v/i, "").split(".").map(Number);
  for (let i = 0; i < Math.max(partsA.length, partsB.length); i++) {
    const numA = Number.isFinite(partsA[i]) ? partsA[i] : 0;
    const numB = Number.isFinite(partsB[i]) ? partsB[i] : 0;
    if (numA > numB) return 1;
    if (numA < numB) return -1;
  }
  return 0;
}

/**
 * Checks for suite/extension updates against GitHub releases.
 * @param {string} currentVersion Current version string (e.g. "1.3.0")
 * @param {Function} [fetchFn] Custom fetch function for testability
 * @returns {Promise<{ hasUpdate: boolean, currentVersion: string, latestVersion: string, releaseUrl: string }>}
 */
export async function checkExtensionUpdate(currentVersion, fetchFn = (typeof fetch !== "undefined" ? fetch : null)) {
  if (!fetchFn) {
    return { hasUpdate: false, currentVersion, latestVersion: "", releaseUrl: "" };
  }

  try {
    const response = await fetchFn("https://github.com/naravid19/N_m3u8DL_RE_GUI/releases/latest", {
      redirect: "manual"
    });

    const location = response?.headers?.get?.("location") || "";
    if (!location) {
      return { hasUpdate: false, currentVersion, latestVersion: "", releaseUrl: "" };
    }

    const match = location.match(/\/tag\/v?([0-9]+\.[0-9]+\.[0-9]+)/);
    if (!match) {
      return { hasUpdate: false, currentVersion, latestVersion: "", releaseUrl: "" };
    }

    const latestTag = match[1];
    const isNewer = compareVersions(latestTag, currentVersion) > 0;

    return {
      hasUpdate: isNewer,
      currentVersion,
      latestVersion: `v${latestTag}`,
      releaseUrl: location
    };
  } catch {
    return { hasUpdate: false, currentVersion, latestVersion: "", releaseUrl: "" };
  }
}

