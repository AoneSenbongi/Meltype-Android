// SPDX-License-Identifier: GPL-3.0-or-later
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { releaseMessage, sendRelease } from './send-release-notification.mjs';

function event() {
  return { action: 'published', repository: { id: 1407496285, full_name: 'AoneSenbongi/Meltype-Android' },
    release: { draft: false, prerelease: false, tag_name: 'android-1.2.2',
      assets: [{ name: 'Meltype-Android-1.2.2.apk', state: 'uploaded', size: 100,
        digest: 'sha256:' + 'a'.repeat(64),
        browser_download_url: 'https://github.com/AoneSenbongi/Meltype-Android/releases/download/android-1.2.2/Meltype-Android-1.2.2.apk' }] } };
}
test('published stable release becomes data-only update message', () => {
  assert.deepEqual(releaseMessage(event()).message.data, { type: 'meltype.release', version: '1.2.2' });
  assert.equal(releaseMessage(event()).message.topic, 'meltype-android-releases');
  assert.equal(releaseMessage(event()).message.notification, undefined);
});
for (const [name, mutate] of [
  ['other event', e => { e.action = 'edited'; }],
  ['other owner', e => { e.repository.full_name = 'other/Meltype-Android'; }],
  ['reused repo name', e => { e.repository.id = 1; }],
  ['draft', e => { e.release.draft = true; }],
  ['prerelease', e => { e.release.prerelease = true; }],
  ['candidate tag', e => { e.release.tag_name = 'android-1.2.2-rc.1'; }],
  ['newline tag', e => { e.release.tag_name = 'android-1.2.2\n'; }],
  ['non-text tag', e => { e.release.tag_name = ['android-1.2.2']; }],
  ['overflow version', e => { e.release.tag_name = 'android-999999999999.2.2'; }],
  ['wrong download host', e => { e.release.assets[0].browser_download_url = 'https://example.org/update.apk'; }],
  ['missing digest', e => { delete e.release.assets[0].digest; }],
  ['unfinished upload', e => { e.release.assets[0].state = 'new'; }],
  ['missing normal APK', e => { e.release.assets[0].name = 'Meltype-Android-learning-1.2.2.apk'; }],
  ['oversized APK', e => { e.release.assets[0].size = 536870913; }],
]) test(`ignore ${name}`, () => { const e = event(); mutate(e); assert.equal(releaseMessage(e), null); });
test('sending uses only the configured FCM project and never exposes an APK URL in data', async () => {
  let request;
  const result = await sendRelease(event(), 'test-only', async (url, init) => {
    request = { url, init };
    return { ok: true, json: async () => ({ name: 'projects/meltype-android-updates/messages/test' }) };
  });
  assert.equal(result.sent, true);
  assert.equal(request.url, 'https://fcm.googleapis.com/v1/projects/meltype-android-updates/messages:send');
  assert.equal(request.init.headers.Authorization, 'Bearer test-only');
  assert.deepEqual(JSON.parse(request.init.body).message.data, { type: 'meltype.release', version: '1.2.2' });
});
test('irrelevant events do not make requests', async () => {
  const e = event(); e.action = 'deleted';
  assert.deepEqual(await sendRelease(e, undefined, () => { throw new Error('Unexpected request'); }), { skipped: true });
});
test('failed sends fail visibly without leaking credentials', async () => {
  await assert.rejects(sendRelease(event(), 'private-token', async () => ({ ok: false, status: 403 })), /HTTP 403/);
  await assert.rejects(sendRelease(event(), undefined), /access token is missing/);
});
