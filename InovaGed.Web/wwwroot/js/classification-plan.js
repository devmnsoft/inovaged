/* global Sortable */
(function () {
    const nodes = (window.__cp_nodes || []);
    const getId = n => n.id || n.Id;
    const getParentId = n => n.parentId || n.ParentId;

    function buildTree() {
        const children = new Map();
        nodes.forEach(n => {
            const p = getParentId(n) || null;
            if (!children.has(p)) children.set(p, []);
            children.get(p).push(n);
        });
        children.forEach(list => list.sort((a, b) => (a.code || a.Code).localeCompare(b.code || b.Code)));

        function render(parentId, level) {
            const list = children.get(parentId || null) || [];
            const ul = document.createElement("ul");
            ul.className = "list-group mb-2";
            ul.dataset.parentId = parentId || "";

            list.forEach(n => {
                const li = document.createElement("li");
                li.className = "list-group-item d-flex align-items-start justify-content-between gap-2";
                li.dataset.id = getId(n);

                const left = document.createElement("div");
                left.className = "flex-grow-1";
                left.innerHTML = `
          <div>
            <span class="text-secondary" style="font-family: ui-monospace;">${"—".repeat(level)}</span>
            <span style="font-family: ui-monospace; font-weight:600">${escapeHtml(n.code || n.Code)}</span>
            <span class="ms-2">${escapeHtml(n.name || n.Name)}</span>
          </div>
          <div class="text-muted small">${escapeHtml(n.retentionStartEvent || n.RetentionStartEvent)} • Destino: ${escapeHtml(n.finalDestination || n.FinalDestination)}</div>
        `;

                const right = document.createElement("div");
                right.className = "d-flex gap-1";
                right.innerHTML = `
          <button class="btn btn-sm btn-outline-secondary" type="button" title="Editar"><i class="bi bi-pencil"></i></button>
          <button class="btn btn-sm btn-outline-primary" type="button" title="Criar filho"><i class="bi bi-plus-lg"></i></button>
          <span class="btn btn-sm btn-outline-secondary" style="cursor:grab" title="Arraste"><i class="bi bi-grip-vertical"></i></span>
        `;

                right.children[0].addEventListener("click", () => cp.openEdit(getId(n)));
                right.children[1].addEventListener("click", () => cp.openCreate(getId(n)));

                li.appendChild(left);
                li.appendChild(right);
                ul.appendChild(li);

                const childUl = render(getId(n), level + 1);
                if (childUl.children.length) {
                    const wrap = document.createElement("div");
                    wrap.className = "ms-4 mt-2";
                    wrap.appendChild(childUl);
                    ul.appendChild(wrap);
                }
            });

            new Sortable(ul, {
                group: "cpTree",
                animation: 150,
                handle: ".bi-grip-vertical",
                onAdd: async function (evt) {
                    const movedId = evt.item.dataset.id;
                    const newParentId = evt.to.dataset.parentId || null;
                    await cp.move(movedId, newParentId);
                }
            });

            return ul;
        }

        const root = document.getElementById("cpTree");
        root.innerHTML = "";
        root.appendChild(render(null, 0));
    }

    function escapeHtml(str) {
        return (str || "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll("\"", "&quot;")
            .replaceAll("'", "&#039;");
    }

    async function loadModal(url) {
        const body = document.getElementById("cpEditBody");
        body.innerHTML = `<div class="text-muted">Carregando...</div>`;
        const html = await fetch(url).then(r => r.text());
        body.innerHTML = html;

        const form = document.getElementById("cpEditForm");
        form.addEventListener("submit", async (e) => {
            e.preventDefault();
            const fd = new FormData(form);
            const res = await fetch(form.action, { method: "POST", body: fd });
            const j = await res.json().catch(() => null);
            if (!res.ok || !j?.ok) {
                alert(j?.error || "Falha ao salvar.");
                return;
            }
            bootstrap.Modal.getInstance(document.getElementById("cpEditModal")).hide();
            location.reload();
        });
    }

    window.cp = {
        openEdit: async (id) => {
            await loadModal(`/ClassificationPlan/Edit?id=${encodeURIComponent(id)}`);
            new bootstrap.Modal(document.getElementById("cpEditModal")).show();
        },
        openCreate: async (parentId) => {
            const url = parentId ? `/ClassificationPlan/Edit?parentId=${encodeURIComponent(parentId)}` : `/ClassificationPlan/Edit`;
            await loadModal(url);
            new bootstrap.Modal(document.getElementById("cpEditModal")).show();
        },
        move: async (id, newParentId) => {
            const res = await fetch("/ClassificationPlan/Move", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ id, newParentId })
            });
            const j = await res.json().catch(() => null);
            if (!res.ok || !j?.ok) {
                alert(j?.error || "Falha ao mover.");
                location.reload();
            }
        }
    };

    buildTree();
})();