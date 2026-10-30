#!/usr/bin/env python3
"""P03 concurrency reproduction: E09 oversell protection + E13 concurrent idempotency.
12 checks - Python stdlib only.

Requires the backend running (default http://localhost:5000).
The script is SELF-CONTAINED: it pins the products it needs to a known stock
via ADJUST first, so it can run on any running instance regardless of the
current data state. Nothing else in the database is touched except the
transactions it creates.

One-command reproduce (repo root, backend running):
    python tests/e09_e13_concurrency.py

Optional: set P03_API to target another instance, e.g. an isolated copy:
    set P03_API=http://localhost:5100 && python tests/e09_e13_concurrency.py

NOTE: creates transactions (P007/P008 end at stock 14 / 3).
Reset to the pristine demo baseline afterwards if needed:
    cd backend
    dotnet ef database drop --force   (stop the backend first!)
    dotnet ef database update
    dotnet run -- --seed-import

Scenarios
  E09  ADJUST P007 to 5, then 10 concurrent OUT x1
       -> exactly 5x201 + 5 rejected, stock 0, strictly sequential balances
  E13  6 concurrent identical POSTs sharing one RequestId (IN +2 on P008)
       -> all return the FIRST result, exactly 1 transaction
       + same RequestId with different payload -> 409 IDEMPOTENCY_CONFLICT

Exit codes: 0 = all pass, 2 = some failed, 1 = setup error.
"""
import json
import os
import sys
import urllib.error
import urllib.request
import uuid
from concurrent.futures import ThreadPoolExecutor

BASE = os.environ.get("P03_API", "http://localhost:5000")
ADMIN = ("admin", "Admin@123!")

results = []


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


def rec(cid, ok, detail):
    results.append((cid, bool(ok)))
    print("%s %s :: %s" % ("PASS" if ok else "FAIL", cid, detail))


def main():
    r = api("POST", "/api/auth/login", {"username": ADMIN[0], "password": ADMIN[1]})
    if r[0] != 200:
        print("SETUP ERROR: login failed with HTTP %s (is the backend running?)" % r[0])
        sys.exit(1)
    token = r[1]["token"]
    prods = api("GET", "/api/products?pageSize=100", token=token)[1]["items"]
    by_code = {p["productCode"]: p for p in prods}
    rid = lambda: str(uuid.uuid4())  # noqa: E731

    def adjust(prod_id, qty):
        r = api("POST", "/api/stock-transactions", {"requestId": rid(), "productId": prod_id,
                                                    "type": "ADJUST", "quantity": qty}, token)
        if r[0] != 201:
            print("SETUP ERROR: ADJUST failed -> HTTP %s %s" % (r[0], r[1]))
            sys.exit(1)

    # ================= E09: 10 concurrent OUT, stock pinned to 5 =================
    p7 = by_code["P007"]
    adjust(p7["id"], 5)
    tx_before = api("GET", "/api/stock-transactions?pageSize=1", token=token)[1]["total"]

    def out_one(_):
        return api("POST", "/api/stock-transactions", {"requestId": rid(), "productId": p7["id"],
                                                       "type": "OUT", "quantity": 1}, token)
    print("E09: firing 10 concurrent OUT x1 against stock=5 (P007) ...")
    with ThreadPoolExecutor(max_workers=10) as pool:
        rs = list(pool.map(out_one, range(10)))
    ok201 = [x for x in rs if x[0] == 201]
    rejected = [x for x in rs if x[0] != 201]
    balances = sorted((x[1]["newStock"] for x in ok201), reverse=True)
    final = api("GET", "/api/products/%s" % p7["id"], token=token)[1]
    tx_after = api("GET", "/api/stock-transactions?pageSize=1", token=token)[1]["total"]
    reject_ok = all((x[0] == 400 and x[1]["code"] == "INSUFFICIENT_STOCK")
                    or (x[0] == 409 and x[1]["code"] == "STOCK_CONFLICT") for x in rejected)

    rec("E09.1", len(ok201) == 5 and len(rejected) == 5,
        "10 concurrent OUT -> %dx201 + %d rejected" % (len(ok201), len(rejected)))
    rec("E09.2", reject_ok, "rejections are INSUFFICIENT_STOCK(400)/STOCK_CONFLICT(409): %s"
        % sorted({x[1]["code"] for x in rejected}))
    rec("E09.3", final["currentStock"] == 0 and final["stockStatus"] == "out",
        "final stock=%s status=%s (no oversell)" % (final["currentStock"], final["stockStatus"]))
    rec("E09.4", balances == [4, 3, 2, 1, 0],
        "balances strictly sequential (no duplicate/overlap): %s" % balances)
    rec("E09.5", tx_after == tx_before + 5, "exactly 5 new transactions (+%s)" % (tx_after - tx_before))
    adjust(p7["id"], 14)  # restore seed level for reproducibility on re-runs

    # ================= E13: concurrent idempotent replay =================
    p8 = by_code["P008"]
    adjust(p8["id"], 1)  # pin to seed level
    tx_before = api("GET", "/api/stock-transactions?pageSize=1", token=token)[1]["total"]
    request_id = rid()
    body = {"requestId": request_id, "productId": p8["id"], "type": "IN", "quantity": 2}

    def replay(_):
        return api("POST", "/api/stock-transactions", body, token)
    print("\nE13: firing 6 concurrent identical POSTs (same RequestId, IN +2, P008 stock=1) ...")
    with ThreadPoolExecutor(max_workers=6) as pool:
        rs = list(pool.map(replay, range(6)))
    final = api("GET", "/api/products/%s" % p8["id"], token=token)[1]
    tx_after = api("GET", "/api/stock-transactions?pageSize=1", token=token)[1]["total"]
    ids = {x[1]["id"] for x in rs}
    stocks = {x[1]["newStock"] for x in rs}
    first_id = rs[0][1]["id"]

    rec("E13.1", all(x[0] in (200, 201) for x in rs),
        "all 6 concurrent replays succeed: %s" % sorted(x[0] for x in rs))
    rec("E13.2", len(ids) == 1, "all return the SAME transaction id: %s" % ids)
    rec("E13.3", len(stocks) == 1, "all return the SAME newStock: %s" % stocks)
    rec("E13.4", tx_after == tx_before + 1, "exactly 1 new transaction (+%s)" % (tx_after - tx_before))
    rec("E13.5", len(stocks) == 1 and final["currentStock"] == next(iter(stocks)),
        "stock matches the single recorded result: %s" % final["currentStock"])

    r = api("POST", "/api/stock-transactions", dict(body, quantity=9), token)
    rec("E13.6", r[0] == 409 and r[1]["code"] == "IDEMPOTENCY_CONFLICT",
        "same RequestId different payload -> %s/%s" % (r[0], r[1].get("code")))

    r = api("POST", "/api/stock-transactions", body, token)
    rec("E13.7", r[0] == 200 and r[1]["id"] == first_id and api(
            "GET", "/api/stock-transactions?pageSize=1", token=token)[1]["total"] == tx_after,
        "sequential replay after the race still returns first result, no extra txn")

    failed = [cid for cid, ok in results if not ok]
    print("\nCONCURRENCY: %d/%d PASS%s" % (len(results) - len(failed), len(results),
                                           "" if not failed else ", FAILED: " + ",".join(failed)))
    sys.exit(2 if failed else 0)


if __name__ == "__main__":
    main()
