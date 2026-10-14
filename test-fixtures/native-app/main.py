import http.server
import socketserver
import os
import sys

PORT = int(os.environ.get("APP_PORT", "8085"))
FAIL_STARTUP = os.environ.get("FAIL_STARTUP", "false").lower() == "true"

if FAIL_STARTUP:
    print("FATAL: Startup simulation failure requested. Exiting code 1.", file=sys.stderr, flush=True)
    sys.exit(1)

class NativeHandler(http.server.SimpleHTTPRequestHandler):
    def do_GET(self):
        if self.path == "/health":
            self.send_response(200)
            self.send_header("Content-type", "application/json")
            self.end_headers()
            self.wfile.write(b'{"status": "ok", "mode": "native-service"}\n')
        else:
            self.send_response(200)
            self.send_header("Content-type", "text/html")
            self.end_headers()
            self.wfile.write(b"<h1>Bridge Test Native Linux App</h1>\n")

if __name__ == "__main__":
    print(f"Starting native Linux service on port {PORT}...", flush=True)
    with socketserver.TCPServer(("", PORT), NativeHandler) as httpd:
        httpd.serve_forever()
