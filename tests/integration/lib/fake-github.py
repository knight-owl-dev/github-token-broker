#!/usr/bin/env python3
"""A canned stand-in for GitHub's installation token endpoint.

It answers one route, and checks one thing about the caller: that the App JWT is
current and attributed to this App. Everything else is taken on trust, because
what these cases exercise is the broker's own narrowing check and what reaches
the client afterwards.

Kept deliberately small. The unit suite's injected handler answers a different
question, and a responder that grew opinions would become a second model of
GitHub for the two of them to disagree about.

Usage:
  fake-github.py --port-file PATH [--status N] [--token VALUE] [--app-id N]
                 [--request-log PATH] [--permissions a=b,c=d]

The port is ephemeral and written to --port-file once bound, so cases never
race for a fixed one.
"""

import argparse
import base64
import json
import sys
import threading
import time
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

ACCESS_TOKEN_SUFFIX = "/access_tokens"


def parse_permissions(raw):
    if not raw:
        return {"metadata": "read", "contents": "write", "pull_requests": "write"}
    return dict(pair.split("=", 1) for pair in raw.split(","))


def decode_segment(segment):
    return json.loads(base64.urlsafe_b64decode(segment + "=" * (-len(segment) % 4)))


def inspect_jwt(authorization, app_id):
    """Report what is wrong with the App JWT, or None when the claims hold.

    The signature is not verified: that needs a crypto library the base images
    do not carry, and AppJwtFactoryTests already covers signing. What is checked
    is everything a wrong or missing JWT gets wrong anyway — that one was sent,
    that it says RS256, that it is attributed to this App, and that it is
    current.
    """
    if not authorization or not authorization.lower().startswith("bearer "):
        return "no bearer credential"

    parts = authorization.split(" ", 1)[1].split(".")
    if len(parts) != 3 or not parts[2]:
        return "not a signed JWT"

    try:
        header = decode_segment(parts[0])
        payload = decode_segment(parts[1])
    except (ValueError, json.JSONDecodeError):
        return "undecodable JWT"

    if header.get("alg") != "RS256":
        return f"alg {header.get('alg')}"

    if payload.get("iss") != app_id:
        return f"iss {payload.get('iss')} is not {app_id}"

    now = int(time.time())
    if not payload.get("exp", 0) > now:
        return "already expired"
    if not payload.get("iat", now + 1) <= now:
        return "iat is not backdated"

    return None


def build_handler(options):
    permissions = parse_permissions(options.permissions)
    lock = threading.Lock()

    class Handler(BaseHTTPRequestHandler):
        protocol_version = "HTTP/1.1"

        def log_message(self, *_args):
            """Silence the default stderr access log."""

        def do_POST(self):  # noqa: N802 — the name the base class dispatches to
            length = int(self.headers.get("Content-Length", "0"))
            body = self.rfile.read(length) if length else b"{}"

            complaint = inspect_jwt(self.headers.get("Authorization"), options.app_id)
            verdict = f"ok iss={options.app_id}" if complaint is None else complaint

            if options.request_log:
                with lock, open(options.request_log, "a", encoding="utf-8") as log:
                    log.write(
                        f"jwt={verdict} {self.path} "
                        f"{body.decode('utf-8', 'replace')}\n"
                    )

            # Answered the way GitHub answers a credential it will not accept, so
            # a broken JWT fails every case that mints rather than one that
            # remembered to look.
            if complaint is not None:
                self.answer(401, {"message": f"Bad credentials: {complaint}"})
                return

            if not self.path.endswith(ACCESS_TOKEN_SUFFIX):
                self.answer(404, {"message": "Not Found"})
                return

            if options.status != 201:
                self.answer(options.status, {"message": "canned failure"})
                return

            requested = json.loads(body).get("repositories") or ["unknown"]
            self.answer(201, {
                "token": options.token,
                "expires_at": (
                    datetime.now(timezone.utc) + timedelta(hours=1)
                ).strftime("%Y-%m-%dT%H:%M:%SZ"),
                "permissions": permissions,
                "repository_selection": "selected",
                "repositories": [{
                    "id": 1,
                    "name": requested[0],
                    "full_name": f"{options.owner}/{requested[0]}",
                }],
            })

        def answer(self, status, payload):
            encoded = json.dumps(payload).encode("utf-8")
            self.send_response(status)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(encoded)))
            self.end_headers()
            self.wfile.write(encoded)

    return Handler


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--port-file", required=True)
    parser.add_argument("--status", type=int, default=201)
    parser.add_argument("--token", default="ghs-integration-token")
    parser.add_argument("--owner", default="integration-owner")
    parser.add_argument("--app-id", type=int, default=123456)
    parser.add_argument("--permissions")
    parser.add_argument("--request-log")
    options = parser.parse_args()

    server = ThreadingHTTPServer(("127.0.0.1", 0), build_handler(options))

    # Written only once the port is real, so a case that sees the file can
    # connect. ApiUrlValidator permits plaintext against loopback alone, which
    # is why this shares the broker's network rather than sitting beside it.
    with open(options.port_file, "w", encoding="utf-8") as handle:
        handle.write(str(server.server_address[1]))

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        sys.exit(0)


if __name__ == "__main__":
    main()
