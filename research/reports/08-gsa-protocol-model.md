# 08 — The GSA SAS Toolkit XML protocol model (gsa-p0074, SAS Host Simulator v1.1)

**Scope of evidence.** Every claim below is grounded in a file read or a command run under
`C:\igs\GSA SAS Host Simulator v1.1` (referred to below as `$GSA`), plus `C:\Progetti\libsas\include\*.h`
and `C:\Progetti\libsas\src\*.cpp`. Where I could not establish something I write **UNKNOWN**.
Working artefacts (parse scripts, dumps, generated tables) live beside this file in
`…\scratchpad\research\` (`parse.py`, `dump.py`, `tbl.py`, `cmp.py`, `stats.py`, `lpx.py`, `ev.py`,
`tables.md`, `coverage.md`, `events.md`, `resp_dump2.txt`, `poll_dump.txt`, `event_dump2.txt`).

---

## 0. What the package actually is

| File | Size | Root | `SAS_template` count |
|---|---|---|---|
| `$GSA\SASTemplates.dtd` | 3 245 B | — (DTD) | — |
| `$GSA\PollTemplates.xml` | 46 374 B | `SAS_templates` | **137** |
| `$GSA\ResponseTemplates.xml` | 113 292 B | `SAS_templates` | **116** |
| `$GSA\EventTemplates.xml` | 12 908 B | `SAS_templates` | **116** |
| `$GSA\UserPollTemplates.xml` | 210 B | `SAS_templates` | **0** (`<SAS_templates/>`) |
| `$GSA\UserResponseTemplates.xml` | 210 B | `SAS_templates` | **0** (`<SAS_templates/>`) |
| `$GSA\Defaults\PollTemplates.xml` | 46 310 B | `SAS_templates` | 137 |
| `$GSA\Defaults\ResponseTemplates.xml` | 113 292 B | `SAS_templates` | 116 |

All five live files declare `<?xml version="1.0" encoding="ISO-8859-1"?>` and
`<!DOCTYPE SAS_templates SYSTEM "SASTemplates.dtd">`, and all carry the same authorship comment:
`<!-- edited with XMLSPY v5 rel. 4 U … by Bill Turner (Acres Gaming) -->`.

**Defaults diff.** `diff` of `SASTemplates.dtd` and `ResponseTemplates.xml` against their `Defaults\`
copies: byte-identical. `PollTemplates.xml` differs by **exactly one line** — the live file has an extra
`<choice_entry name="Unsupported transfer code" value="05"/>` inside the LP 71 *Redeem ticket*
`Transfer code` enum (line 82). So the shipped-vs-live delta is a single hand-added enum member.
That tells us two things: the files are meant to be hand-edited, and `Defaults\` is a pristine restore copy.

**Who reads them.** `strings SASHostSim.exe` shows the loader reads, in this order:
`\PollTemplates.xml`, `\UserPollTemplates.xml`, `\ResponseTemplates.xml`, `\UserResponseTemplates.xml`,
`\EventTemplates.xml`, via `Msxml2.DOMDocument`. Error strings present:
`Unknown parameter type %s`, `Can't add "%s" -- template %02X named "%s" already exists` /
`Duplicate template`, `Line %d: %s` / `Error text: "%s"` / `XML Parse Error`.
So: the `User*` files are an **additive overlay parsed immediately after the stock file**, keyed by
command byte, and a collision is rejected rather than overriding. There is no write-back path in the
strings — the simulator only reads these files.

---

## 1. Semantic specification of the model

### 1.1 The frame the templates sit inside

