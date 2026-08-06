(() => {
  'use strict';
  const root = document.querySelector('[data-physical-locations]');
  if (!root) return;
  const buildingFilter = root.querySelector('[data-building-filter]');
  buildingFilter?.addEventListener('change', () => {
    const selected = buildingFilter.value;
    root.querySelectorAll('[data-building]').forEach(card => { card.hidden = !!selected && card.dataset.building !== selected; });
  });
  const modalElement = document.getElementById('deleteLocationModal');
  const modal = modalElement && window.bootstrap ? bootstrap.Modal.getOrCreateInstance(modalElement) : null;
  root.addEventListener('click', event => {
    const trigger = event.target.closest('[data-delete-location]');
    if (!trigger || !modalElement) return;
    modalElement.querySelector('[data-delete-name]').textContent = trigger.dataset.locationName || 'esta localização';
    modalElement.querySelector('[data-delete-form]').action = `/Physical/Locations/${encodeURIComponent(trigger.dataset.locationId)}/Delete`;
    modal?.show();
  });
})();
