window.InovaGedSigningAgentClient = (() => {
  const protocol = 'agent-cms-detached-v1';
  const antiforgery = () => document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
  const agentBaseUrl = () => document.querySelector('[data-signing-agent-root]')?.dataset.agentUrl || 'https://127.0.0.1:17891';
  const nonce = () => crypto.getRandomValues(new Uint8Array(16)).reduce((s, b) => s + b.toString(16).padStart(2, '0'), '');
  const headers = (pairingToken) => ({
    'X-InovaGed-Pairing-Token': pairingToken,
    'X-InovaGed-Origin': location.origin,
    'X-InovaGed-Request-Nonce': nonce(),
    'X-InovaGed-Agent-Protocol': protocol
  });
  async function json(path, options = {}) {
    const response = await fetch(agentBaseUrl() + path, { cache: 'no-store', ...options });
    if (!response.ok) throw new Error(`Signing Agent ${response.status}`);
    return response.json();
  }
  return {
    health: () => json('/health'),
    info: () => json('/info'),
    createPairingChallenge: () => json('/pairing/challenges', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ origin: location.origin }) }),
    confirmPairingLocal: (id) => json(`/pairing/challenges/${id}/confirm-local`, { method: 'POST' }),
    completePairing: (id, code) => json(`/pairing/challenges/${id}/complete`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ code }) }),
    certificates: (pairingToken) => json('/certificates', { headers: headers(pairingToken) }),
    createSession: (documentId, documentVersionId, purpose) => fetch('/api/signing/sessions', { method: 'POST', headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': antiforgery() }, body: JSON.stringify({ documentId, documentVersionId, purpose }) }).then(r => r.json()),
    createOperation: (pairingToken, session, certificateThumbprint, purpose) => json('/operations', { method: 'POST', headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': antiforgery(), ...headers(pairingToken) }, body: JSON.stringify({ pairingToken, origin: location.origin, contentUrl: new URL(session.contentUrl, location.origin).href, contentToken: session.contentToken, expectedSha256: session.expectedSha256, certificateThumbprint, documentName: session.fileName, version: session.versionLabel, size: session.sizeBytes, purpose }) }),
    confirmLocalUrl: (operationId) => `${agentBaseUrl()}/operations/${operationId}/confirm-ui`,
    operation: (pairingToken, id) => json(`/operations/${id}`, { headers: headers(pairingToken) }),
    complete: (sessionId, result, completionToken, idempotencyKey) => fetch(`/api/signing/sessions/${sessionId}/complete`, { method: 'POST', headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': antiforgery() }, body: JSON.stringify({ completionToken, idempotencyKey, signatureCmsBase64: result.signatureCmsBase64, certificateDerBase64: result.certificateDerBase64, certificateChainDerBase64: [], agentOperationId: result.operationId, agentVersion: protocol }) }).then(r => r.json()),
    downloadP7s: (signatureId) => `/api/signatures/${signatureId}/download`,
    downloadPackage: (signatureId) => `/api/signatures/${signatureId}/package`
  };
})();
