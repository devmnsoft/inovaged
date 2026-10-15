"""Loopback synthetic partner. Credentials stay in this backend's environment.

INOVAGED_PARTNER_HOST=https://localhost:7189
INOVAGED_PARTNER_CLIENT=sector-validation
INOVAGED_PARTNER_SECRET=(synthetic secret configured in the disposable database)
INOVAGED_PARTNER_PORT=5190
INOVAGED_PARTNER_INSECURE_TLS=1  # development certificate only
"""
import http.server
import html
import json
import os
from pathlib import Path
import ssl
import urllib.error
import urllib.request

HOST = os.environ.get("INOVAGED_PARTNER_HOST", "https://localhost:7189").rstrip("/")
PORT = int(os.environ.get("INOVAGED_PARTNER_PORT", "5190"))
CLIENT = os.environ.get("INOVAGED_PARTNER_CLIENT", "sector-validation")
SECRET = os.environ.get("INOVAGED_PARTNER_SECRET", "")


class Handler(http.server.BaseHTTPRequestHandler):
    def log_message(self, *_):
        pass  # Never log URLs, bodies, patient identifiers or grants.

    def reply(self, status, body, content_type="application/json; charset=utf-8"):
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Cache-Control", "no-store")
        self.send_header("Referrer-Policy", "no-referrer")
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path != "/":
            return self.reply(404, b"{}")
        page = Path(__file__).with_name("index.html").read_text(encoding="utf-8")
        page = page.replace('id="host" value="https://inovaged.exemplo"', 'id="host" value="' + html.escape(HOST, quote=True) + '"')
        page = page.replace('id="integracao" value="parceiro-exemplo"', 'id="integracao" value="' + html.escape(CLIENT, quote=True) + '"')
        self.reply(200, page.encode("utf-8"), "text/html; charset=utf-8")

    def do_POST(self):
        if self.path not in ("/abertura", "/estado", "/recuperar"):
            return self.reply(404, b"{}")
        # Local fixture only. Reject cross-origin browser commands and never enable CORS.
        if self.headers.get("Origin") not in (f"http://localhost:{PORT}", f"http://127.0.0.1:{PORT}"):
            return self.reply(403, b"{}")
        if not SECRET:
            return self.reply(503, b'{"mensagem":"Configure a chave no backend de teste."}')
        try:
            length = int(self.headers.get("Content-Length", "0"))
            if not 0 < length <= 16000:
                return self.reply(400, b"{}")
            body = json.loads(self.rfile.read(length))
            key = body.pop("idempotencia", "")
            if not isinstance(key, str) or not key or len(key) > 80:
                return self.reply(400, b"{}")
            target = {"/abertura": "", "/estado": "/estado", "/recuperar": "/recuperar"}[self.path]
            method = "GET" if self.path == "/estado" else "POST"
            request = urllib.request.Request(HOST + "/api/integracoes/demais-setores/aberturas" + target,
                data=None if method == "GET" else json.dumps(body).encode(), method=method,
                headers={"Content-Type": "application/json", "X-InovaGed-Integracao": CLIENT,
                         "X-InovaGed-Chave": SECRET, "X-InovaGed-Idempotencia": key})
            context = ssl._create_unverified_context() if os.environ.get("INOVAGED_PARTNER_INSECURE_TLS") == "1" else None
            with urllib.request.urlopen(request, timeout=30, context=context) as response:
                self.reply(response.status, response.read())
        except urllib.error.HTTPError as error:
            self.reply(error.code, error.read())
        except Exception:
            self.reply(502, b'{"mensagem":"A aplicacao de teste nao respondeu."}')


if __name__ == "__main__":
    http.server.ThreadingHTTPServer(("127.0.0.1", PORT), Handler).serve_forever()
