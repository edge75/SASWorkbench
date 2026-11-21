"""Classify extracted GSA test-script cases by what each one demands.

Deliberately not "can we run this?" but "what does this case ask for?", because the
first is an opinion and the second is readable off the case. The demands are then
checked against what this build actually provides, which is a measurement that moves
on its own as the build grows.

Demands recognised:
  decoder.lpXX     a long poll the case names, written (XX) by these documents
  decoder.eventXX  an exception the case names, written {XX}
  stimulus.*       something that has to happen at the machine
  machine.*        something only a person at the machine can do
  hostsim.*        an action of the GSA tool that has no counterpart here

Usage:
    python ts_classify.py cases.json [--csv out.csv]
"""
import re
import json
import argparse
import collections

# What this build can do, from `sasctl catalogue` and the stimulus backends.
DECODABLE_LP = set("""01 02 08 0E 0F 10 11 12 13 14 15 16 17 18 19 1A 1B 1C 1E 1F 20 2A 2B
2C 2D 2F 31 32 33 34 35 36 37 38 39 3A 3B 3C 3D 3E 3F 40 41 42 43 44 45 46 47 48 49 4A 4C
4D 4F 50 51 52 53 54 55 56 57 6F 70 71 74 7B 7E 83 84 85 86 87 8E 8F 90 9A A0 AF B1 B2 B3
B4 B5""".split())
# The ten exceptions that carry a payload; this build reads all of them. Every other
# exception IS the whole event in three bytes, so there is nothing to decode and a case
# naming one is not blocked on a decoder -- only on producing it. See `sasctl catalogue
# events`.
DECODABLE_EVENT = set("4F 7C 7E 7F 88 8A 8B 8C 9E 9F".split())
PROVIDED_STIMULUS = {
    "stimulus.bill", "stimulus.play", "stimulus.ticket", "stimulus.settle",
    "stimulus.real_time_events", "stimulus.button.collect",
    "stimulus.accounting_denomination",
    "stimulus.door.main", "stimulus.door.slot", "stimulus.door.logic",
    "stimulus.door.drop", "stimulus.door.cashbox", "stimulus.door.belly",
    "stimulus.door.stacker", "stimulus.door.card_cage",
}

# A code in braces is always an exception. A code in parentheses is a long poll
# everywhere EXCEPT the Required Exceptions chapter, where the titles name exception
# codes the same way -- "AC Power On/Off (17, 18)" is a pair of exceptions, not polls.
LP_IN_TITLE = re.compile(r"\(([0-9A-F]{2})(?:\s*[,-]\s*([0-9A-F]{2}))*\)")
EV_IN_TITLE = re.compile(r"\{([0-9A-F]{2})\}")
CODES_IN_PARENS = re.compile(r"\(([0-9A-F]{2}(?:\s*[,-]\s*[0-9A-F]{2})*)\)")
EXCEPTION_CHAPTERS = {"4"}          # TSE0001 chapter 4, Required Exceptions
# "F1 = 6F Basic Meters 1, address 1" -- the One Key Polls column names the command
ONEKEY = re.compile(r"\bF\d{1,2}\s*=\s*([0-9A-F]{2})\b")

STIMULUS_WORDS = [
    (r"\bplay\b|\bgame play\b|spin|wager|place a bet|max bet|play \d+ game", "stimulus.play"),
    (r"insert (a )?bill|bill in|accept a bill|insert (a )?note|bill accept", "stimulus.bill"),
    (r"insert (a )?ticket|ticket in|redeem (a )?ticket|ticket insert", "stimulus.ticket"),
    (r"\bcash ?out\b|press (the )?cashout|collect button", "stimulus.button.collect"),
    (r"open (the )?door|close (the )?door|door open|door close", "stimulus.door.main"),
    (r"hand ?pay|jackpot reset|key.?off|cancel credit", "stimulus.handpay"),
    (r"\bAFT\b|transfer to (the )?(egm|host)|funds transfer", "machine.aft"),
    (r"progressive", "stimulus.progressive"),
    (r"legacy bonus|bonus award|\bMJT\b|multiplied jackpot", "machine.legacy_bonus"),
    (r"tournament", "stimulus.tournament"),
    (r"authenticat|component list", "stimulus.authentication"),
    (r"\bhopper\b", "stimulus.hopper"),
    (r"\breel\b", "stimulus.reel"),
    (r"printer|print (a )?ticket|paper out|paper low", "printer.wallet"),
]

MACHINE_WORDS = [
    (r"RAM clear|memory clear|master reset|clear (the )?(NV)?RAM", "machine.nvram_clear"),
    (r"power ?cycle|power (the )?EGM (off|down)|turn (the )?EGM off|AC power", "machine.power_cycle"),
    (r"audit menu|operator menu|configuration menu|enter (the )?menu|display meters",
     "machine.audit_menu"),
    (r"configure (the )?EGM|EGM configuration|set the EGM to|operator change|"
     r"EGM (is )?configured (for|to)|(dis|en)able EGM for|(dis|en)able (for|using) |"
     r"set EGM \w+ (limit|high|low)|configure (to|for) \w", "machine.config_file"),
    # A criterion that compares the host's number against the machine's own screen needs
    # a person looking at the machine. The wire half is assertable; the comparison is not.
    (r"display panel|EGM display|at the EGM|on the EGM screen|EGM meters? (display|screen)|"
     r"matches .{0,40}meter at ", "machine.display_panel"),
    (r"multi[- ]?games?\b|multiple games|enabled games|for multi games", "machine.multiple_games"),
    (r"multi[- ]?denom", "machine.multiple_denominations"),
    (r"check with (the )?manufacturer|as provided by (the )?manufacturer|"
     r"manufacturer('s)? (documentation|specification)", "machine.manufacturer_data"),
    (r"create a (critical )?memory error|EEPROM error|EPROM error|CMOS error|low battery|"
     r"corrupt", "machine.fault_injection"),
    (r"attendant|technician|service (the )?machine|out of service", "machine.attendant"),
]

