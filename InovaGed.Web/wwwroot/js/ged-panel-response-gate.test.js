const assert = require('assert');
const gate = require('./ged-panel-response-gate.js');

function current(overrides) {
    return Object.assign({ open: true, generation: 1, documentId: 'A', versionId: 'v1' }, overrides);
}

assert.strictEqual(gate.applies({ generation: 1, documentId: 'A', versionId: 'v1' }, current({ documentId: 'B' })), false);
assert.strictEqual(gate.applies({ generation: 1, documentId: 'A', versionId: 'v1' }, current({ open: false })), false);
assert.strictEqual(gate.applies({ generation: 1, documentId: 'A', versionId: 'old' }, current({ versionId: 'new' })), false);
assert.strictEqual(gate.applies({ generation: 1, documentId: 'A', versionId: '' }, current({ versionId: '' })), true);
assert.strictEqual(gate.applies({ generation: 2, documentId: 'A', versionId: '' }, current({ generation: 2, versionId: 'resolved' })), true);
assert.strictEqual(gate.applies({ generation: 1, documentId: 'A', versionId: '' }, current({ generation: 2, versionId: 'resolved' })), false);
assert.strictEqual(gate.applies({ generation: 3, documentId: 'A', versionId: 'v9' }, current({ generation: 3, versionId: 'v9' })), true);
console.log('ged-panel-response-gate: ok');
