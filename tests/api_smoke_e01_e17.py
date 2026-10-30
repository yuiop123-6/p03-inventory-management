#!/usr/bin/env python3
"""P03 API smoke test E01-E17 (19 checks) - Python stdlib only.

Requires a CLEAN baseline database (9 products / 129 stock / 0 transactions)
and the backend running (default http://localhost:5000).

Reset the baseline before running (from the backend/ directory):
    dotnet ef database drop --force
    dotnet ef database update
    dotnet run -- --seed-import

NOTE: this script MUTATES data (creates transactions, adjusts stock).
Reset the baseline again afterwards if you need the pristine demo state.

Usage:  python tests/api_smoke_e01_e17.py
Exit codes: 0 = all pass, 2 = some failed, 1 = setup error.
"""
import json
import sys
import urllib.error
import urllib.request
import uuid
from concurrent.futures import ThreadPoolExecutor

BASE = __import__("os").environ.get("P03_API", "http://localhost:5000")
ADMIN = ("admin", "Admin@123!")
OPERATOR = ("operator", "Operator@123!")

results = []
tokens = {}


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


def login(user, password):
    status, obj = api("POST", "/api/auth/login", {"username": user, "password": password})
    if status != 200:
        print("SETUP ERROR: login failed with HTTP %s" % status)
        sys.exit(1)
    return obj["token"]


def rec(cid, title, ok, detail=""):
    results.append((cid, bool(ok)))
    print("%s %s %s :: %s" % ("PASS" if ok else "FAIL", cid, title, detail))


def get_products(token, qs=""):
    return api("GET", "/api/products" + qs, token=token)


def find_by_code(items, code):
    for p in items:
        if p["productCode"] == code:
            return p
    return None


