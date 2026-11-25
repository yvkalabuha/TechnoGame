"""Send a Python file to the live Blender MCP add-on socket (127.0.0.1:9876) and print the JSON reply.

Usage: python3 tools/models3d/blender_exec.py <script.py>   (Blender must run with the BlenderMCP server started)
"""
import json
import socket
import sys


def main(path):
    """Returns 0 on success, 1 when Blender reports an error or the reply is missing or not JSON."""
    with open(path, encoding="utf-8") as f:
        code = f.read()
    r = None
    with socket.create_connection(("127.0.0.1", 9876), timeout=600) as s:
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
    if r is None:
        print("no JSON reply from Blender", file=sys.stderr)
        return 1
    print(json.dumps(r)[:20000])
    res = r.get("result") if isinstance(r, dict) else None
    if r.get("status") != "success" or (isinstance(res, dict) and (res.get("error") or res.get("executed") is False)):
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1]))
