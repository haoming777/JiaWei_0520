# -*- coding: utf-8 -*-
# Test for "当前班次未生产" marker SQL. Mirrors:
#   AsyncDatabaseRecorder.CheckAndMarkNoProductionShifts (30s tick: delete stale markers,
#   then insert markers for shifts with no detail — window already ended OR the current shift)
#   + SaveRecordToDatabase commit-time delete (marker removed the moment production lands)
import sqlite3, datetime

MARKER = "当前班次未生产"          # 当前班次未生产
YE = "夜班"; ZAO = "早班"; ZHONG = "中班"  # 夜班/早班/中班
RETENTION = 30

def shift_of(dt):
    h = dt.hour
    if 8 <= h <= 15: return ZAO
    if 16 <= h <= 23: return ZHONG
    return YE

db = sqlite3.connect(":memory:")

# --- schema (minimal mirror of production) ---
db.execute("""CREATE TABLE production_records_detail (
    id INTEGER PRIMARY KEY AUTOINCREMENT, p_time TEXT, p_date TEXT, p_shift TEXT,
    p_shift_date TEXT, sku TEXT)""")
db.execute("""CREATE TABLE production_records_summary (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    p_date TEXT NOT NULL, p_shift TEXT NOT NULL, sku TEXT NOT NULL,
    total_count INTEGER DEFAULT 0, ok_count INTEGER DEFAULT 0, ng_count INTEGER DEFAULT 0,
    ng_yiwu INTEGER DEFAULT 0, ng_gaigai INTEGER DEFAULT 0, ng_guankou INTEGER DEFAULT 0,
    ng_zheng INTEGER DEFAULT 0, ng_fan INTEGER DEFAULT 0, ng_baoguan INTEGER DEFAULT 0,
    ng_xiekou INTEGER DEFAULT 0, ng_weijian INTEGER DEFAULT 0, ng_hunhe INTEGER DEFAULT 0,
    ng_pcode INTEGER DEFAULT 0, ng_sebiao INTEGER DEFAULT 0,
    continuous_exclude_count INTEGER DEFAULT 0, yield_rate REAL DEFAULT 0,
    UNIQUE(p_date, p_shift, sku))""")
db.execute("""CREATE TABLE app_shift_presence (
    p_shift_date TEXT NOT NULL, p_shift TEXT NOT NULL,
    first_seen TEXT, last_seen TEXT, PRIMARY KEY (p_shift_date, p_shift))""")

now = datetime.datetime.now()
today = now.strftime("%Y-%m-%d")
yesterday = (now - datetime.timedelta(days=1)).strftime("%Y-%m-%d")
day_before = (now - datetime.timedelta(days=2)).strftime("%Y-%m-%d")
cur_shift = shift_of(now)
print("now:", now, "| today:", today, "| cur shift:", cur_shift)

# --- seed presence ---
# S1: yesterday ZAO, ended, no detail  -> expect MARKER
db.execute("INSERT INTO app_shift_presence VALUES (?,?,?,?)", (yesterday, ZAO, "t", "t"))
# S2: yesterday ZHONG, ended, HAS detail -> expect NO marker
db.execute("INSERT INTO app_shift_presence VALUES (?,?,?,?)", (yesterday, ZHONG, "t", "t"))
db.execute("INSERT INTO production_records_detail (p_time,p_date,p_shift,p_shift_date,sku) VALUES (?,?,?,?,?)",
           (yesterday + " 18:00:00", yesterday, ZHONG, yesterday, "SKU-A"))
# S3: today cur shift, presence, no detail -> expect MARKER (current shift: insert immediately, window not ended)
db.execute("INSERT INTO app_shift_presence VALUES (?,?,?,?)", (today, cur_shift, "t", "t"))
# S4: day_before YE, ended, no detail -> expect MARKER
db.execute("INSERT INTO app_shift_presence VALUES (?,?,?,?)", (day_before, YE, "t", "t"))
# S5: day_before ZAO has detail AND stale marker already present -> expect marker DELETED
db.execute("INSERT INTO app_shift_presence VALUES (?,?,?,?)", (day_before, ZAO, "t", "t"))
db.execute("INSERT INTO production_records_detail (p_time,p_date,p_shift,p_shift_date,sku) VALUES (?,?,?,?,?)",
           (day_before + " 10:00:00", day_before, ZAO, day_before, "SKU-B"))
db.execute("INSERT INTO production_records_summary (p_date,p_shift,sku,total_count,ok_count,ng_count) VALUES (?,?,?,?,?,?)",
           (day_before, ZAO, MARKER, 0, 0, 0))
# S6: ancient shift (>retention) presence, no detail -> expect NO marker (retention guard)
ancient = (now - datetime.timedelta(days=RETENTION + 5)).strftime("%Y-%m-%d")
db.execute("INSERT INTO app_shift_presence VALUES (?,?,?,?)", (ancient, ZAO, "t", "t"))

