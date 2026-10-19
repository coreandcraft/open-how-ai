// Synthetic, deterministic histories only. Does not access installed tools.
import { createWriteStream } from 'node:fs';
import { once } from 'node:events';
const count = Number(process.argv[2] || 10000);
const destination = process.argv[3] || 'artifacts/generated-conversations.json';
if (!Number.isSafeInteger(count) || count < 1 || count > 1000000) throw new Error('Count must be between 1 and 1,000,000');
const stream = createWriteStream(destination, { flags: 'wx' });
async function write(text) { if (!stream.write(text)) await once(stream,'drain'); }
await write('[');
for (let i = 0; i < count; i++) {
  if (i) await write(',\n');
  await write(JSON.stringify({ id: `synthetic-${i}`, title: `Synthetic project conversation ${i}`, messages: [
    { id: `u-${i}`, role: 'user', content: `Investigate option ${i}.`, parentId: null },
    { id: `a-${i}`, role: 'assistant', content: 'Record the observed outcome without inventing missing evidence.', parentId: `u-${i}` },
    { id: `b-${i}`, role: 'assistant', content: 'An alternate response for branch regression testing.', parentId: `u-${i}` }
  ]}));
}
await write(']'); stream.end(); await once(stream,'finish');
console.log(`Generated ${count} conversations / ${count * 3} messages in ${destination}`);
