(function () {
  document.querySelectorAll("[data-ds-clinical-extraction]").forEach(function (form) {
    let running = false;
    const button = form.querySelector("button[type='submit']");
    const result = form.querySelector("[data-ds-clinical-result]");
    const refresh = form.querySelector("[data-ds-clinical-refresh]");
    refresh.addEventListener("click", function () { window.location.reload(); });
    form.addEventListener("submit", async function (event) {
      event.preventDefault();
      if (running) return;
      running = true; button.disabled = true;
      result.textContent = "Verificando a autorização e a política de IA desta versão…";
      try {
        const response = await fetch(form.action, { method: "POST", body: new FormData(form), credentials: "same-origin", headers: { "X-Requested-With": "fetch" } });
        if (!(response.headers.get("content-type") || "").includes("json")) throw new Error("unavailable");
        const payload = await response.json();
        result.textContent = payload.message || "A solicitação não foi concluída.";
        if (payload.state === "AGUARDANDO_RESULTADO" && payload.runId) {
          form.elements.resumeRunId.value = payload.runId;
          button.textContent = "Retomar esta solicitação";
        }
        if (payload.state === "CONCLUIDO") refresh.hidden = false;
      } catch {
        result.textContent = "Não foi possível confirmar o resultado. Repita esta solicitação para consultar a mesma execução; não é necessário iniciar outro envio.";
      } finally { running = false; button.disabled = false; }
    });
  });
})();