def main():
    tokens["admin"] = login(*ADMIN)
    tokens["op"] = login(*OPERATOR)
    t = tokens["admin"]
    rid = lambda: str(uuid.uuid4())  # noqa: E731

    # ---- E01 seed baseline (1 check) ----
    dash = api("GET", "/api/dashboard", token=t)[1]
    prods = get_products(t, "?pageSize=100")[1]
    cats = api("GET", "/api/categories", token=t)[1]
    tx = api("GET", "/api/stock-transactions?pageSize=1", token=t)[1]
    rec("E01", "seed baseline 9/129/4/2, 3 categories, 0 transactions",
        dash["totalProducts"] == 9 and dash["totalStockQuantity"] == 129
        and dash["lowStockProducts"] == 4 and dash["outOfStockProducts"] == 2
        and prods["total"] == 9 and cats["total"] == 3 and tx["total"] == 0,
        "dash=%s/%s/%s/%s prods=%s cats=%s txns=%s" % (
            dash["totalProducts"], dash["totalStockQuantity"], dash["lowStockProducts"],
            dash["outOfStockProducts"], prods["total"], cats["total"], tx["total"]))

    # ---- E02 duplicate product code (1) ----
    p1 = find_by_code(prods["items"], "P001")
    r = api("POST", "/api/products", {"productCode": "P001", "name": "Dup",
                                      "categoryId": p1["categoryId"], "unitPrice": 1,
                                      "currentStock": 0, "minimumStockLevel": 0}, t)
    rec("E02", "duplicate product code -> 409 DUPLICATE_VALUE",
        r[0] == 409 and r[1]["code"] == "DUPLICATE_VALUE", "%s/%s" % (r[0], r[1].get("code")))

    # ---- E03 duplicate category name (1) ----
    name = cats["items"][0]["name"]
    r1 = api("POST", "/api/categories", {"name": name}, t)
    r2 = api("POST", "/api/categories", {"name": name.upper() + "  "}, t)
    rec("E03", "duplicate category name (case/space) -> 409",
        r1[0] == 409 and r2[0] == 409, "exact=%s variant=%s" % (r1[0], r2[0]))

    # ---- E04 delete referenced category (1) ----
    r = api("DELETE", "/api/categories/%s" % cats["items"][0]["id"], token=t)
    rec("E04", "delete category with products -> 409 REFERENTIAL_CONFLICT",
        r[0] == 409 and r[1]["code"] == "REFERENTIAL_CONFLICT", "%s/%s" % (r[0], r[1].get("code")))

    # ---- E11 invalid values (3 checks: price / stock / qty) ----
    r1 = api("POST", "/api/products", {"productCode": "PNEG1", "name": "Neg price",
                                       "categoryId": p1["categoryId"], "unitPrice": -1,
                                       "currentStock": 0, "minimumStockLevel": 0}, t)
    rec("E11a", "negative unitPrice -> 400", r1[0] == 400, str(r1[0]))
    r2 = api("POST", "/api/products", {"productCode": "PNEG2", "name": "Neg stock",
                                       "categoryId": p1["categoryId"], "unitPrice": 1,
                                       "currentStock": -5, "minimumStockLevel": 0}, t)
    rec("E11b", "negative currentStock -> 400", r2[0] == 400, str(r2[0]))
    any_prod = prods["items"][0]
    r3 = api("POST", "/api/stock-transactions", {"requestId": rid(), "productId": any_prod["id"],
                                                 "type": "IN", "quantity": -3}, t)
    r4 = api("POST", "/api/stock-transactions", {"requestId": rid(), "productId": any_prod["id"],
                                                 "type": "OUT", "quantity": 0}, t)
    rec("E11c", "negative IN qty / zero OUT qty -> 400", r3[0] == 400 and r4[0] == 400,
        "%s/%s" % (r3[0], r4[0]))

    # ---- E12 PUT forbidden fields (1) ----
    cur = get_products(t, "?search=P001")[1]["items"][0]
    r1 = api("PUT", "/api/products/%s" % cur["id"], {"productCode": "HACKED", "name": "x"}, t)
    r2 = api("PUT", "/api/products/%s" % cur["id"], {"currentStock": 999, "name": "x"}, t)
    after = api("GET", "/api/products/%s" % cur["id"], token=t)[1]
    rec("E12", "PUT with productCode/currentStock -> 400, untouched",
        r1[0] == 400 and r2[0] == 400 and after["productCode"] == "P001"
        and after["currentStock"] == cur["currentStock"],
        "%s/%s stock=%s" % (r1[0], r2[0], after["currentStock"]))

    # ---- E14 operator permissions (1) ----
    p_any = get_products(t, "?pageSize=1")[1]["items"][0]
    r_adj = api("POST", "/api/stock-transactions", {"requestId": rid(), "productId": p_any["id"],
                                                    "type": "ADJUST", "quantity": 1}, tokens["op"])
    r_del = api("DELETE", "/api/products/%s" % p_any["id"], token=tokens["op"])
    r_put = api("PUT", "/api/products/%s" % p_any["id"], {"name": "nope"}, tokens["op"])
    r_cat = api("POST", "/api/categories", {"name": "OpCat"}, tokens["op"])
    r_rep = api("GET", "/api/reports/products", token=tokens["op"])
    rec("E14", "operator: ADJUST/DELETE/PUT/POST-CAT 403; reads OK",
        all(x[0] == 403 for x in (r_adj, r_del, r_put, r_cat)) and r_rep[0] == 200,
        "403x%s read=%s" % (sum(1 for x in (r_adj, r_del, r_put, r_cat) if x[0] == 403), r_rep[0]))

    # ---- E05 IN (1) ----
    p2 = find_by_code(prods["items"], "P002")
    dash_before = api("GET", "/api/dashboard", token=t)[1]["totalStockQuantity"]
    r = api("POST", "/api/stock-transactions", {"requestId": rid(), "productId": p2["id"],
                                                "type": "IN", "quantity": 5, "notes": "E05"}, t)
    after = api("GET", "/api/products/%s" % p2["id"], token=t)[1]
    rec("E05", "IN +5: 201, stock & dashboard updated",
        r[0] == 201 and r[1]["newStock"] == p2["currentStock"] + 5
        and after["currentStock"] == p2["currentStock"] + 5
        and api("GET", "/api/dashboard", token=t)[1]["totalStockQuantity"] == dash_before + 5,
        "%s->%s" % (r[1].get("previousStock"), r[1].get("newStock")))

    # ---- E06 OUT by operator (1) ----
    p2 = api("GET", "/api/products/%s" % p2["id"], token=t)[1]
    r = api("POST", "/api/stock-transactions", {"requestId": rid(), "productId": p2["id"],
                                                "type": "OUT", "quantity": 2}, tokens["op"])
    after = api("GET", "/api/products/%s" % p2["id"], token=t)[1]
    rec("E06", "OUT -2 (operator): 201, stock decremented",
        r[0] == 201 and r[1]["newStock"] == p2["currentStock"] - 2
        and after["currentStock"] == p2["currentStock"] - 2,
        "%s->%s" % (r[1].get("previousStock"), r[1].get("newStock")))

    # ---- E07 insufficient stock (1) ----
    p2 = api("GET", "/api/products/%s" % p2["id"], token=t)[1]
    tx_before = api("GET", "/api/stock-transactions?pageSize=1", token=t)[1]["total"]
    r = api("POST", "/api/stock-transactions", {"requestId": rid(), "productId": p2["id"],
                                                "type": "OUT", "quantity": p2["currentStock"] + 1}, t)
    tx_after = api("GET", "/api/stock-transactions?pageSize=1", token=t)[1]["total"]
    stock_now = api("GET", "/api/products/%s" % p2["id"], token=t)[1]["currentStock"]
    rec("E07", "OUT exceeding stock -> 400 INSUFFICIENT_STOCK, no side effects",
        r[0] == 400 and r[1]["code"] == "INSUFFICIENT_STOCK" and stock_now == p2["currentStock"]
        and tx_after == tx_before, "%s/%s stock=%s" % (r[0], r[1].get("code"), stock_now))

    # ---- E08 ADJUST (1) ----
    p2 = api("GET", "/api/products/%s" % p2["id"], token=t)[1]
    r = api("POST", "/api/stock-transactions", {"requestId": rid(), "productId": p2["id"],
                                                "type": "ADJUST", "quantity": 0}, t)
    after = api("GET", "/api/products/%s" % p2["id"], token=t)[1]
    rec("E08", "ADJUST to 0: 201, status=out",
        r[0] == 201 and r[1]["newStock"] == 0 and after["currentStock"] == 0
        and after["stockStatus"] == "out", "%s->%s" % (r[1].get("previousStock"), r[1].get("newStock")))

    # ---- E09 concurrent pair (1) ----
    all_p = get_products(t, "?pageSize=100")[1]["items"]
    richest = max(all_p, key=lambda x: x["currentStock"])
    stock = richest["currentStock"]
    tx_before = api("GET", "/api/stock-transactions?pageSize=1", token=t)[1]["total"]

    def out_full(_):
        return api("POST", "/api/stock-transactions",
                   {"requestId": rid(), "productId": richest["id"], "type": "OUT", "quantity": stock}, t)
    with ThreadPoolExecutor(max_workers=2) as pool:
        pair = list(pool.map(out_full, range(2)))
    statuses = sorted(x[0] for x in pair)
    loser = pair[0] if pair[1][0] == 201 else pair[1]
    winner = pair[0] if pair[0][0] == 201 else pair[1]
    final = api("GET", "/api/products/%s" % richest["id"], token=t)[1]["currentStock"]
    loser_ok = (loser[0] == 400 and loser[1]["code"] == "INSUFFICIENT_STOCK") or \
               (loser[0] == 409 and loser[1]["code"] == "STOCK_CONFLICT")
    rec("E09", "2 concurrent OUT of full stock: one 201, one 4xx, stock 0",
        statuses == [201, loser[0]] and loser_ok and winner[1]["newStock"] == 0 and final == 0
        and api("GET", "/api/stock-transactions?pageSize=1", token=t)[1]["total"] == tx_before + 1,
        "statuses=%s loser=%s/%s final=%s" % (statuses, loser[0], loser[1].get("code"), final))

    # ---- E13 idempotency (1) ----
    p4 = find_by_code(all_p, "P004")
    request_id = rid()
    tx_before = api("GET", "/api/stock-transactions?pageSize=1", token=t)[1]["total"]
    body = {"requestId": request_id, "productId": p4["id"], "type": "IN", "quantity": 3}
    r1 = api("POST", "/api/stock-transactions", body, t)
    r2 = api("POST", "/api/stock-transactions", body, t)
    r3 = api("POST", "/api/stock-transactions", dict(body, quantity=9), t)
    tx_after = api("GET", "/api/stock-transactions?pageSize=1", token=t)[1]["total"]
    rec("E13", "replay returns first result, diff payload 409, single txn",
        r1[0] == 201 and r2[0] in (200, 201) and r2[1]["id"] == r1[1]["id"]
        and r3[0] == 409 and r3[1]["code"] == "IDEMPOTENCY_CONFLICT" and tx_after == tx_before + 1,
        "first=%s replay=%s conflict=%s txns+1=%s" % (r1[0], r2[0], r3[0], tx_after - tx_before == 1))

    # ---- E10 transaction query (1) ----
    p2_id = p2["id"]
    f1 = api("GET", "/api/stock-transactions?productId=%s&pageSize=100" % p2_id, token=t)[1]
    f2 = api("GET", "/api/stock-transactions?productId=%s&type=OUT&pageSize=100" % p2_id, token=t)[1]
    desc = all(f1["items"][i - 1]["createdAt"] >= f1["items"][i]["createdAt"]
               for i in range(1, len(f1["items"])))
    page1 = api("GET", "/api/stock-transactions?productId=%s&page=1&pageSize=1" % p2_id, token=t)[1]
    page2 = api("GET", "/api/stock-transactions?productId=%s&page=2&pageSize=1" % p2_id, token=t)[1]
    rec("E10", "tx query: filter/DESC/paging/joined names",
        all(x["productId"] == p2_id for x in f1["items"])
        and all(x["type"] == "OUT" for x in f2["items"]) and desc
        and page1["total"] == f1["total"]
        and page1["items"][0]["id"] != page2["items"][0]["id"]
        and all(x["productCode"] and x["productName"] for x in f1["items"]),
        "rows=%s out=%s desc=%s" % (f1["total"], f2["total"], desc))

    # ---- E15 status buckets (1) ----
    now = get_products(t, "?pageSize=100")[1]["items"]
    pick = sorted(now, key=lambda x: -x["currentStock"])[:3]
    for p, target in zip(pick, [0, None, "min+1"]):
        if target is None:
            target = p["minimumStockLevel"]
        elif target == "min+1":
            target = p["minimumStockLevel"] + 1
        api("POST", "/api/stock-transactions", {"requestId": rid(), "productId": p["id"],
                                                "type": "ADJUST", "quantity": target}, t)
    now = get_products(t, "?pageSize=100")[1]["items"]
    local = {
        "out": sorted(p["id"] for p in now if p["currentStock"] == 0),
        "low": sorted(p["id"] for p in now if 0 < p["currentStock"] <= p["minimumStockLevel"]),
        "normal": sorted(p["id"] for p in now if p["currentStock"] > p["minimumStockLevel"]),
    }
    api_out = sorted(p["id"] for p in get_products(t, "?stockStatus=out&pageSize=100")[1]["items"])
    api_low = sorted(p["id"] for p in get_products(t, "?stockStatus=low&pageSize=100")[1]["items"])
    api_norm = sorted(p["id"] for p in get_products(t, "?stockStatus=normal&pageSize=100")[1]["items"])
    rec("E15", "status buckets mutually exclusive & match API filters",
        len(local["out"]) + len(local["low"]) + len(local["normal"]) == 9
        and local["out"] == api_out and local["low"] == api_low and local["normal"] == api_norm,
        "out=%s low=%s normal=%s" % (len(local["out"]), len(local["low"]), len(local["normal"])))

    # ---- E16 dashboard integrity (1) ----
    tx_all = api("GET", "/api/stock-transactions?pageSize=100", token=t)[1]["items"]
    now = get_products(t, "?pageSize=100")[1]["items"]
    dash = api("GET", "/api/dashboard", token=t)[1]
    metrics_ok = (dash["totalProducts"] == len(now)
                  and dash["totalStockQuantity"] == sum(p["currentStock"] for p in now)
                  and dash["lowStockProducts"] == len(local["low"])
                  and dash["outOfStockProducts"] == len(local["out"]))
    days = {}
    import datetime
    for i in range(6, -1, -1):
        days[(datetime.datetime.utcnow() - datetime.timedelta(days=i)).strftime("%Y-%m-%d")] = 0
    for x in tx_all:
        d = x["createdAt"][:10]
        if d in days:
            if x["type"] == "IN":
                days[d] += x["quantity"]
            elif x["type"] == "OUT":
                days[d] -= x["quantity"]
            else:
                days[d] += x["newStock"] - x["previousStock"]
    got = [d["netChange"] for d in dash["dailyNetChanges"]]
    rec("E16", "dashboard metrics match, 7-day zero-filled net changes match",
        metrics_ok and len(dash["dailyNetChanges"]) == 7 and got == list(days.values())
        and 0 < len(dash["recentTransactions"]) <= 10,
        "got=%s exp=%s" % (got, list(days.values())))

    # ---- E17 reports (1) ----
    rp = api("GET", "/api/reports/products?pageSize=100", token=t)[1]
    value_ok = all(abs(it["stockAmount"] - next(p for p in now if p["id"] == it["id"])["unitPrice"]
                       * next(p for p in now if p["id"] == it["id"])["currentStock"]) < 0.005
                   for it in rp["items"])
    rt = api("GET", "/api/reports/transactions?pageSize=100", token=t)[1]
    tx_all_total = api("GET", "/api/stock-transactions?pageSize=100", token=t)[1]["total"]
    desc = all(rt["items"][i - 1]["createdAt"] >= rt["items"][i]["createdAt"]
               for i in range(1, len(rt["items"])))
    rec("E17", "reports: stockAmount=price*stock, txn report consistent & DESC",
        value_ok and rp["total"] == 9 and rt["total"] == tx_all_total and desc,
        "rows=%s txns=%s/%s desc=%s" % (len(rp["items"]), rt["total"], tx_all_total, desc))

    failed = [cid for cid, ok in results if not ok]
    print("\nSMOKE: %d/%d PASS%s" % (len(results) - len(failed), len(results),
                                     "" if not failed else ", FAILED: " + ",".join(failed)))
    sys.exit(2 if failed else 0)


if __name__ == "__main__":
    main()