HOSTSIM_WORDS = [
    (r"inter-?byte delay|oscilloscope|timing details|response time|baud", "hostsim.line_timing"),
]


def demands(case):
    """What one case asks for, as a set of capability keys."""
    # HostSim Set-up, One Key Polls, Polling Loops, Filters and Meter Set describe the
    # *host's* configuration. That is this tool's own job and creates no demand on the
    # machine, so scanning it for machine words invents requirements the case never had.
    text = " ".join(
        [case.get("title") or "", case.get("egm setup") or ""]
        + [s["text"] for s in case["steps"]]
        + [p for s in case["steps"] for p in s["pass"]]
        + case["notes"])

    out = set()
    title = case.get("title") or ""
    chapter = (case.get("section") or "").split(".")[0]
    kind = "event" if chapter in EXCEPTION_CHAPTERS else "lp"
    for group in CODES_IN_PARENS.findall(title):
        for code in re.findall(r"[0-9A-F]{2}", group):
            out.add(f"decoder.{kind}{code}")
    for code in EV_IN_TITLE.findall(title):
        out.add(f"decoder.event{code}")
    for code in ONEKEY.findall(case.get("one key polls") or ""):
        # "F1 = 00 General Poll, address 0" names the general/broadcast poll, which the
        # engine sends natively. It is not a long poll and not a decoder gap.
        if code != "00":
            out.add(f"decoder.lp{code}")
    for pattern, key in STIMULUS_WORDS + MACHINE_WORDS + HOSTSIM_WORDS:
        if re.search(pattern, text, re.I):
            out.add(key)
    return out


def met(key):
    if key.startswith("decoder.lp"):
        return key[len("decoder.lp"):] in DECODABLE_LP
    if key.startswith("decoder.event"):
        # An exception without a payload is three bytes and wholly understood, so it is
        # never the thing blocking a case. Only the ten with payloads can be.
        return True
    if key.startswith("stimulus."):
        return key in PROVIDED_STIMULUS
    if key == "printer.wallet":
        return True
    return False          # machine.* and hostsim.* need a person or a tool we lack


# Demands no build can ever satisfy here: they need a person at a machine, or a number
# only the manufacturer holds. Everything else -- including configuring the machine and
# clearing its memory -- is buildable against the software peer, which is constructed in
# whatever configuration a case declares. Against a REAL machine those same demands need
# the audit menu and therefore a person; that is why the two peers are counted apart.
NEEDS_A_PERSON = {
    "machine.display_panel", "machine.attendant", "machine.manufacturer_data",
    "machine.fault_injection", "hostsim.line_timing",
}
# Buildable for the software peer, but a person's job on a real machine.
PERSON_ON_REAL_MACHINE = {
    "machine.config_file", "machine.nvram_clear", "machine.power_cycle",
    "machine.multiple_games", "machine.multiple_denominations", "machine.audit_menu",
}


def klass(unmet):
    """A: runnable now. B: needs something buildable. C: needs a person."""
    if not unmet:
        return "A"
    if unmet & NEEDS_A_PERSON:
        return "C"
    return "B"


def klass_real(unmet):
    """The same question asked of a real gaming machine rather than the software peer."""
    if not unmet:
        return "A"
    if unmet & (NEEDS_A_PERSON | PERSON_ON_REAL_MACHINE):
        return "C"
    return "B"


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("cases")
    ap.add_argument("--csv")
    a = ap.parse_args()
    cases = json.load(open(a.cases, encoding="utf-8"))

    by_doc = collections.defaultdict(lambda: collections.Counter())
    gap = collections.Counter()
    rows = []
    for c in cases:
        unmet = {k for k in demands(c) if not met(k)}
        k, kr = klass(unmet), klass_real(unmet)
        doc = c["doc"][14:-4].split("_EGM_TS_")[-1]
        by_doc[doc][k] += 1
        by_doc[doc]["r" + kr] += 1
        for u in unmet:
            gap[u] += 1
        rows.append((doc, c["section"], c["title"], k, kr, ";".join(sorted(unmet))))

    print("                        --- software machine ---   --- real machine ---")
    print(f"{'document':22s} {'A':>5s} {'B':>5s} {'C':>5s} {'':4s} {'A':>5s} {'B':>5s} {'C':>5s}"
          f" {'total':>7s}")
    tot = collections.Counter()
    for doc in sorted(by_doc):
        n = by_doc[doc]
        tot.update(n)
        print(f"{doc:22s} {n['A']:5d} {n['B']:5d} {n['C']:5d} {'':4s} "
              f"{n['rA']:5d} {n['rB']:5d} {n['rC']:5d} {n['A']+n['B']+n['C']:7d}")
    print(f"{'TOTAL':22s} {tot['A']:5d} {tot['B']:5d} {tot['C']:5d} {'':4s} "
          f"{tot['rA']:5d} {tot['rB']:5d} {tot['rC']:5d} {tot['A']+tot['B']+tot['C']:7d}")

    print("\nmost common unmet demands")
    for k, n in gap.most_common(25):
        print(f"  {n:4d}  {k}")

    if a.csv:
        import csv
        with open(a.csv, "w", newline="", encoding="utf-8") as fh:
            w = csv.writer(fh)
            w.writerow(["document", "section", "title", "class_software", "class_real", "unmet"])
            w.writerows(rows)
        print(f"\n-> {a.csv}")
