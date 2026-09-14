(() => {
  'use strict';
  document.querySelectorAll('[data-dialog-open]').forEach(button => button.addEventListener('click', () => document.getElementById(button.dataset.dialogOpen)?.showModal()));
  document.querySelectorAll('[data-dialog-close]').forEach(button => button.addEventListener('click', () => button.closest('dialog')?.close()));
  document.querySelectorAll('[data-copy-hash]').forEach(button => button.addEventListener('click', async () => {
    await navigator.clipboard.writeText(button.dataset.copyHash || '');
    button.textContent = 'SHA-256 copiado';
  }));
  document.querySelector('[data-reprint-form]')?.addEventListener('submit', event => {
    const form = event.currentTarget;
    if (form.querySelector('[name="mode"]:checked')?.value !== 'current') return;
    event.preventDefault();
    const reason = form.querySelector('[name="reason"]');
    if (!reason.reportValidity()) return;
    const url = new URL(form.dataset.currentUrl, window.location.origin);
    url.searchParams.set('reprintReason', reason.value);
    window.location.assign(url);
  });
  const queue = document.querySelector('[data-print-queue]');
  if (queue) {
    const key = 'inovaged.labels.queue.preferences';
    const size = queue.querySelector('[name="PageSize"]');
    const sort = queue.querySelector('[name="Sort"]');
    try { const saved = JSON.parse(localStorage.getItem(key) || '{}'); if (!new URLSearchParams(location.search).has('PageSize') && saved.pageSize) size.value = saved.pageSize; if (!new URLSearchParams(location.search).has('Sort') && saved.sort) sort.value = saved.sort; } catch { /* corrupted preferences are ignored */ }
    queue.addEventListener('submit', () => localStorage.setItem(key, JSON.stringify({ pageSize: size.value, sort: sort.value })));
  }
})();
