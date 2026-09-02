(() => {
  const roots = document.querySelectorAll('[data-print-logo-editor]');
  roots.forEach(root => {
    const image = root.querySelector('[data-logo-preview]');
    const frame = root.querySelector('[data-logo-frame]');
    if (!image || !frame) return;
    const update = () => {
      const option = root.querySelector('[data-logo-select] option:checked');
      if (option?.dataset.preview) image.src = option.dataset.preview;
      const width = Number(root.querySelector('[data-logo-width]')?.value);
      const height = Number(root.querySelector('[data-logo-height]')?.value);
      if (width > 0) frame.style.width = `${width}mm`;
      frame.style.height = height > 0 ? `${height}mm` : 'auto';
      image.style.objectFit = (root.querySelector('[data-logo-fit]')?.value || 'contain').toLowerCase();
    };
    root.addEventListener('input', update); root.addEventListener('change', update); update();
  });
})();
