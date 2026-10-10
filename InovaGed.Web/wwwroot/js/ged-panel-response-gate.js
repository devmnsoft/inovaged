(function (root, factory) {
    const api = factory();
    if (typeof module !== 'undefined' && module.exports) module.exports = api;
    root.GedPanelResponseGate = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
    function text(value) { return value == null ? '' : String(value); }

    function applies(response, current) {
        if (!current || current.open !== true) return false;
        if (!response || response.generation !== current.generation) return false;
        if (text(response.documentId) !== text(current.documentId)) return false;
        const responseVersion = text(response.versionId);
        const currentVersion = text(current.versionId);
        if (!responseVersion || !currentVersion) return true;
        return responseVersion === currentVersion;
    }

    return { applies: applies };
});
