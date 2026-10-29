// SPDX-License-Identifier: GPL-3.0-or-later
import { readFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';

export function releaseMessage(event) {
  if (event.action !== 'published' || event.repository?.full_name !== 'AoneSenbongi/Meltype-Android' ||
      event.repository?.id !== 1407496285) return null;
  const release = event.release;
  if (!release || typeof release.tag_name !== 'string' || release.draft !== false || release.prerelease !== false ||
      !/^android-[0-9]+\.[0-9]+\.[0-9]+$/.test(release.tag_name)) return null;
  const version = release.tag_name.slice(8);
  if (version.length > 32 || version.trim() !== version || version.split('.').some(part => Number(part) > 2147483647)) return null;
  const name = `Meltype-Android-${version}.apk`;
  const asset = release.assets?.find(item => item.name === name);
  if (!asset || asset.state !== 'uploaded' || !Number.isSafeInteger(asset.size) || asset.size <= 0 || asset.size > 536870912 ||
      !/^sha256:[a-fA-F0-9]{64}$/.test(asset.digest ?? '') ||
      asset.browser_download_url !== `https://github.com/AoneSenbongi/Meltype-Android/releases/download/${release.tag_name}/${name}`) return null;
  return {
    message: {
      topic: 'meltype-android-releases',
      data: { type: 'meltype.release', version },
      android: { priority: 'high', collapse_key: 'meltype-release' }
    }
  };
}

export async function sendRelease(event, accessToken, fetchImpl = fetch) {
  const message = releaseMessage(event);
  if (!message) return { skipped: true };
  if (!accessToken) throw new Error('FCM access token is missing');
  const response = await fetchImpl('https://fcm.googleapis.com/v1/projects/meltype-android-updates/messages:send', {
    method: 'POST',
    headers: { Authorization: `Bearer ${accessToken}`, 'Content-Type': 'application/json' },
    body: JSON.stringify(message),
    signal: AbortSignal.timeout(30000)
  });
  if (!response.ok) throw new Error(`FCM request failed (HTTP ${response.status})`);
  const result = await response.json();
  if (typeof result.name !== 'string' || !result.name.startsWith('projects/meltype-android-updates/messages/'))
    throw new Error('Unexpected FCM response');
  return { sent: true, messageName: result.name };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    const event = JSON.parse(await readFile(process.env.GITHUB_EVENT_PATH, 'utf8'));
    console.log(JSON.stringify(await sendRelease(event, process.env.FCM_ACCESS_TOKEN)));
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
