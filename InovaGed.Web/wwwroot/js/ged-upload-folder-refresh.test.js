const assert = require('assert');
const api = require('./ged-upload-folder-refresh.js');

const captured = [{ documentId: 'doc-a', fileName: 'a.pdf' }];
assert.deepStrictEqual(api.resolveCreatedDocuments([], captured), captured);
assert.deepStrictEqual(api.resolveCreatedDocuments(null, captured), captured);
assert.deepStrictEqual(api.resolveCreatedDocuments([{ documentId: '' }], captured), captured);
assert.deepStrictEqual(api.resolveCreatedDocuments([{ documentId: 'doc-b' }], captured).map(x => x.documentId), ['doc-b']);
assert.deepStrictEqual(api.resolveCreatedDocuments([], []), []);
assert.match(api.transferSeparateNotice, /OCR/);
assert.match(api.transferSeparateNotice, /visualização/);
console.log('ged-upload-folder-refresh: ok');
