#!/usr/bin/env python3
"""P03 performance spot-check (acceptance requirement, spec section 9).

Measures P95 latency under no external load (sequential requests):
  - product list            target P95 < 1s
  - product detail          target P95 < 1s
  - dashboard               (reported, no hard gate)
  - stock submission (IN)   target P95 < 2s

Requires the backend running (default http://localhost:5000, override with
env P03_API). Prints the recording template required by the spec: hardware,
SQL Server version, data scale, P95 numbers. Mutates the database (stock
submissions) - reset the baseline afterwards if needed.

Usage:  python tests/perf_check.py
Exit codes: 0 = targets met, 2 = targets exceeded, 1 = setup error.
"""
import json
import os
import platform
import subprocess
import sys
import time
import urllib.error
import urllib.request
import uuid

BASE = os.environ.get("P03_API", "http://localhost:5000")
N_LIST = 50
N_DETAIL = 50
N_DASH = 30
N_SUBMIT = 30
ADMIN = ("admin", "Admin@123!")


def api(method, path, body=None, token=None):
    req = urllib.request.Request(BASE + path, method=method)
    req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", "Bearer " + token)
    data = json.dumps(body).encode() if body is not None else None
    try:
        with urllib.request.urlopen(req, data=data, timeout=15) as res:
            raw = res.read().decode()
            return res.status, (json.loads(raw) if raw else None)
    except urllib.error.HTTPError as e:
        raw = e.read().decode()
        return e.code, (json.loads(raw) if raw else None)


def p95(samples):
    s = sorted(samples)
    return s[max(0, int(len(s) * 0.95) - 1)]


def bench(label, n, fn):
    for _ in range(5):  # warm-up
        fn()
    times = []
    for _ in range(n):
        t0 = time.perf_counter()
        fn()
        times.append((time.perf_counter() - t0) * 1000)
    print("  %-18s n=%-3d P50=%7.1fms  P95=%7.1fms  max=%7.1fms"
          % (label, n, sorted(times)[len(times) // 2], p95(times), max(times)))
    return p95(times)


def main():
    r = api("POST", "/api/auth/login", {"username": ADMIN[0], "password": ADMIN[1]})
    if r[0] != 200:
        print("SETUP ERROR: login failed with HTTP %s" % r[0])
        sys.exit(1)
    token = r[1]["token"]

    # ---- environment record ----
    try:
        sdk = subprocess.run(["dotnet", "--version"], capture_output=True, text=True,
                             timeout=30).stdout.strip()
    except Exception:
        sdk = "unknown"
    try:
        info = subprocess.run(["sqllocaldb", "info", "MSSQLLocalDB"], capture_output=True,
                              timeout=30).stdout.decode("gbk", errors="replace")
        sqlver = next((l.split(":", 1)[1].strip() for l in info.splitlines()
                       if l.split(":", 1)[0].strip() in ("Version", "版本")), "unknown")
    except Exception:
        sqlver = "unknown"
    prods = api("GET", "/api/products?pageSize=100", token=token)[1]
    tx_total = api("GET", "/api/stock-transactions?pageSize=1", token=token)[1]["total"]
    print("== Environment ==")
    print("  OS: %s, CPUs: %s" % (platform.platform(), os.cpu_count()))
    print("  CPU: %s" % platform.processor())
    print("  .NET SDK: %s | LocalDB version: %s" % (sdk, sqlver))
    print("  Data scale: %d products, %d transactions, total stock=%s"
          % (prods["total"], tx_total,
             sum(p["currentStock"] for p in prods["items"])))

    # ---- benchmarks ----
    print("\n== Latency (sequential, no external load) ==")
    p_list = bench("product list", N_LIST,
                   lambda: api("GET", "/api/products?page=1&pageSize=20", token=token))
    pid = prods["items"][0]["id"]
    p_detail = bench("product detail", N_DETAIL,
                     lambda: api("GET", "/api/products/%s" % pid, token=token))
    p_dash = bench("dashboard", N_DASH,
                   lambda: api("GET", "/api/dashboard", token=token))

    target = prods["items"][0]
    rid = lambda: str(uuid.uuid4())  # noqa: E731

    def submit():
        return api("POST", "/api/stock-transactions",
                   {"requestId": rid(), "productId": target["id"],
                    "type": "IN", "quantity": 1}, token)
    p_submit = bench("stock submit", N_SUBMIT, submit)

    print("\n== Verdict ==")
    checks = [("list P95 < 1000ms", p_list < 1000, "%.0fms" % p_list),
              ("detail P95 < 1000ms", p_detail < 1000, "%.0fms" % p_detail),
              ("submit P95 < 2000ms", p_submit < 2000, "%.0fms" % p_submit),
              ("dashboard (no gate)", True, "%.0fms" % p_dash)]
    failed = False
    for name, ok, val in checks:
        print("  %s %-22s (%s)" % ("PASS" if ok else "FAIL", name, val))
        failed |= not ok
    sys.exit(2 if failed else 0)


if __name__ == "__main__":
    main()
