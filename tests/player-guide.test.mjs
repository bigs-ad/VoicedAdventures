import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

test('player guide and package use explicit Chinese client names', () => {
  const root = new URL('../', import.meta.url);
  const readme = readFileSync(new URL('README.md', root), 'utf8');
  assert.doesNotMatch(readme, /Turtle|现代客户端/);
  const guide = readFileSync(new URL('使用说明.txt', root), 'utf8');
  for (const name of ['正式服与无限服', '水豚服', 'VoicedAdventures.exe', '腾讯云', '千问']) {
    assert.ok(guide.includes(name), name);
  }
  assert.doesNotMatch(guide, /Turtle|Modern|现代客户端/);
  const packaging = readFileSync(new URL('scripts/package.ps1', root), 'utf8');
  assert.ok(packaging.includes("Add-ReleaseFile '使用说明.txt' '使用说明.txt'"));
  assert.ok(packaging.includes("'正式服与无限服'"));
  assert.ok(packaging.includes("'水豚服'"));
});