# --- SQL 1: delete stale markers (verbatim from C#) ---
db.execute("""DELETE FROM production_records_summary
WHERE sku = ?
  AND EXISTS (SELECT 1 FROM production_records_detail d
              WHERE d.p_shift_date = production_records_summary.p_date
                AND d.p_shift = production_records_summary.p_shift)""", (MARKER,))

# --- SQL 2: insert missing markers (verbatim from C#, 19 cols / 19 values;
#     params: marker, today, cur_shift, marker) ---
SQL2 = """INSERT OR IGNORE INTO production_records_summary
    (p_date, p_shift, sku, total_count, ok_count, ng_count,
     ng_yiwu, ng_gaigai, ng_guankou, ng_zheng, ng_fan,
     ng_baoguan, ng_xiekou, ng_weijian, ng_hunhe, ng_pcode, ng_sebiao,
     continuous_exclude_count, yield_rate)
SELECT p_shift_date, p_shift, ?,
       0, 0, 0,
       0, 0, 0, 0, 0,
       0, 0, 0, 0, 0, 0,
       0, 0
FROM app_shift_presence
WHERE (datetime(CASE p_shift
            WHEN '""" + YE + """' THEN p_shift_date || ' 08:00:00'
            WHEN '""" + ZAO + """' THEN p_shift_date || ' 16:00:00'
            WHEN '""" + ZHONG + """' THEN date(p_shift_date, '+1 day') || ' 00:00:00'
        END) <= datetime('now', 'localtime')
        OR (p_shift_date = ? AND p_shift = ?))
  AND p_shift_date >= date('now', 'localtime', '-""" + str(RETENTION) + """ day')
  AND NOT EXISTS (SELECT 1 FROM production_records_detail d
                  WHERE d.p_shift_date = app_shift_presence.p_shift_date
                    AND d.p_shift = app_shift_presence.p_shift)
  AND NOT EXISTS (SELECT 1 FROM production_records_summary s
                  WHERE s.p_date = app_shift_presence.p_shift_date
                    AND s.p_shift = app_shift_presence.p_shift
                    AND s.sku = ?)"""

def run_tick():
    db.execute(SQL2, (MARKER, today, cur_shift, MARKER))

run_tick()

rows = db.execute("SELECT p_date, p_shift, sku FROM production_records_summary ORDER BY p_date, p_shift, sku").fetchall()
print("--- summary rows after tick ---")
for r in rows: print(r)

expected = {
    (yesterday, ZAO): True,      # S1 -> marker
    (yesterday, ZHONG): False,   # S2 -> no (has detail)
    (today, cur_shift): True,    # S3 -> marker (current shift, inserted immediately)
    (day_before, YE): True,      # S4 -> marker
    (day_before, ZAO): False,    # S5 -> stale marker deleted, has detail
    (ancient, ZAO): False,       # S6 -> no (retention guard)
}
ok = True
for (d, s), want in expected.items():
    got = any(r[0] == d and r[1] == s and r[2] == MARKER for r in rows)
    status = "PASS" if got == want else "FAIL"
    if got != want: ok = False
    print(f"{status}: {d} {s} marker={'yes' if got else 'no'} expected={'yes' if want else 'no'}")

# idempotency: run SQL 2 again, count must not change
n1 = db.execute("SELECT COUNT(*) FROM production_records_summary WHERE sku = ?", (MARKER,)).fetchone()[0]
run_tick()
n2 = db.execute("SELECT COUNT(*) FROM production_records_summary WHERE sku = ?", (MARKER,)).fetchone()[0]
print(f"idempotency: marker rows {n1} -> {n2} (expect equal)")
if n2 != n1: ok = False

# --- S7: production arrives in the current shift -> commit-time delete removes the marker ---
db.execute("INSERT INTO production_records_detail (p_time,p_date,p_shift,p_shift_date,sku) VALUES (?,?,?,?,?)",
           (today + " 12:00:00", today, cur_shift, today, "SKU-C"))
# mirror of the C# commit-time delete in SaveRecordToDatabase (record.ShiftDateStr / record.Shift)
db.execute("DELETE FROM production_records_summary WHERE sku = ? AND p_date = ? AND p_shift = ?",
           (MARKER, today, cur_shift))
n3 = db.execute("SELECT COUNT(*) FROM production_records_summary WHERE sku = ? AND p_date = ? AND p_shift = ?",
                (MARKER, today, cur_shift)).fetchone()[0]
print(f"S7: marker after production = {n3} (expect 0)")
if n3 != 0: ok = False
# next 30s tick must NOT re-insert it (detail now exists)
run_tick()
n4 = db.execute("SELECT COUNT(*) FROM production_records_summary WHERE sku = ? AND p_date = ? AND p_shift = ?",
                (MARKER, today, cur_shift)).fetchone()[0]
print(f"S7b: marker after re-tick = {n4} (expect 0, no re-insert)")
if n4 != 0: ok = False

print("ALL PASS" if ok else "SOME FAILED")
db.close()
