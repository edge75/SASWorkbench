"""Run every decoder over an installed GSA SAS Host Simulator v1.1 tree.

    python run_all.py "C:\igs\GSA SAS Host Simulator v1.1" [outdir]

Writes one .txt per source file into <outdir> (default: ./decoded).
"""
import sys, os, glob, io, contextlib
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import decode_simple, decode_loopdefs, decode_metersets, decode_polldefs
import decode_hostconfig, decode_log, decode_trc


def run(fn, src, dst):
    buf = io.StringIO()
    try:
        with contextlib.redirect_stdout(buf):
            fn(src)
        status = 'ok'
    except Exception as e:
        buf.write('\n*** DECODER ERROR: %r ***\n' % (e,))
        status = 'FAILED'
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    open(dst, 'w', encoding='utf-8').write(buf.getvalue())
    print('%-8s %-58s -> %s' % (status, os.path.relpath(src, BASE), os.path.relpath(dst, OUT)))


if __name__ == '__main__':
    BASE = sys.argv[1] if len(sys.argv) > 1 else r'C:\igs\GSA SAS Host Simulator v1.1'
    OUT = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.getcwd(), 'decoded')
    J = os.path.join
    for root in (BASE, J(BASE, 'Defaults')):
        tag = 'live' if root == BASE else 'defaults'
        for name, fn in (('ComPortSettings.dat', decode_simple.comport),
                         ('TestConfigFile.dat', decode_simple.testconfig),
                         ('Tracefilters.dat', decode_simple.tracefilters),
                         ('TicketList.dat', decode_simple.ticketlist),
                         ('MeterSetDefinitions.dat', decode_metersets.main),
                         ('HostConfigs.dat', decode_hostconfig.main)):
            p = J(root, name)
            if os.path.exists(p):
                run(fn, p, J(OUT, tag, name + '.txt'))
        for name in ('LoopDefs1.dat', 'LoopDefs2.dat', 'LoopDefs3.dat',
                     'PollLoopDefinitions.dat', 'UserLoopDefs.dat'):
            p = J(root, name)
            if os.path.exists(p):
                run(decode_loopdefs.main, p, J(OUT, tag, name + '.txt'))
        for name in ('PollDefsSet1.dat', 'PollDefsSet2.dat', 'PollDefsSet3.dat',
                     'UserPollDefinition.dat'):
            p = J(root, name)
            if os.path.exists(p):
                run(decode_polldefs.main, p, J(OUT, tag, name + '.txt'))
        for p in sorted(glob.glob(J(root, 'TestData', '*.dat'))):
            run(decode_hostconfig.main, p, J(OUT, tag, 'TestData', os.path.basename(p) + '.txt'))
    for p in sorted(glob.glob(J(BASE, '*.log'))):
        run(decode_log.decode, p, J(OUT, 'live', os.path.basename(p) + '.txt'))
    for p in sorted(glob.glob(J(BASE, '*.trc')) + glob.glob(J(BASE, '10.*', '*', '*.trc'))):
        rel = os.path.relpath(p, BASE).replace(os.sep, '_').replace(chr(47), '_')
        run(decode_trc.main, p, J(OUT, 'traces', rel + '.txt'))
