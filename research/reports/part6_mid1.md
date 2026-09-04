
#### 12.1.1 Long polls the tool does NOT know

Present in SAS but absent from `[Long Poll Commands]`, and therefore not selectable from the
drop-down (some are still reachable through other paths):

| Code | Reachable elsewhere? |
|---|---|
| `$1D` | yes — defined in `[Electronic Funds Transfer]` (Cumulative meters) |
| `$22`–`$29`, `$62`–`$6B` | yes — `[Electronic Funds Transfer]` |
| `$2F`… ranges `$59`–`$6E` (except `$5x` gaps), `$72`, `$75`, `$77`–`$7A`, `$80`–`$82`, `$86`, `$88`–`$89`, `$8D`, `$91`–`$93`, `$9B`–`$9F`, `$A1`–`$A3`, `$A5`–`$AE` (except `$A8`,`$AA`), `$B0`, `$B8`+ | **no** |
| `$72` (AFT transfer funds) | via `[Data Structures]` + `[Message Structures]`, and the `Transfer Funds` tab — **never as a catalogue entry** |
| `$75` (Update receipt data) | via the AFT `Receipt` tab button only |
| `$A5` (ASDC) | via the empty `[ASDC]` section; decoder exists in `[Messages]` |

The most notable omission is **`$72` itself** — the tool's flagship AFT command has no
`[Long Poll Commands]` row at all; it is built entirely from `[Data Structures]`. Any
replacement must keep that separation in mind: **the catalogue is not the whole command set.**

### 12.2 `[Electronic Funds Transfer]` — full table

18 entries, lines 661–701. These are the only entries in the file that use the TRN and ACK
flags. Legacy EFT (pre-AFT); `$22`–`$29` work in dollars, `$62`–`$6B` in credits.

