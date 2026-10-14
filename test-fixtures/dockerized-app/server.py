import http.server
import socketserver
import os
import sys

PORT = 8080
SIMULATE_FAILURE = os.environ.get("SIMULATE_FAILURE", "false").lower() == "true"

class HealthHandler(http.server.SimpleHTTPRequestHandler):
    def do_GET(self):
        if self.path == "/health":
            if SIMULATE_FAILURE:
                self.send_response(500)
                self.send_header("Content-type", "application/json")
                self.end_headers()
                self.wfile.write(b'{"status": "unhealthy", "code": 500}\n')
            else:
                self.send_response(200)
                self.send_header("Content-type", "application/json")
                self.end_headers()
                self.wfile.write(b'{"status": "ok", "app": "test-dockerized"}\n')
        else:
            self.send_response(200)
            self.send_header("Content-type", "text/html")
            self.end_headers()
            self.wfile.write(b"<h1>Bridge Test Dockerized App</h1><p>Running healthy.</p>\n")

if __name__ == "__main__":
    print(f"Starting server on port {PORT}... (SIMULATE_FAILURE={SIMULATE_FAILURE})", flush=True)
    with socketserver.TCPServer(("", PORT), HealthHandler) as httpd:
        httpd.serve_forever()
