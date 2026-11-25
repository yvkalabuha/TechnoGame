"""Send a Python file to the live Blender MCP add-on socket (127.0.0.1:9876) and print the JSON reply.

Usage: python3 tools/models3d/blender_exec.py <script.py>   (Blender must run with the BlenderMCP server started)
"""
import json
import socket
import sys


def main(path):
    code = open(path, encoding="utf-8").read()
    s = socket.create_connection(("127.0.0.1", 9876), timeout=600)
    s.sendall(json.dumps({"type": "execute_code", "params": {"code": code}}).encode())
    buf = b""
    while True:
        d = s.recv(65536)
        if not d:
            break
        buf += d
        try:
            r = json.loads(buf.decode())
            break
        except ValueError:
            continue
    print(json.dumps(r)[:20000])


if __name__ == "__main__":
    main(sys.argv[1])
