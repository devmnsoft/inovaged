(function () {
  const init = async () => {
    const hubs = document.querySelectorAll('[data-ocr-realtime]');
    if (!hubs.length) return;

    await import('/lib/microsoft/signalr/signalr.min.js').catch(() => null);
    if (!window.signalR) return;

    const connection = new signalR.HubConnectionBuilder().withUrl('/hubs/ocr-status').withAutomaticReconnect().build();
    connection.on('ocr.status', (evt) => {
      const badge = document.querySelector(`[data-ocr-badge="${evt.versionId}"]`);
      if (badge) { badge.textContent = evt.status; badge.className = `badge ${evt.status === 'ERROR' ? 'bg-danger' : evt.status === 'COMPLETED' ? 'bg-success' : 'bg-warning text-dark'}`; }
      const previewFrame = document.querySelector('[data-preview-frame]');
      if (previewFrame && evt.status === 'COMPLETED') previewFrame.src = previewFrame.src;
    });

    await connection.start();
    document.querySelectorAll('[data-ocr-version]').forEach(el => connection.invoke('SubscribeVersion', el.getAttribute('data-ocr-version')));
  };

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
})();
