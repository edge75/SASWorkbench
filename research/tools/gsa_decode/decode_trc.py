r"""*.trc  --  "Trace/Log -> Save Trace to Text File" output of SASHostSim.exe.

Plain ANSI text, CRLF line endings, no length limit.  Grammar:

    file      := header CRLF CRLF tsline CRLF CRLF body
    header    := "Trace started at " MM/DD/YY " " HH:MM:SS
    tsline    := "Timestamp is " mode
                 mode is exactly one of the six Trace/Log -> Timestamp Format
                 menu items:
                     "absolute time"
                     "relative to start of last message"
                     "relative to start of last poll"
                     "relative to end of last message"
                     "relative to end of last poll"
                     "wall-clock time"
    body      := ( message | separator | pause )*
    message   := dataline CRLF textline CRLF
    dataline  := side stamp raw
                   side  : "HOST" or "EGM", left-aligned in a 6-char field
                   stamp : right-aligned decimal with one fractional digit
                           ("  34.5"), or "-------" for the first message
                   raw   : the message bytes, "%02X " each.
                           HOST lines wrap the FIRST byte in square brackets --
                           "[01] A0 00 00 6C 13" -- because that byte carries
                           the SAS wakeup (9th) bit.  EGM lines have no
                           brackets.
    textline  := indented decoded text: "<template name>: <parm> = <value>; ..."
                 Indent is 17 spaces for the five relative modes and 20 spaces
                 for "absolute time" (the stamp field is wider).
    separator := "----------"         (inserted where the user cleared/split
                                       the trace, and at end of file)
    pause     := "***PAUSE***"        (Trace/Log -> Pause Trace)

Everything needed to replay a session is present: direction, timestamp,
raw bytes (minus the wakeup bit, which is implied by the brackets) and the
simulator's own decoding of the message.
"""
import sys, os, re

HDR = re.compile(r'^Trace started at (\d\d)/(\d\d)/(\d\d) (\d\d):(\d\d):(\d\d)\s*$')
TSM = re.compile(r'^Timestamp is (.+?)\s*$')
MSG = re.compile(r'^(HOST|EGM)\s+(-{3,}|[\d.]+)\s+(.*)$')


def parse(path):
    txt = open(path, 'r', encoding='latin-1', newline='').read()
    lines = txt.split('\r\n') if '\r\n' in txt else txt.split('\n')
    out = dict(path=path, started=None, mode=None, events=[])
    pend = None
    for ln in lines:
        if not ln.strip():
            continue
        m = HDR.match(ln)
        if m:
            out['started'] = '20%s-%s-%s %s:%s:%s' % (m.group(3), m.group(1), m.group(2),
                                                      m.group(4), m.group(5), m.group(6))
            continue
        m = TSM.match(ln)
        if m:
            out['mode'] = m.group(1); continue
        if ln.strip() == '----------':
            out['events'].append(dict(kind='separator')); pend = None; continue
        if ln.strip() == '***PAUSE***':
            out['events'].append(dict(kind='pause')); pend = None; continue
        m = MSG.match(ln)
        if m:
            side, stamp, raw = m.group(1), m.group(2), m.group(3).strip()
            wake = raw.startswith('[')
            hexs = raw.replace('[', '').replace(']', '').split()
            pend = dict(kind='message', side=side,
                        stamp=None if stamp.startswith('-') else float(stamp),
                        wakeup_on_first_byte=wake,
                        bytes=[int(h, 16) for h in hexs if len(h) == 2], text='')
            out['events'].append(pend)
            continue
        if pend is not None:                     # continuation = decoded text
            pend['text'] = (pend['text'] + ' ' + ln.strip()).strip()
    return out


def main(path):
    t = parse(path)
    print('=== %s ===' % path)
    print('started : %s' % t['started'])
    print('stamps  : %s' % t['mode'])
    msgs = [e for e in t['events'] if e['kind'] == 'message']
    print('events  : %d (%d messages, %d HOST, %d EGM)' % (
        len(t['events']), len(msgs),
        sum(1 for e in msgs if e['side'] == 'HOST'), sum(1 for e in msgs if e['side'] == 'EGM')))
    for e in t['events']:
        if e['kind'] != 'message':
            print('  -- %s --' % e['kind']); continue
        print('  %-4s %9s %-4s %-38s %s' % (
            e['side'], '' if e['stamp'] is None else '%.1f' % e['stamp'],
            'WAKE' if e['wakeup_on_first_byte'] else '',
            ' '.join('%02X' % b for b in e['bytes']), e['text']))


if __name__ == '__main__':
    main(sys.argv[1] if len(sys.argv) > 1
         else r'C:\igs\GSA SAS Host Simulator v1.1\step2.trc')