The templates describe **only the payload between the command byte and the CRC**. The full wire frame,
verified against `$GSA\step5.trc` and `$GSA\step2.trc` (the simulator's own decoded traces):

```
Host -> EGM :  [addr]  cmd  [len]  <template fields...>  [CRC-lo CRC-hi]
EGM  -> Host :  addr    cmd  [len]  <template fields...>  [CRC-lo CRC-hi]
```

* `[addr]` in brackets in a trace line means the 9th "wakeup" bit was set on that byte — e.g.
  `HOST 3255138.0   [80]` = RESYNC, `[81]` = general poll, `[01] 1F` = addressed long poll 1F.
  The templates say nothing about the wakeup bit; it is a per-poll UI setting
  (`Wakeup Bits...`, `Set wakeup bit on byte`, `UART wakeup bit is delayed` in the exe's dialogs).
* `[len]` is present **iff** `length="yes"` on the `SAS_template`.
* The trailing CRC-16 is present **iff** `crc="yes"`.

### 1.2 `SAS_template` attributes

```xml
<!ATTLIST SAS_template name CDATA #REQUIRED cmd CDATA #REQUIRED
    crc (yes|no) "no" length (yes|no) "no" multigame (yes|no) "no">
```

**`cmd`** — two hex digits, the SAS command byte. It is the primary key: the loader rejects a second
template with the same `cmd` (`template %02X named "%s" already exists`). Confirmed by data: 137
templates / 137 distinct `cmd` in PollTemplates; 116/116 in both ResponseTemplates and EventTemplates.
In `EventTemplates.xml` `cmd` is **not** a command byte — it is the **exception code** (e.g.
`cmd="11"` = *Slot door was opened*, `cmd="7E"` = *Game Start*). The DTD reuses `SAS_template`
for a different namespace, which is a modelling smell (§4).

**`crc`** — `yes` ⇒ a 2-byte CRC-16 trails the payload. Distribution: 70/137 polls, **114/116** responses,
**0/116** events (exceptions are bare bytes on a general poll, so no CRC — correct).
The two responses with no `crc`, i.e. defaulting to `no`, are:

| cmd | response name |
|---|---|
| `3D` | Send cash out ticket information |
| `6A` | Send available EFT meters |

For `3D` this is demonstrably **wrong**: `lp3D_SendCashoutTicketInformation()` in
`C:\Progetti\libsas\src\lp.cpp` ends with `crcCalc(txPacket.data, length, &txPacket.data[length]); txPacket.length = length + 2;`
— libsas does append a CRC. So a decoder driven purely by GSA's data would mis-frame LP 3D responses.
`6A` is untestable against libsas (EFT is not implemented there) — **UNKNOWN**, but suspicious by symmetry.

**`length`** — `yes` ⇒ a single **length byte immediately after the command byte**, counting *only the
template payload*: it excludes address, command, the length byte itself, and the CRC.
Verified byte-for-byte from `$GSA\step5.trc`:

```
EGM  01 54 09 36 30 32 31 30 30 30 30 31 5B C9
     Send SAS version ID and EGM serial number: SAS Version = "602"; Serial number = "100001";
```
`54` has `length="yes"`; `09` = 3 bytes of `ascii_parm SAS Version` + 6 bytes of serial = 9; `5B C9` = CRC.
Distribution: 15/137 polls, 21/116 responses.

Independently confirmed on the EGM side: `lp6F_SendExtendedMetersForGameN()` sets
`txBuffer[2] = length - 3;` (total minus addr+cmd+len) and then appends the CRC after that.

**`multigame`** — 19/137 poll templates, **0** response templates, **0** event templates.
It is *not* a wire-format modifier. Every one of the 19 begins with a `bcd_parm` game number
(`A0 B4 B5 6F AF 53 83 9A 2F 8C 95 96 97 98 99 09 A4 2D 52` — see `grep -n 'multigame="yes"' PollTemplates.xml`).
The exe's dialog strings `Send to multiple games?` / `Game numbers` / `Multiple command %02X FAILED`
show what it does: it tells the UI *"the first field is a game number, so offer to fan this single poll
out into N polls over an operator-supplied game-number list."* It is an **automation hint attached to the
message type**, and it is exactly the kind of thing SasWorkbench needs — but as a first-class concept,
not a boolean.

> Do not confuse `multigame` with the SAS 6.02 **multi-denom preamble (LP B0)**. The preamble is a
> per-poll-instance flag, stored outside the templates: `$GSA\PollDef.txt` prints
> `Multi-Denom Preamble = Yes|No` for each saved poll (20 x `Yes`, 641 x `No`), the exe has a
> `Multi-Denom Poll` checkbox in the *Define Poll* dialog, and there is **no `B0` template at all**
> in `PollTemplates.xml`. libsas implements it as `lpB0_MultiDenomPreamble()` which unwraps
> `[addr] B0 <len> <denom> <baseCommand> ...` and re-dispatches the inner poll.

### 1.3 Field types — encoding and decoding semantics

Element inventory actually used (from `parse.py` tag histograms):

| element | Poll | Response | Event |
|---|---|---|---|
| `bcd_parm` | 69 | 184 | 9 |
| `binary_parm` | 65 | 85 | 6 |
| `bool_parm` | 71 | 104 | 2 |
| `ascii_parm` | 0 | 10 | 0 |
| `hex_parm` | 2 | 1 | 0 |
| `choice_parm` / `choice_entry` | 33 / 263 | 79 / 1238 | 3 / 94 |
| `bitmap_parm` / `bitmap_field` | 7 / 53 | 12 / 89 | 1 / 3 |
| `optional` | 59 | 73 | 0 |
| `padding` | 0 | 2 | 0 |
| `variable_ascii_parm` | 28 | 8 | 0 |
| `variable_bcd_parm` | 0 | 4 | 0 |
| `variable_hex_parm` | 4 | 3 | 0 |
| `variable_meter_parm` | 2 | 0 | 0 |
| `variable_meter_response` | 0 | 2 | 0 |
| `poll_2F_meter_parm` | 0 | 10 | 0 |
| `aft_receipt_parm` | 1 | 0 | 0 |
| `option_choice_parm` / `option_choice_entry` | 2 / 10 | 1 / 7 | 0 |

#### `bcd_parm{name, size}` — packed BCD, big-endian, 2 digits per byte
`size` is a **byte** count, so the digit count is `2 x size`. Most-significant digit first.
Verified from `$GSA\step3.trc`:

```
EGM  01 7E 07 19 20 26 13 17 25 CD 35
     Send current date and time: Date = 07192026; Time = 131725;
```
Template: `<bcd_parm name="Date" size="4"/><bcd_parm name="Time" size="3"/>` — `07 19 20 26` → `07192026`
(MMDDYYYY), `13 17 25` → `131725` (HHMMSS). And
`01 51 00 09 45 58` → `<bcd_parm name="Number of Games" size="2"/>` → `0009`.
Sizes used: 1,2,3,4,5,8 (polls) and 1,2,3,4,5,8 (responses). The exe formats BCD with `%I64u`/`%I64X`,
so the runtime holds it in a 64-bit integer — implying ≤ 9 bytes / 18 digits in practice.
Error string on malformed input: `%s BAD VALUE (` / ` BAD PARSE`; the *SAS Comms* config page has a
checkbox `Verify data fields (non-BCD data in BCD field, e.g.)` — the toolkit's **only** validation feature.
There is **no scale, no unit, no currency** attribute: a "cents" meter and a "quantity" meter are the same type.

#### `binary_parm{name, size, length, radix}` — unsigned integer
`size` is a byte count; the exe rejects anything outside 1..8 (`%s: invalid size %d (must be between 1 and 8)`)
and formats with `%I64u` / `0x%I64X`. `radix` in {`binary`,`decimal`,`hex`}, default `hex`, and it is
**presentation only** — it does not change the bytes. Verified:

```
EGM  01 1F 51 58 47 61 6D 01 FF 01 00 00 47 61 6D 65 31 35 39 36 36 30 B2 4C
     Game ID = "QX"; Additional ID = "Gam"; Denomination - (01) $0.01; Max Wager = 255;
     Progressive Group = 1; Game Options = 0x0; Paytable = "Game15"; Base percentage = "9660";
```
`Max Wager` is `binary_parm size=1 radix=decimal` → byte `FF` rendered `255`;
`Game Options` is `binary_parm size=2` (radix defaults to hex) → `00 00` rendered `0x0`.

**There is no byte-order attribute, and SAS is not consistent.** libsas reads the LP 74 / LP 72 asset
number and pool ID little-endian (`#define BUILD_DWORDP(x) ((x[0]) | (x[1]<<8) | (x[2]<<16) | (x[3]<<24))`,
`libsas_internal.h:296`; `poolId = rxBuffer[length] | (rxBuffer[length+1] << 8)`, `lp.cpp:5090`), and the
LP 6F meter code little-endian with an explicit comment
(`// The field Requested meter is in binary format, hence, LSB first`), **but** reads the LP 21 ROM
signature seed with `internalReverseCopy((unsigned char*)&seed, &rxBuffer->data[2], 2)` — i.e.
**big-endian**. A codec generated from the GSA DTD alone cannot express that difference; the simulator
must be carrying the convention in code. Which order the *spec* mandates per field is **UNKNOWN** from
this package. This is the single most important gap to fix in our own model.

`length="yes"` on `binary_parm` occurs twice per file and only inside `option_choice_entry`
(the LP 6E CRC16/CRC32 seeds and results). Its meaning is the same as on the `variable_*` types:
**a 1-byte count precedes the value**. So `length` is a uniform "this field is preceded by its own
length byte" modifier, not a type-specific quirk.

#### `bool_parm{name}` — one byte, zero/non-zero
No `size` attribute. Two roles: (a) a standalone byte in a template, (b) the payload of a `bitmap_field`
(then it is one bit). As a standalone byte it is **exactly one byte** — proven by LP 1B (below), where
the byte-budget only closes if the `bool_parm` "Handpay reset to credit meter is available" is one byte.

#### `ascii_parm{name, size}` — fixed-width ASCII, no terminator, no length byte
`Game ID` size 2 → `51 58` = `"QX"`. Sizes used: 2,3,4,6. Error string
`%s: ASCII parameter requires a size`. Space padding / justification rules are **UNKNOWN** from the model —
they are a spec convention the template cannot express.

#### `hex_parm{name, size}` — fixed-width opaque byte blob rendered as hex
Distinct from `binary_parm` in that it has no size ceiling and no integer semantics
(`----Hex param`, `%s: Hex parameter requires a size`). Used only for the 20-byte AFT
`Registration key` (LP 72, LP 73 poll; LP 73 response).

#### `variable_ascii_parm` / `variable_bcd_parm` / `variable_hex_parm {name, size, length}`
`size` here is **not** the on-wire width — it is the **maximum capacity**. The on-wire width comes from
`length`:

* `length="yes"` (36 of 39 uses) → a **1-byte count precedes the data**; the field occupies `1 + n` bytes.
  e.g. LP B7 `<variable_ascii_parm name="Floor location" size="40" length="yes"/>` — capacity 40,
  actual length in the preceding byte. libsas encodes it identically:
  `txBuffer[8] = floorLocationLength; memcpy(&txBuffer[9], floorLocation, floorLocationLength);` (`lp.cpp:10863`).
* `length="no"` (**exactly one use**, `ResponseTemplates.xml:79`) → **no count; the field consumes
  everything left in the message**. Verified: LP 54 response
  `<ascii_parm name="SAS Version" size="3"/><variable_ascii_parm name="Serial number" size="40" length="no"/>`
  decoding `01 54 09 "602" "100001" 5B C9` → serial `"100001"`, 6 bytes = `len(9) - 3`.
  This only works because the template has `length="yes"` at the message level and the field is last.

**How `length="yes"` at message level interacts with a field's `length`.** They are independent and both
present: the message length byte bounds the payload; each length-prefixed field carries its own count
inside that payload. LP B7 poll is the clean example — `crc="yes" length="yes"`, then
`binary_parm Asset number size=4` then a length-prefixed floor location. The message length is
`4 + 1 + n`. The message-level length is what makes trailing `optional` groups decidable (§1.4).

#### `choice_parm{name,size}` + `choice_entry{name,value}` — closed enum over `size` bytes
`size` is restricted to 1..4 (`%s: invalid size %d (must be between 1 and 4)`); every use in all three
files is `size="1"`. Rendered `(%%0%dX) %%s`, i.e. value zero-padded to `2 x size` hex digits then the name:
`Denomination - (01) $0.01`. Unknown values print `%s BAD VALUE (%d)`.

**The `F1` convention.** `value="F1"` / `name="Unsupported value"` appears **32x** in PollTemplates,
**48x** in ResponseTemplates, **3x** in EventTemplates. It is not a SAS code — it is a synthetic
**negative-test vector** baked into the enum so the operator can pick "send an illegal value" from the
same dropdown as the legal ones. This is a genuinely good idea and we should steal it (§4).

#### `bitmap_parm{name,size}` + `bitmap_field{size,offset}` — packed bit fields
`bitmap_parm/@size` is the byte width (1, 2 or 4 in practice). `bitmap_field/@offset` is the
**bit position, LSB = 0**, and `bitmap_field/@size` (default `1`) is the **number of bits**; the extracted
value is `(word >> offset) & ((1 << size) - 1)`, with bit `offset` as the LSB of the extracted value.
A 1-bit field carries a `bool_parm`; a multi-bit field carries a `choice_parm`.

**Verified concretely** from `$GSA\step2.trc` against the LP A0 response template:

```
EGM  01 A0 00 00 D2 CD 03 00 00 00 92 40
```
`Feature 1 = 0xD2 = 1101 0010b`:

| offset (size) | field | bits | decoded by the sim |
|---|---|---|---|
| 0 | Jackpot multiplier enabled | b0 = 0 | `= NO` |
| 1 | AFT bonus awards | b1 = 1 | `= YES` |
| 2 | Legacy bonus awards | b2 = 0 | `= NO` |
| 3 | Tournaments | b3 = 0 | `= NO` |
| 4 | Validation extensions | b4 = 1 | `= YES` |
| 5 (size 2) | Validation style | b6b5 = `10` | `(02) Secure Enhanced` |
| 7 | Ticket redemption | b7 = 1 | `= YES` |

`(0xD2 >> 5) & 0x03 = 0x02` ⇒ **LSB-0, offset is the low bit of the extracted value**. Confirmed again by
`Feature 2 = 0xCD`: `0xCD & 0x03 = 1` → `(01) Won credits metered when won`.
The sim prints `(Bit %d) ` for `bool_parm` and `(Bits %d to %d) ` for `choice_parm`.

**Multi-byte bitmaps are the unresolved case.** `bitmap_parm size="4"` with `offset` 0..31
(LP B5 response `Progressive Levels`, LP 6E response `Available authentication methods`) and
`size="2"` with `offset` 15 (LP 7B) exist, but I found **no trace or log in the package that decodes a
multi-byte bitmap**, so whether bit 8 is the LSB of the *second* byte on the wire or of the *first*
is **UNKNOWN** from this evidence. Any implementation must resolve this from the spec, and our model
should make it explicit rather than implicit.

#### `padding{size}` — bytes on the wire that are consumed and discarded
Two uses only, both in responses. Error strings `BAD PADDING SIZE`, `Padding parameter requires a size`.

`padding` is where GSA's model quietly *loses data*. LP 1B response template:
```xml
<binary_parm name="Progressive group" size="1" radix="decimal"/>
<binary_parm name="Progressive level"  size="1" radix="hex"/>
<bcd_parm    name="Handpay amount"     size="5"/>
<bcd_parm    name="Partial pay amount" size="2"/>
<bool_parm   name="Handpay reset to credit meter is available"/>
<padding size="10"/>
```
= 1+1+5+2+1+10 = 20 payload bytes ⇒ 22-byte frame + CRC. libsas emits exactly 22 bytes
(`lp1B_SendHandpayInformation`, `length = 22`), and those "10 padding bytes" are in fact
`decToBcdArray(handpayItem.sessionGameWinAmount, &txBuffer[12], 5)` and
`decToBcdArray(handpayItem.sessionGamePayAmount, &txBuffer[17], 5)` — two real SAS 6.02 fields.
**A tester using the stock GSA templates is blind to session win/pay amounts on LP 1B.**
(The byte count also independently proves `bool_parm` = 1 byte.)
The other `padding size="3"` closes out the LP A0 response, matching the trace above (`00 00 00`).

#### `optional{name}(...)` — a trailing suffix group, presence inferred from remaining length
This is the model's conditional mechanism, and it is deliberately weak: `optional` has **only a `name`**,
no predicate, no discriminator, no count. 59 in polls, 73 in responses; **49/59 and 71/73 are anonymous**
(`name=""`). Nesting reaches depth **2** in polls and **3** in responses.

The decode rule that makes it work is *positional and greedy*: **an `optional` group is present iff
enough bytes remain in the message to hold it.** Because every template puts its `optional`s strictly at
the end, and nests them so that group *k+1* can only appear inside group *k*, a message is a **prefix-closed
sequence of suffixes**. Concrete example, LP 4F response (`crc="yes" length="yes"`):
```xml
<choice_parm name="Status" size="1">...</choice_parm>
<binary_parm name="Percent full" size="1" radix="decimal"/>
<optional name=""><bcd_parm name="Level" size="4"/></optional>
```
`len = 2` → no level; `len = 6` → level present. Nothing else in the message says so.

The same idiom does duty for **repetition**, by unrolling. LP 86 *Receive multiple progressive levels*
literally repeats
```xml
<optional name=""><binary_parm name="Level" size="1" radix="decimal"/><bcd_parm name="Amount" size="5"/></optional>
```
**19 times**; LP 87 response 10 times; LP B2 response 10 times; LP 75 poll 10 times; LP 2F 12 times.
That is why LP 86's poll template has 22 direct children and 60 total nodes for what is conceptually
`level_amount[1..20]`. **The model has no array/repeat construct at all.**

And it is *not* sufficient. Two failure modes are visible in the data:
1. **Fixed unroll bound.** LP 2F poll offers meters 1..13 (1 mandatory + 12 optional); LP 2F response
   offers 1..10. libsas caps at 10 (`if (numberOfMeters > 10) ... MDPEC_IMPROPERLY_FORMATTED_LP`).
   The bound is a template-authoring choice, not a protocol fact.
2. **Optionals that are really discriminated.** LP 72 response nests
   `optional{ Receipt status, optional{ Transfer type, ..., optional{cum cashable}, optional{cum restricted}, optional{cum nonrestricted} } }`.
   The three cumulative-amount optionals are each a `variable_bcd_parm size=9 length=yes`, so they are
   self-delimiting — but whether they are present is a function of the transfer type and the SAS version,
   not merely of bytes-remaining.

#### `option_choice_parm{name,size}` + `option_choice_entry{name,value}(child?)` — a tagged union
The one genuinely *structural* construct in the model: a discriminant of `size` bytes (1..4) followed by
**the payload belonging to the matched entry** (the DTD allows `?`, i.e. 0 or 1 child). Only 3 uses,
all in the LP 6E authentication path. The poll side:
```xml
<optional name="Address">
  <option_choice_parm name="Addressing mode" size="1">
    <option_choice_entry name="By index number" value="00">
      <variable_hex_parm name="Component index" size="127" length="yes"/></option_choice_entry>
    <option_choice_entry name="By name" value="01">
      <variable_ascii_parm name="Component name" size="127" length="yes"/></option_choice_entry>
    <option_choice_entry name="Unsupported value" value="F1"/>   <!-- no payload -->
  </option_choice_parm>
</optional>
<optional name="Authentication data">
  <option_choice_parm name="Method" size="4">
    <option_choice_entry name="CRC 16" value="0001"><binary_parm name="CRC16 seed" size="2" length="yes"/></option_choice_entry>
    <option_choice_entry name="CRC 32" value="0002"><binary_parm name="CRC32 seed" size="4" length="yes"/></option_choice_entry>
    <option_choice_entry name="MD5"    value="0004"><variable_hex_parm  name="MD5 seed"    size="16" length="yes"/></option_choice_entry>
    <option_choice_entry name="Kobetron I"  value="0008"><variable_ascii_parm name="Kobetron I seed"  size="4" length="yes"/></option_choice_entry>
    <option_choice_entry name="Kobetron II" value="0010"><variable_ascii_parm name="Kobetron II seed" size="4" length="yes"/></option_choice_entry>
    <option_choice_entry name="SHA1"  value="0020"><variable_hex_parm name="SHA1 seed" size="20" length="yes"/></option_choice_entry>
    <option_choice_entry name="Unsupported value" value="F1"/>
  </option_choice_parm>
  <variable_hex_parm name="Offset" size="16" length="yes"/>
</optional>
```
Note the discriminant values `0001 0002 0004 0008 0010 0020` are **bit flags**, and the *same*
4-byte word appears elsewhere as a `bitmap_parm size="4"` with `bitmap_field offset="0".."5"`
(LP 6E response, *Available authentication methods*). The model therefore describes one protocol field
two different ways in two different places, with no link between them.

### 1.4 The four bespoke types, and why they exist

Each of these is a construct the generic type system **cannot express**. They are the model's confession
of its own limits, and each one maps to a specific missing combinator.

#### `variable_meter_parm{name}` — poll side of LP 6F / LP AF (2 uses)
```xml
<SAS_template name="Send extended meters for game N" cmd="6F" crc="yes" multigame="yes" length="yes">
  <bcd_parm name="Game number" size="2"/>
  <variable_meter_parm name="Meters"/>
</SAS_template>
```
Wire format, from `lp6F_SendExtendedMetersForGameN` (`lp.cpp:4091`):
`numberOfMeters = (commandLength - 2) / 2;` then
`requestedMeter = rxBuffer[5+i*2] | (rxBuffer[5+i*2+1] << 8);`
⇒ **an unbounded array of 2-byte little-endian meter codes, count = (message length - 2) / 2.**
Missing combinator: **`repeat until end-of-message`** with a computed count.

#### `variable_meter_response{name}` — response side of LP 6F / LP AF (2 uses)
Same function, encode path:
```c
memcpy(&txBuffer[length], &rxBuffer[5 + (i*2)], 2);  length += 2;   // meter code, echoed
txBuffer[length++] = extendedMeterSize;                             // per-meter byte count
decToBcdArray(extendedMeterValue, &txBuffer[length], extendedMeterSize);
```
⇒ **repeat-until-end of `{ code:u16le, size:u8, value:BCD[size] }`**, where `extendedMeterSize` is
computed per meter by `getExtendedMeterSize(requestedMeter, extendedMeterValue)`.
Missing combinators: **repeat-until-end** *and* **element-internal length prefix governing the next field**.

#### `poll_2F_meter_parm{name}` — response side of LP 2F (10 uses: 1 + 9 `optional`)
From `lp2F_SendSelectedMetersForGameN`:
```c
numberOfMeters = commandLength - 2;              // 1-byte meter codes in the poll
requestedMeter = rxBuffer[5 + i];
txBuffer[length++] = requestedMeter;
decToBcdArray(meterValue, &txBuffer[length], getMeterMinimumSize(requestedMeter));
length += getMeterMinimumSize(requestedMeter);
```
⇒ `{ code:u8, value:BCD[f(code)] }` — **the field width is a table lookup keyed by a sibling field's
value**. No length byte on the wire; the decoder must know the meter-code→width table.
Missing capability: **cross-field dependency resolved through an external lookup table**.
This is precisely the capability an assertion-oriented tool needs anyway (meter semantics), and it is
the strongest argument that meter codes deserve to be a *first-class domain* in our model, not an integer.

#### `aft_receipt_parm{name}` — LP 72 poll only (1 use)
From `lp72_TransferFunds` (`lp.cpp:5092-5178`): a **length-prefixed TLV block**
`[total_len:u8][ {code:u8, len:u8, value:len bytes} ... ]`, with per-code typing and per-code length
validation (any unknown code ⇒ `sendHostNack`):

| code | field | type / length rule (libsas) |
|---|---|---|
| `00` | Transfer source/destination | ASCII, ≤ buffer |
| `01` | Date & time | BCD, **len must be 7** |
| `10` | Patron name | ASCII, ≤ buffer |
| `11` | Patron account number | ASCII, ≤ buffer |
| `13` | Account balance | BCD, **len must be 5** |
| `41` | Debit card number | BCD, **len must be 2** |
| `42` | Transaction fee | BCD, **len must be 5** |
| `43` | Total debit amount | BCD, **len must be 5** |

The simulator's own strings confirm the same eight codes and add per-field diagnostics
(`Field%i, Receipt code %2X = %s`, `Date/Time BAD SIZE (%d)`, `Account balance BAD DATA (`, ...).
Missing combinators: **TLV / map-of-optional-tagged-fields**, plus **per-tag length constraints** —
i.e. exactly the assertions we want to express declaratively.

### 1.5 Two structural observations worth carrying forward

* **Polls and responses are separate documents keyed by the same `cmd`.** There is no link element;
  the correlation is by convention. The exe's `Unable to find command %02X for response processing!`
  shows the runtime pairs them by command byte at dispatch time.
* **Exceptions live in a third document with the same schema**, where `cmd` means something else.
  116 exception templates; only 9 of them carry any payload at all (`4F, 7C, 7E, 7F, 88, 8A, 8B, 8C`;
  the 3 `A0/A1/A2` meter-change ones are declared but empty) — the rest are bare code→name mappings.

---

## 2. Complete inventory

### 2.1 Counts

* PollTemplates: **137** templates · `crc=yes` 70 · `length=yes` 15 · `multigame=yes` 19
* ResponseTemplates: **116** templates · `crc=yes` 114 · `length=yes` 21 · `multigame=yes` 0
* EventTemplates: **116** templates · none of `crc`/`length`/`multigame` set on any
* UserPollTemplates / UserResponseTemplates: **0** templates each (empty extension points)
* Union of poll and response command codes: **137**

### 2.2 Command-code table (poll ∪ response = 137 codes)

Legend: `P` = poll template, `R` = response template. `#d` = direct children, `#t` = total descendant
nodes (a proxy for how much the copy-pasted enum lists inflate the tree). The full unabridged 137-row
table is at `…\scratchpad\research\tables.md`; below, the 55 structurally identical single-meter reads
are folded into one row.

| cmd | name | P crc | P len | P mg | P #d/#t | R crc | R len | R #d/#t |
|---|---|---|---|---|---|---|---|---|
| 01 | Shutdown | yes | no | no | 0/0 | — | — | — |
| 02 | Startup | yes | no | no | 0/0 | — | — | — |
| 03 | Sound off | yes | no | no | 0/0 | — | — | — |
| 04 | Sound on | yes | no | no | 0/0 | — | — | — |
| 05 | Reel spin / game-play sounds disabled | yes | no | no | 0/0 | — | — | — |
| 06 | Enable bill acceptor | yes | no | no | 0/0 | — | — | — |
| 07 | Disable bill acceptor | yes | no | no | 0/0 | — | — | — |
| 08 | Configure bill denominations | yes | no | no | 2/52 | — | — | — |
| 09 | Enable/disable game N | yes | no | **yes** | 2/2 | — | — | — |
| 0A | Enter maintenance mode | yes | no | no | 0/0 | — | — | — |
| 0B | Exit maintenance mode | yes | no | no | 0/0 | — | — | — |
| 0E | Enable/disable RTE reporting | yes | no | no | 1/1 | — | — | — |
| 0F | Send meters 10 through 15 | no | no | no | 0/0 | yes | no | 6/6 |
| 10–1A, 1C, 1E, 20, 27, 2A–2C, 31–3C, 3E–47, 49, 4A, 55 | single / small meter reads (55 codes) | no | no | no | 0/0 | yes | no | 1–8 |
| 1B | Send handpay information | no | no | no | 0/0 | yes | no | 6/6 |
| 1D | Send EFT cumulative meters | no | no | no | 0/0 | yes | no | 4/4 |
| 1F | Send gaming machine ID and information | no | no | no | 0/0 | yes | no | 8/36 |
| 21 | ROM signature verification | yes | no | no | 1/1 | yes | no | 1/1 |
| 22 | Transfer non-cashable amount to GM | yes | no | no | 3/3 | yes | no | 4/17 |
| 23 | Transfer promotional amount to GM | yes | no | no | 3/3 | yes | no | 4/17 |
| 24 | Transfer dollar amount to host | yes | no | no | 2/2 | yes | no | 4/17 |
| 25 | Transfer cash amount out of GM | yes | no | no | 2/2 | yes | no | 4/17 |
| 26 | Host request for cashout dollar amount | yes | no | no | 1/1 | yes | no | 4/17 |
| 28 | Send transfer log | no | no | no | 0/0 | yes | **yes** | 5/160 |
| 29 | Transfer cashable amount to GM | yes | no | no | 3/3 | yes | no | 4/17 |
| 2D | Send total hand-paid cancelled credits | yes | no | **yes** | 1/1 | yes | no | 2/2 |
| 2E | Game delay | yes | no | no | 1/1 | — | — | — |
| 2F | Send selected meters for game N | yes | **yes** | **yes** | 14/26 | yes | **yes** | 11/20 |
| 3D | Send cash-out ticket information | no | no | no | 0/0 | **no (defect)** | no | 2/2 |
| 48 | Send last accepted bill information | no | no | no | 0/0 | yes | no | 3/69 |
| 4C | Set secure enhanced validation ID | yes | no | no | 2/2 | yes | no | 2/2 |
| 4D | Send enhanced validation information | yes | no | no | 1/1 | yes | no | 10/23 |
| 4F | Send current hopper status | no | no | no | 0/0 | yes | **yes** | 3/14 |
| 50 | Send validation meters | yes | no | no | 1/14 | yes | no | 3/16 |
| 51 | Send total number of games implemented | no | no | no | 0/0 | yes | no | 1/1 |
| 52 | Send game N meters | yes | no | **yes** | 1/1 | yes | no | 5/5 |
| 53 | Send game N configuration | yes | no | **yes** | 1/1 | yes | no | 9/37 |
| 54 | Send SAS version ID and EGM serial number | no | no | no | 0/0 | yes | **yes** | 2/2 |
| 56 | Send enabled / implemented game numbers | no | no | no | 0/0 | yes | **yes** | 11/21 |
| 57 | Send pending cashout information | no | no | no | 0/0 | yes | no | 2/6 |
| 58 | Receive validation number | yes | no | no | 2/2 | yes | no | 1/5 |
| 62, 63, 69 | Transfer credits to GM (EFT) | yes | no | no | 3/3 | yes | no | 4/17 |
| 64, 65, 66, 67, 6B | EFT transfers to host / force cashout | yes | no | no | 1–2 | yes | no | 3/16 |
| 6A | Send available EFT transfers / meters | no | no | no | 0/0 | **no (suspect)** | no | 2/6 |
| 6E | Send authentication info | yes | **yes** | no | 3/29 | yes | **yes** | 4/48 |
| 6F | Send extended meters for game N | yes | **yes** | **yes** | 2/2 | yes | **yes** | 2/2 |
| 70 | Send ticket validation data | no | no | no | 0/0 | yes | **yes** | 2/9 |
| 71 | Redeem ticket | yes | **yes** | no | 2/24 | yes | **yes** | 2/26 |
| 72 | AFT transfer funds | yes | **yes** | no | 2/47 | yes | **yes** | 3/80 |
| 73 | AFT register gaming machine | yes | **yes** | no | 2/11 | yes | **yes** | 4/9 |
| 74 | AFT lock and status request | yes | no | no | 3/15 | yes | **yes** | 12/50 |
| 75 | Set AFT receipt data | yes | **yes** | no | 11/149 | — | — | — |
| 76 | Set custom AFT ticket data | yes | **yes** | no | 6/48 | yes | **yes** | 5/40 |
| 7B | Extended validation status | yes | **yes** | no | 4/32 | yes | **yes** | 4/18 |
| 7C | Set extended ticket data | yes | **yes** | no | 5/45 | yes | no | 1/1 |
| 7D | Set ticket data | yes | **yes** | no | 1/7 | yes | no | 1/1 |
| 7E | Send current date and time | no | no | no | 0/0 | yes | no | 2/2 |
| 7F | Receive date and time | yes | no | no | 2/2 | — | — | — |
| 80 | Send progressive amount | yes | no | no | 3/3 | — | — | — |
| 83 | Send cumulative progressive wins | yes | no | **yes** | 1/1 | yes | no | 2/2 |
| 84 | Send progressive win amount | no | no | no | 0/0 | yes | no | 3/3 |
| 85 | Send SAS progressive win amount | no | no | no | 0/0 | yes | no | 3/3 |
| 86 | Receive multiple progressive levels | yes | **yes** | no | 22/60 | — | — | — |
| 87 | Send multiple SAS progressive win amounts | no | no | no | 0/0 | yes | **yes** | 12/32 |
| 8A | Initiate legacy bonus | yes | no | no | 2/6 | — | — | — |
| 8B | Initiate multiplied jackpot mode | yes | no | no | 5/22 | — | — | — |
| 8C | Enter/exit tournament mode | yes | no | **yes** | 5/5 | — | — | — |
| 8E | Send card information | no | no | no | 0/0 | yes | no | 6/6 |
| 8F | Send physical reel stop information | no | no | no | 0/0 | yes | no | 9/9 |
| 90 | Send legacy bonus win amount | no | no | no | 0/0 | yes | no | 4/8 |
| 94 | Remote handpay reset | yes | no | no | 0/0 | yes | no | 1/5 |
| 95–98 | Tournament games played/won, credits wagered/won | yes | no | **yes** | 1/1 | yes | no | 2/2 |
| 99 | Send tournament meters / meters 95–98 | yes | no | **yes** | 1/1 | yes | no | 5/5 |
| 9A | Send legacy bonus meters | yes | no | **yes** | 1/1 | yes | no | 4/4 |
| A0 | Send enabled features | yes | no | **yes** | 1/1 | yes | no | 5/45 |
| A4 | Send cash out limit | yes | no | **yes** | 1/1 | yes | no | 2/2 |
| A8 | Enable jackpot handpay reset method | yes | no | no | 1/4 | yes | no | 1/5 |
| AA | Enable/disable game auto rebet | yes | no | no | 1/1 | — | — | — |
| AF | Send extended meters for game N (alt) | yes | **yes** | **yes** | 2/2 | yes | **yes** | 2/2 |
| B1 | Send current player denomination | no | no | no | 0/0 | yes | no | 1/29 |
| B2 | Send enabled player denominations | no | no | no | 0/0 | yes | **yes** | 11/301 |
| B3 | Send token denomination | no | no | no | 0/0 | yes | no | 1/29 |
| B4 | Send wager category information | yes | no | **yes** | 2/2 | yes | **yes** | 4/4 |
| B5 | Send extended game N information | yes | no | **yes** | 1/1 | yes | **yes** | 8/376 |
| B6 | Meter collection status | yes | **yes** | no | 1/4 | yes | **yes** | 1/4 |
| B7 | Set machine numbers | yes | **yes** | no | 2/2 | yes | **yes** | 3/7 |

### 2.3 Asymmetries between the two files

**Poll templates with no response template — 21 codes.** All are genuine SAS write polls that answer
only with an ACK (echo of address + command), so a payload template is meaningless:
`01 02 03 04 05 06 07 08 09 0A 0B 0E 2E 75 7F 80 86 8A 8B 8C AA`.
Notably this includes `75` (Set AFT receipt data) and `86` (Receive multiple progressive levels) — both
of which have large, hand-unrolled *poll* templates.

**Response templates with no poll template — 0.** Every response code has a poll.

**Naming drift between the pair** (same `cmd`, different `name`): `24`, `25`, `29`, `56`, `65`, `6A`, `99`
(e.g. poll `99 Send tournament meters` vs response `99 Send meters 95 through 98`;
poll `56 Send enabled game numbers` vs response `56 Send implemented game numbers`).
Response `74` has a **leading space** in its name (`" AFT lock and status request"`). Poll `3D` is
`Send cash-out ticket information`, response `3D` is `Send cash out ticket information`; poll `28` is
`Send tranfer log` (sic). Small things, but they prove the two files are maintained independently with no
cross-check — a code-generated model would not have them.

### 2.4 Events

116 exception templates in `EventTemplates.xml`; 107 are payload-free code→name entries.
The 9 with payloads: `4F` Bill accepted (country + denomination enums + BCD count), `7C` Legacy bonus
awarded, `7E` Game start (includes a 6-bit denomination field inside a `bitmap_parm`), `7F` Game end,
`88` Reel N stopped, `8A` Game recall entry displayed, `8B` Card held/not held, `8C` Game selected.
`A0`/`A1`/`A2` (meter change pending / cancelled, enabled games+denoms changed) are declared but empty.

---

## 3. Coverage vs libsas

### 3.1 Method

libsas's handled set was taken from the real dispatcher — the `switch (command)` in
`C:\Progetti\libsas\src\libsas_threads.cpp` (lines 3574–4029), resolving each `case LP_*:` through
`enum SasLongPollCode` in `C:\Progetti\libsas\include\libsas.h:1561`. Script: `lpx.py`.

**Result: 120 distinct long-poll codes are dispatched**, not 122. (The prior "122" figure matches the
**exception** enum instead: `enum SasExceptionCode` at `libsas.h:233` has exactly **122** members.)
`lp.h` declares 111 per-code `lpNN_*` functions; the remaining 9 codes
(`01 02 03 04 05 06 07 0A 0B`) share the generic `lpXX_EnableDisable()`.
`LP_SETSOUNDVOLUME (0x0C)` and `LP_PLAYSOUNDNUMBER (0x0D)` are **declared in the enum but never
dispatched** — they appear only in the LP B0 preamble reject list at `lp.cpp:9796`.
Customer-registered long polls are handled outside the switch by `clpXX_sendToUser()`.

### 3.2 Long polls

| bucket | count | codes |
|---|---|---|
| **In both** | **119** | 01 02 03 04 05 06 07 08 09 0A 0B 0E 0F 10 11 12 13 14 15 16 17 18 19 1A 1B 1C 1E 1F 20 21 2A 2B 2C 2D 2E 2F 31 32 33 34 35 36 37 38 39 3A 3B 3C 3D 3E 3F 40 41 42 43 44 45 46 47 48 49 4A 4C 4D 4F 50 51 52 53 54 55 56 57 58 6E 6F 70 71 72 73 74 75 76 7B 7C 7D 7E 7F 80 83 84 85 86 87 8A 8B 8C 8E 8F 90 94 95 96 97 98 99 9A A0 A4 A8 AA AF B1 B2 B3 B4 B5 B6 B7 |
| **GSA only** | **18** | `1D 22 23 24 25 26 27 28 29 62 63 64 65 66 67 69 6A 6B` — **all legacy EFT** (SAS 5.x credit / dollar transfers, transfer log, EFT cumulative meters). libsas implements no EFT at all. |
| **libsas only** | **1** | `B0` — **multi-denom preamble** (`lpB0_MultiDenomPreamble`). GSA models it as a per-poll checkbox and a `(B0)` prefix in the poll name (`$GSA\PollDef.txt`: `2F - (B0) User Defined`), never as a template. |

So the GSA data covers **119/120 = 99.2 %** of what libsas answers, and adds 18 legacy-EFT codes libsas
does not implement. **SasWorkbench's model has essentially no code-level gaps to fill against libsas —
the missing piece is B0 framing, which is a wrapper, not a message.**

### 3.3 Exceptions

| bucket | count | detail |
|---|---|---|
| **In both** | **116** | every GSA event template maps to a libsas `SasExceptionCode` |
| **GSA only** | **0** | — |
| **libsas only** | **6** | `2E CashboxNearFull`, `2F BillAcceptorVersionChanged`, `5F TipAwarded`, `9E SessionStarted`, `9F SessionEnd`, `FF RealTimeEventMessageIdentifier` |

The five real extras (`2E 2F 5F 9E 9F`) are post-v1.1 SAS additions / vendor extensions that the 2003-era
toolkit cannot report; `FF` is the RTE envelope marker, not an exception.
**This is the concrete list of libsas behaviour the official toolkit cannot exercise.**

### 3.4 Payload-level gaps (more important than the code lists)

Code-level coverage is near-total; *field*-level coverage is not:

* **LP 1B** — GSA models the last 10 bytes as `padding`; libsas populates them with
  `sessionGameWinAmount` and `sessionGamePayAmount` (5 BCD each). GSA cannot assert on them.
* **LP 3D response** — GSA says no CRC; libsas emits one. A frame-level mismatch.
* **LP 2F** — GSA poll allows 13 meter slots, GSA response 10; libsas rejects > 10.
* **LP 1B level** — libsas `enum LP_LEVEL` distinguishes `0x00` non-progressive win,
  `0x01..0x20` progressive levels, `0x40` non-progressive top win, `0x80` cancelled credits;
  GSA models the byte as a bare `binary_parm size="1" radix="hex"` with no enum at all.
* **LP 1B reset ID** — libsas `enum LP_RESET_ID` has three values including `0x80` (celebration win);
  GSA models it as a `bool_parm`, so `0x80` decodes as "true" and the distinction is lost.

---

## 4. Design assessment for SasWorkbench

### 4.1 What GSA gets right (adopt the ideas, not the file)

1. **A declarative, data-driven message model at all.** 369 messages described with no per-message code,
   editable by a protocol engineer without a compiler. This is the correct architecture and the single
   biggest reason this package beats `sastest.exe`.
2. **Separation of framing from payload.** `crc` / `length` as message-level booleans, payload described
   independently. Clean, and it matches how SAS is layered.
3. **Presentation separated from encoding.** `radix` affects rendering only (verified: `FF` → `255`).
4. **Named enums with human labels**, rendered `(01) $0.01` — every trace line is self-documenting.
5. **Bit fields with names and multi-bit sub-fields**, LSB-0, `(byte >> offset) & mask`.
   The LP A0 trace decode is genuinely excellent operator output.
6. **The `F1` "Unsupported value" enum member** — a negative-test vector living inside the type.
   The most transferable single idea in the package for a *conformance* tool.
7. **An additive user-overlay file** keyed by command with duplicate rejection: extension without forking.
8. **`multigame` as per-message automation metadata** that drives test generation.

### 4.2 Where it is awkward or insufficient

| # | Problem | Evidence | Consequence for a conformance tool |
|---|---|---|---|
| 1 | **No repetition construct.** | LP 86 poll unrolls the same `optional` block **19x**; LP 87 / B2 / 75 10x; LP 2F 12x; LP B5 response has 376 nodes for one message. | Arrays are un-assertable ("all N levels present", "count field == array length"), bounds are authoring accidents, files are ~3x larger than necessary. |
| 2 | **`optional` has no predicate.** | 132 `optional` elements across the two files, every one with only a `name`; 120 of them anonymous. Presence is inferred solely from bytes-remaining. | Cannot express "present iff transfer code ≠ FF", "present iff feature bit 3 set", "present iff SAS ≥ 6.02". Decoding is guesswork and ambiguity is silent. |
| 3 | **No cross-field dependency.** | `poll_2F_meter_parm` exists *only* because a field's width is `f(meter code)`; `variable_meter_response` only because size is carried inline. | Every new dependency ⇒ a new bespoke element ⇒ a code change. The model does not absorb spec addenda. |
| 4 | **No byte-order attribute on `binary_parm`.** | libsas: asset number / pool ID / meter code LSB-first (`BUILD_DWORDP`, `libsas_internal.h:296`); LP 21 seed MSB-first (`internalReverseCopy`). | Two fields of the same declared type decode differently. Impossible to get right from the data alone. |
| 5 | **No units, scale or currency.** | LP A4 `Cash out limit` is `bcd_parm size=2`; LP 2E `Delay (100ms ticks)` and LP 74 `Lock timeout (10ms ticks)` encode the unit **in the field name**. libsas meter codes distinguish `_Cents` from `_Qty` (`libsas.h:1262+`); GSA has no such notion. | Cannot assert "coin-in increased by exactly one wager", cannot cross-check a cents meter against a quantity meter, cannot render non-USD denominations correctly. |
| 6 | **No assertions or constraints of any kind.** | The only validation anywhere in the product is the *SAS Comms* checkbox `Verify data fields (non-BCD data in BCD field, e.g.)`. | The tool **displays**; it does not **judge**. A conformance harness must judge. |
| 7 | **No message pairing, no state.** | Polls, responses and exceptions are three unlinked documents keyed by the same byte; the runtime pairs them at dispatch (`Unable to find command %02X for response processing!`). | Cannot express "LP 74 response `Game lock status` must be `00` after a successful lock request", or any multi-message invariant. |
| 8 | **`padding` throws data away.** | LP 1B: 10 bytes discarded that libsas fills with session win/pay amounts. | Silent blindness. `padding` should mean "reserved, assert zero", never "ignore". |
| 9 | **No versioning.** | Version lives in prose inside field names: `Lock after transfer request-SAS 6.02`, `Lock timeout (10ms ticks) SAS 6.02`. | Cannot run a 6.01 suite and a 6.02 suite from one model, nor report "EGM used a 6.02-only field while claiming 6.01". |
| 10 | **Model data quality is unchecked.** | `3D` response missing `crc`; 7 poll/response name mismatches; a leading space in one name; `Send tranfer log` typo. | Nothing validates the model against itself; a schema plus a lint pass would have caught all of it. |
| 11 | **Enum duplication.** | The 28-entry denomination enum is copy-pasted **26x** (`choice_entry@value` histogram: `1A` 24x, `0F` 24x); the country enum 3x; the AFT machine-status enum 13x. Hence 1 238 `choice_entry` elements in one file. | Every spec addendum requires 26 coordinated edits. Guaranteed drift. |
| 12 | **`bitmap_parm size>1` byte order unspecified.** | `size="4"` with offsets 0..31 (LP B5, LP 6E); no trace in the package decodes one. **UNKNOWN.** | Multi-byte bit fields are un-implementable from the model alone. |
| 13 | **Exceptions reuse `SAS_template` with `cmd` meaning "exception code".** | `EventTemplates.xml` — 116 templates, zero `crc`/`length`/`multigame`. | Type confusion; a shared loader must know which file it is reading to know what `cmd` means. |
| 14 | **XML + DTD as the carrier.** | Every attribute is `CDATA`: `size CDATA`, `offset CDATA`. | No datatypes, no editor validation, no `$ref` for shared enums, no schema-driven codegen. |

### 4.3 What to add for a tool whose job is *asserting*

The decisive difference: **GSA renders a decoded message for a human; SasWorkbench must produce a
machine-checkable verdict.** That flips three requirements.

* **Decoding must be total and explicit about failure.** "Bytes remaining ⇒ optional present" must become
  a stated predicate, and a decode that cannot be explained must be a *reported failure*, not a
  `BAD PARSE` fragment in a log line.
* **Every field needs an identity that survives renaming.** GSA identifies fields by display name; we need
  a stable `id` for assertions to bind to (`lp74.rsp.gameLockStatus`), with the label as metadata.
* **The model must carry the expectation, not just the shape.** Constraints, pairing, state and units
  belong in the model so that a conformance suite is *derived* rather than hand-written.

### 4.4 Proposed field-type set for SasWorkbench

Authored by us and populated from `libsas.h` / `lp.h` / `lp.cpp` (our own descriptions; no third-party
spec text redistributed). JSON (or YAML compiled to JSON) with a real JSON Schema, so editors validate and
a source generator can emit C# records.

```jsonc
{
  "$schema": "https://sasworkbench/schema/protocol-1.0.json",
  "protocol": "SAS", "version": "6.02", "revision": 3,

  "enums": {                          // fixes 4.2 #11 - defined ONCE, referenced everywhere
    "Denomination": {
      "width": 1,
      "members": [
        { "value": "0x01", "id": "cent1", "label": "$0.01", "minorUnits": 1 },
        { "value": "0x06", "id": "usd1",  "label": "$1.00", "minorUnits": 100 }
      ],
      "testVectors": [ { "value": "0xF1", "id": "unsupported", "kind": "negative" } ]  // GSA's F1 idea, typed
    }
  },
  "meterCodes": {                     // fixes 4.2 #3/#5 - meter semantics as first-class data
    "0x0000": { "id": "totalCoinIn", "unit": "credits", "widthBytes": 4, "multigame": true, "multidenom": true },
    "0x008C": { "id": "validatedCancelledCreditHandpayReceiptPrinted", "unit": "cents", "widthBytes": 5 }
  },

  "fieldTypes": [
    // ---- scalars ------------------------------------------------------------
    { "type": "bcd",   "props": { "bytes": "int 1..9",
                                  "unit": "credits|cents|count|ms|ticks|dateMMDDYYYY|timeHHMMSS",
                                  "scale": "int (power of 10)" } },
    { "type": "uint",  "props": { "bytes": "int 1..8",
                                  "endian": "little|big   (REQUIRED - no default)",
                                  "display": "dec|hex|bin", "unit": "...", "scale": "int" } },
    { "type": "bytes", "props": { "bytes": "int", "display": "hex|base64" } },          // ex hex_parm
    { "type": "ascii", "props": { "bytes": "int", "pad": "space|nul|none", "trim": true } },
    { "type": "bool",  "props": { "bytes": 1, "trueValue": "0x01", "strict": true } },  // strict: any other value is a violation

    // ---- length-governed ----------------------------------------------------
    { "type": "lengthPrefixed", "props": { "countBytes": 1, "maxBytes": "int",
                                           "element": "<scalar type>" } },              // ex variable_* and binary length=yes
    { "type": "remainder",      "props": { "element": "<scalar type>" } },               // ex variable_ascii length=no

    // ---- structure ----------------------------------------------------------
    { "type": "bitfield", "props": { "bytes": "int 1..4",
                                     "byteOrder": "little|big (REQUIRED when bytes>1)",
                                     "bitNumbering": "lsb0",
                                     "fields": [ { "offset": "int", "width": "int (default 1)",
                                                   "id": "...", "as": "bool|enumRef",
                                                   "reservedMustBe": "0" } ] } },
    { "type": "enum",  "props": { "enumRef": "...", "onUnknown": "violation|warn" } },
    { "type": "union", "props": { "tag": { "bytes": "int 1..4", "endian": "...", "enumRef": "..." },
                                  "tagIsBitmask": false,
                                  "arms": { "<enumMemberId>": "<field list | null>" },
                                  "onUnmatchedTag": "violation" } },                     // ex option_choice_parm

    // ---- the four bespoke GSA types, generalised ----------------------------
    { "type": "array", "props": { "element": "<field list>",
                                  "count": { "from": "fieldId | remainingBytes/elementSize | literal" },
                                  "min": "int", "max": "int" } },                        // kills the 19x unroll
    { "type": "tlv",   "props": { "totalLength": { "countBytes": 1 },
                                  "tag": { "bytes": 1 }, "length": { "bytes": 1 },
                                  "entries": { "0x01": { "id": "dateTime", "type": "bcd", "bytes": 7 } },
                                  "onUnknownTag": "violation", "duplicatePolicy": "reject" } }, // ex aft_receipt_parm
    { "type": "table", "props": { "keyField": "...", "table": "meterCodes",
                                  "widthFrom": "widthBytes", "valueType": "bcd" } },     // ex poll_2F_meter_parm
    { "type": "reserved", "props": { "bytes": "int", "mustBe": "0x00|any",
                                     "captureAs": "..." } },                              // ex padding - NEVER discards

    // ---- conditionality -----------------------------------------------------
    { "type": "group", "props": { "id": "...", "fields": [ ],
                                  "presentWhen": "<expression>",      // e.g. "transferCode != 0xFF"
                                  "presentWhenBytesRemain": "int",    // GSA's implicit rule, made explicit
                                  "sinceVersion": "6.02",
                                  "requiresFeature": "aft" } } ]
}
```

Message-level shape:

```jsonc
{ "id": "lp74.aftLockAndStatus",
  "command": "0x74", "direction": "hostToEgm",
  "framing": { "address": true, "lengthByte": false, "crc": "crc16" },
  "sinceVersion": "6.01",
  "expansion": { "kind": "perGame", "field": "gameNumber" },   // GSA's `multigame`, typed
  "wrappers": [ "multiDenomPreamble" ],                         // LP B0 as a declared wrapper, not a checkbox
  "fields": [ ],
  "pairsWith": "lp74.rsp",                                      // fixes 4.2 #7
  "assert": [
    { "when": "req.lockCode == 'requestLock'",
      "then": [ { "field": "rsp.gameLockStatus", "in": ["gameLocked", "gameLockPending"] },
                { "field": "rsp.assetNumber", "equals": "session.assetNumber" } ] },
    { "always": [ { "field": "rsp.maxHistoryBufferSize", "range": [1, 127] } ] } ],
  "timing": { "maxResponseMs": 20 } }
```

**Justification of each departure from GSA**

| Departure | Why |
|---|---|
| `array` with a `count` expression | Replaces the 19x / 12x / 10x unrolls (#1) and makes "count field == element count" assertable. Removes roughly 40 % of the node count on its own. |
| `group.presentWhen` expression | Turns GSA's implicit bytes-remaining rule into a stated predicate (#2), so a wrong-length response becomes a reported failure instead of a silent re-interpretation. |
| **Mandatory** `endian` on `uint` and multi-byte `bitfield` | libsas proves SAS uses both orders (#4, #12). A default is a bug factory; making it required forces the author to look it up once. |
| `unit` + `scale` + `minorUnits` | Enables value assertions and correct currency rendering (#5); mirrors libsas's own `_Cents`/`_Qty` meter naming. |
| `meterCodes` as a shared table | `poll_2F_meter_parm` and `variable_meter_response` stop being bespoke types and become `table` / `array` instances (#3), and every meter gets a name in reports. |
| `tlv` | Generalises `aft_receipt_parm`; per-tag length rules become declarative — exactly the checks libsas hard-codes as `if (fieldLength != 5) sendHostNack(...)`. |
| `reserved` replacing `padding` | Never discards; can assert zero and can be promoted to a named field when an addendum defines it. Would have caught the LP 1B session-amount gap (#8). |
| `union` with `onUnmatchedTag` | Same shape as `option_choice_parm` but with an explicit failure mode. |
| Stable `id` alongside `label` | Assertions bind to `id`; renaming a label never breaks a test. GSA's 7 poll/response name mismatches show what happens without this. |
| Top-level `enums` / `meterCodes` with `$ref` | Removes the 26x denomination duplication (#11). |
| `sinceVersion` / `requiresFeature` | One model serves 6.01 and 6.02 suites, and the harness can assert "EGM used a 6.02 field while reporting 6.01" (#9). |
| `pairsWith` + `assert` + `timing` | The whole point: the model yields verdicts, not renderings (#6, #7). |
| `wrappers: ["multiDenomPreamble"]` | LP B0 becomes a declared transformation applicable to any message — matching `lpB0_MultiDenomPreamble`'s unwrap-and-redispatch — instead of a per-poll checkbox. |
| JSON + JSON Schema instead of XML + DTD | Real datatypes, `$ref`, editor validation, source generation into C# records (#14). |
| `testVectors` on enums | Formalises GSA's `F1` idea: negative vectors are typed, excluded from decode, and drive generated negative test cases. |
| Separate `exceptions` document with its own type | Ends the `cmd`-means-two-things overload (#13). |

---

## 5. Runtime import of the GSA XML as an optional overlay

**Feasible? Yes, and cheaply.** The files are small (172 KB total), plain ISO-8859-1 XML with a fixed
17-element vocabulary, no namespaces, no entities; `System.Xml.Linq` parses them in milliseconds once the
`<!DOCTYPE>` line is stripped or resolved. Translation into the type set of §4.4 is mechanical:

| GSA element | SasWorkbench target |
|---|---|
| `bcd_parm` | `bcd` |
| `binary_parm` | `uint` (**endian must be supplied by us**) |
| `hex_parm` | `bytes` |
| `ascii_parm` | `ascii` |
| `bool_parm` | `bool` |
| `variable_*_parm length=yes`, `binary_parm length=yes` | `lengthPrefixed{element}` |
| `variable_ascii_parm length=no` | `remainder{ascii}` |
| `choice_parm` + `choice_entry` | inline `enum` (`F1` entries → `testVectors`) |
| `bitmap_parm` + `bitmap_field` | `bitfield` (**byteOrder must be supplied when size > 1**) |
| `padding` | `reserved{mustBe:any}` |
| `optional` | `group{presentWhenBytesRemain}` |
| `option_choice_parm` | `union` |
| `variable_meter_parm` | `array{count: remainingBytes/2, element: uint16}` |
| `variable_meter_response` | `array{count: untilEnd, element: {uint16, uint8 len, bcd[len]}}` |
| `poll_2F_meter_parm` | `table{keyField: code, table: meterCodes}` |
| `aft_receipt_parm` | `tlv` (8 codes and their length rules, from `lp.cpp:5092-5178`) |

**Two things the import cannot supply**, and they are hard blockers for using an imported model
unattended: (a) byte order for `uint` ≥ 2 bytes, (b) byte order for `bitfield` ≥ 2 bytes. Both must come
from our own base model. The practical shape is therefore **overlay, never replacement**: import can add
messages and enrich labels; our model stays authoritative for framing and encoding.

**What it buys**

1. **Cross-validation of our own model.** Diff imported vs native across 137 poll / 116 response /
   116 event descriptions. This report already found four disagreements that way (LP 3D CRC,
   LP 1B padding, LP 2F meter-slot bounds, LP 1B level and reset-ID enums).
2. **Customer-supplied extensions with no rebuild.** A lab that added templates to
   `UserPollTemplates.xml` for its own custom long polls gets them in SasWorkbench for free — the same
   additive-overlay contract the simulator already implements, and the same contract libsas exposes
   through `clpXX_sendToUser()`.
3. **Legacy EFT for free.** The 18 EFT codes GSA has and libsas lacks (`1D 22-29 62-6B`) are exactly the
   set needed to poll a *third-party* EGM. We would never author them ourselves.
4. **A ready-made golden-file corpus.** `$GSA\step*.trc` pairs raw byte strings with decoded text; feeding
   those bytes through our decoder and diffing against the GSA text is an immediate regression suite.
5. **Familiar vocabulary for the lab.** Testers who know the GSA tool see their own field names.

**What it costs / risks**

* **Licensing.** These are GSA-copyrighted data files (`gsa-p0074`). Importing at runtime from the
  *user's own* installation is a materially different posture from redistributing them — we must ship
  **zero** GSA content and read only from a user-configured path. Consistent with the decision to author
  our own model.
* **Imported definitions must be marked untrusted.** Flag them in the UI and exclude them from
  conformance verdicts until a human reviews them, precisely because of the endian gap and the
  demonstrable data defects.
* **Drift risk if imports can override.** If imports shadow native definitions we inherit GSA's bugs
  (LP 3D would lose its CRC). Import should **add** by default and require an explicit `--override` to
  shadow a native message — mirroring the simulator's own `Duplicate template` rejection.

**Recommendation:** ship it as `sasctl model import --gsa <dir>` producing our own JSON, plus
`sasctl model diff` reporting disagreements against the native model. Do **not** wire it into the live
decode path as a silent fallback.

---

## Appendix — files read / commands run

* Read in full: `SASTemplates.dtd`; `PollTemplates.xml` (1 038 lines); `ResponseTemplates.xml`
  (2 416 lines, via the structural dump `resp_dump2.txt`); `EventTemplates.xml` (256 lines);
  `UserPollTemplates.xml`; `UserResponseTemplates.xml`; `step2.trc`; `step5.trc`; `step3.trc` (head);
  `configFiles.txt`; `configDesc.txt`; `PollDef.txt` (sampled); `ReleaseNotes.txt` (grepped);
  `MeterSetDefinitions.dat` (binary, inspected).
* `diff` of live vs `Defaults\` for `SASTemplates.dtd`, `PollTemplates.xml`, `ResponseTemplates.xml`.
* `strings SASHostSim.exe` (1 653 strings) — parser vocabulary, size limits, error messages, dialog text.
* `C:\Progetti\libsas\include\lp.h`; `libsas.h` (`enum SasLongPollCode`, `enum SasExceptionCode`,
  `enum LP_LEVEL`, `enum LP_RESET_ID`); `libsas_internal.h` (`BUILD_DWORDP`);
  `src\lp.cpp` (`lp1B`, `lp21`, `lp2E`, `lp2F`, `lp3D`, `lp6F`, `lp72` receipt TLV, `lp74`, `lpB0`);
  `src\libsas_threads.cpp` (dispatcher switch, lines 3574-4029);
  `src\libsas_internal.cpp` (`internalReverseCopy`).
* Not read (out of scope by instruction): `C:\Progetti\libsas\utils\sashost`.
