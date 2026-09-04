# gsa_decode — decoders for GSA SAS Host Simulator v1.1 data files

Every module documents the exact on-disk layout in its docstring and can be run standalone.
All formats are little-endian; strings are `BYTE length + latin-1 bytes` (`CSTRING`).

| module | files it decodes |
|---|---|
| `gsa_common.py`      | shared cursor / hexdump helpers |
| `decode_simple.py`   | `ComPortSettings.dat`, `TestConfigFile.dat`, `Tracefilters.dat`, `TicketList.dat` |
| `decode_loopdefs.py` | `LoopDefs1/2/3.dat`, `PollLoopDefinitions.dat`, `UserLoopDefs.dat` |
| `decode_metersets.py`| `MeterSetDefinitions.dat` |
| `decode_polldefs.py` | `PollDefsSet1/2/3.dat`, `UserPollDefinition.dat` |
| `decode_hostconfig.py`| `HostConfigs.dat`, `TestData\*.dat` |
| `decode_log.py`      | `SASHostLogNNNNN.log` (binary event log, per-byte timestamps) |
| `decode_trc.py`      | `*.trc` (text trace export) |
| `run_all.py`         | runs everything over an installed tree |

Usage:

    python run_all.py "C:\igs\GSA SAS Host Simulator v1.1" .\decoded
    python decode_polldefs.py "C:/igs/GSA SAS Host Simulator v1.1/PollDefsSet1.dat" -v
    python decode_trc.py "C:/igs/GSA SAS Host Simulator v1.1/10.2/10.2.2/step1.trc"

Validation: over the shipped package all container parsers consume the file to exactly EOF
(110/110 files, 0 failures); `decode_polldefs` additionally cross-checks that the per-record
`pollCode` equals its group's `pollCode` and that the number of record tags found equals the sum of
the declared instance counts.
