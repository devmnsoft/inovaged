(function (root, factory) {
    var api = factory();
    if (typeof module === 'object' && module.exports) module.exports = api;
    root.GedUploadFolderRefresh = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
    function withDocumentId(list) {
        if (!Array.isArray(list)) return [];
        return list.filter(function (item) { return item && item.documentId; });
    }

    function resolveCreatedDocuments(finishList, capturedList) {
        var fromFinish = withDocumentId(finishList);
        if (fromFinish.length) return fromFinish;
        return withDocumentId(capturedList);
    }

    return {
        resolveCreatedDocuments: resolveCreatedDocuments,
        transferSeparateNotice: 'O envio do arquivo terminou. A leitura OCR e a visualização seguem em rotina própria e podem ainda estar pendentes.'
    };
});
