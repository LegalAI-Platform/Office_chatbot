// Swaps credential references in n8n workflow JSON files.
//   to-ids   <creds.json> <src_dir> <out_dir>  : "CRED:<name>" -> real credential id (for import)
//   to-names <creds.json> <src_dir> <out_dir>  : real credential id -> "CRED:<name>" (for export)
// The repository only stores CRED:<name> placeholders, so it never depends on the ids
// of one particular n8n instance. Credential data is never read or written here.
'use strict';
const fs = require('fs');
const path = require('path');

const [mode, credsFile, srcDir, outDir] = process.argv.slice(2);
if (!['to-ids', 'to-names'].includes(mode) || !credsFile || !srcDir || !outDir) {
  console.error('usage: swap-credentials.js to-ids|to-names <creds.json> <src_dir> <out_dir>');
  process.exit(2);
}

let creds = [];
try { creds = JSON.parse(fs.readFileSync(credsFile, 'utf8')); } catch { creds = []; }
const byName = new Map(creds.map((c) => [c.name, c.id]));
const byId = new Map(creds.map((c) => [c.id, c.name]));

// Fields that change on every save and would only add noise to git diffs.
const VOLATILE = ['versionId', 'activeVersionId', 'createdAt', 'updatedAt', 'triggerCount', 'shared',
  'homeProject', 'parentFolder', 'isArchived', 'activeVersion', 'scopes', 'checksum', 'versionCounter'];

fs.mkdirSync(outDir, { recursive: true });
const missing = new Set();
let count = 0;

for (const file of fs.readdirSync(srcDir).filter((f) => f.endsWith('.json'))) {
  const raw = JSON.parse(fs.readFileSync(path.join(srcDir, file), 'utf8'));
  const workflows = Array.isArray(raw) ? raw : [raw];
  for (const wf of workflows) {
    for (const node of wf.nodes || []) {
      for (const ref of Object.values(node.credentials || {})) {
        if (mode === 'to-ids' && String(ref.id).startsWith('CRED:')) {
          const name = ref.id.slice(5);
          if (byName.has(name)) ref.id = byName.get(name);
          else missing.add(name);
        } else if (mode === 'to-names') {
          const name = byId.get(ref.id) || ref.name;
          ref.id = `CRED:${name}`;
          ref.name = name;
        }
      }
    }
    if (mode === 'to-names') {
      for (const key of VOLATILE) delete wf[key];
      wf.active = false;
    }
    const outName = mode === 'to-names' ? `${wf.name}.json` : file;
    fs.writeFileSync(path.join(outDir, outName), JSON.stringify(wf, null, 2) + '\n');
    count += 1;
  }
}

console.log(`${count} workflow(s) written to ${outDir}`);
if (missing.size) {
  console.log(`WARNING: credentials not found in this n8n (create them, then import again): ${[...missing].sort().join(', ')}`);
}
