(() => {
  'use strict';

  const namePattern = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;
  const namespace = 'http://www.w3.org/2000/svg';

  function isRegistered(name) {
    return namePattern.test(name) && document.getElementById(`atlas-icon-${name}`)?.tagName.toLowerCase() === 'symbol';
  }

  function create(name, options = {}) {
    const requested = String(name || '').toLowerCase();
    const safeName = isRegistered(requested) ? requested : 'missing';
    const size = Math.min(64, Math.max(12, Number.parseInt(options.size, 10) || 18));
    const svg = document.createElementNS(namespace, 'svg');
    const use = document.createElementNS(namespace, 'use');
    svg.setAttribute('class', `atlas-icon atlas-icon--${size}`);
    svg.setAttribute('width', String(size));
    svg.setAttribute('height', String(size));
    svg.setAttribute('focusable', 'false');
    if (options.decorative !== false) svg.setAttribute('aria-hidden', 'true');
    else {
      svg.setAttribute('role', 'img');
      svg.setAttribute('aria-label', String(options.label || safeName));
    }
    use.setAttribute('href', `#atlas-icon-${safeName}`);
    svg.append(use);
    return svg;
  }

  window.AtlasIcon = Object.freeze({
    create,
    render(name, options) { return create(name, options).outerHTML; },
    isRegistered
  });
})();
