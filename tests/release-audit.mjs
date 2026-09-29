import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { dirname, extname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
function files(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    const path = join(directory, entry.name);
    return entry.isDirectory() ? files(path) : [path];
  });
}
const sources = files(join(root, 'experiments'));
const textSources = sources.filter(path => ['.cs', '.lua', '.toc', '.json'].includes(extname(path)));

test('runtime export has no recording-pack implementation or coverage UI', () => {
  const forbidden = /LocalVoicePack|PersonalVoicePack|LocalPackException|HybridPlayback|PackCoverage|QVRNPCCoverage|未收录/;
  for (const path of textSources.filter(path => !/tests?\.(?:cs|lua)$/i.test(path))) {
    assert.ok(!forbidden.test(readFileSync(path, 'utf8')), relative(root, path));
  }
  for (const path of sources) {
    assert.doesNotMatch(relative(root, path), /(?:LocalVoicePack|PersonalVoicePack|HybridPlayback|pack-coverage)/i);
    assert.ok(!['.wav', '.mp3', '.ogg', '.qvp', '.pcm'].includes(extname(path)), `recording asset: ${path}`);
  }
});

test('export contains no private credentials, cloned voice IDs or machine paths', () => {
  const forbidden = /-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|\b(?:ghp|gho|github_pat)_[A-Za-z0-9_]{20,}|\bAKID[A-Za-z0-9]{24,}|\bsk-[a-zA-Z0-9]{24,}|C:[\\/]Users[\\/]24393|D:[\\/]Game[\\/]|qwen-tts-(?:vc|vd)-[A-Za-z0-9_-]{20,}/;
  for (const path of textSources) {
    assert.ok(!forbidden.test(readFileSync(path, 'utf8')), relative(root, path));
  }
});

test('addon redistribution placeholders do not include imported lookup data', () => {
  for (const name of ['NpcRaceFallback.lua', 'QuestSpeakers.lua', 'IdentityModels.lua']) {
    const path = join(root, 'experiments/file-read-egress/addon/VoicedAdventures', name);
    const content = readFileSync(path, 'utf8');
    assert.ok(content.length < 1500, `${name} must remain a small compatibility placeholder`);
    assert.doesNotMatch(content, /\[\s*\d{2,}\s*\]\s*=/, `${name} contains imported IDs`);
  }
});
