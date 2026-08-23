import { test } from 'node:test';
import assert from 'node:assert/strict';
import { compareVersions, checkExtensionUpdate } from '../lib/update-check.js';

test('compareVersions handles numeric segment comparisons correctly', () => {
  assert.equal(compareVersions('1.3.0', '1.3.0'), 0);
  assert.equal(compareVersions('v1.3.0', '1.3.0'), 0);
  assert.equal(compareVersions('1.4.0', '1.3.0'), 1);
  assert.equal(compareVersions('2.0.0', '1.3.0'), 1);
  assert.equal(compareVersions('1.3.10', '1.3.9'), 1);
  assert.equal(compareVersions('1.2.9', '1.3.0'), -1);
  assert.equal(compareVersions('1.3.0', '1.3.1'), -1);
});

test('checkExtensionUpdate reports update available when newer release exists', async () => {
  const stubFetch = async () => ({
    headers: {
      get: (key) => (key.toLowerCase() === 'location' ? 'https://github.com/naravid19/N_m3u8DL_RE_GUI/releases/tag/v1.4.0' : null)
    }
  });

  const result = await checkExtensionUpdate('1.3.0', stubFetch);
  assert.equal(result.hasUpdate, true);
  assert.equal(result.currentVersion, '1.3.0');
  assert.equal(result.latestVersion, 'v1.4.0');
  assert.equal(result.releaseUrl, 'https://github.com/naravid19/N_m3u8DL_RE_GUI/releases/tag/v1.4.0');
});

test('checkExtensionUpdate reports no update when version matches', async () => {
  const stubFetch = async () => ({
    headers: {
      get: (key) => (key.toLowerCase() === 'location' ? 'https://github.com/naravid19/N_m3u8DL_RE_GUI/releases/tag/v1.3.0' : null)
    }
  });

  const result = await checkExtensionUpdate('1.3.0', stubFetch);
  assert.equal(result.hasUpdate, false);
});

test('checkExtensionUpdate handles network failure gracefully without throwing', async () => {
  const failingFetch = async () => {
    throw new Error('Network offline');
  };

  const result = await checkExtensionUpdate('1.3.0', failingFetch);
  assert.equal(result.hasUpdate, false);
  assert.equal(result.latestVersion, '');
});

test('checkExtensionUpdate handles missing location header', async () => {
  const noLocationFetch = async () => ({
    headers: {
      get: () => null
    }
  });

  const result = await checkExtensionUpdate('1.3.0', noLocationFetch);
  assert.equal(result.hasUpdate, false);
});

