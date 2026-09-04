# SasWorkbench — UI proposals (M1.5)

Four complete layout-plus-colour directions for the Avalonia window, a comparison, and a
recommendation. This is the **UI** half of the M1.5 design contract; it assumes the UX half
settles the information architecture and answers only: *what does it look like, how is it
arranged, and what are the exact values.*

Everything here is expressible in Avalonia 11 XAML — `Grid`, `DockPanel`, `Expander`,
`SplitView`, `ListBox`, `Border`, `Rectangle`, styles, control themes, and a
`ResourceDictionary` with `ThemeDictionaries`. No proposal needs a custom renderer, and the two
places where Avalonia genuinely cannot do the obvious thing are named where they occur.

---

## 0 · What all four share

### 0.1 The defect being fixed

`App.axaml` is four style rules and no colour tokens. Hierarchy in `MainWindow.axaml` is
expressed almost entirely as `Opacity`: `0.4`, `0.45`, `0.5`, `0.55`, `0.65`, `0.7`, `0.75`.
Seven opacity levels is not a hierarchy, it is a fog — and it is applied to the substance
(timestamps, channel, notes, decoded values, meter sources, step details, and the provenance
line) rather than to the chrome.

**Rule adopted by all four proposals: opacity never expresses hierarchy.** Hierarchy comes from
four text tokens (`Text.Primary`, `Text.Secondary`, `Text.Data`, `Text.Disabled`), plus weight,
size, case and position. `Opacity` is permitted only on a control that is genuinely disabled,
and even there `Text.Disabled` is preferred.

### 0.2 The token vocabulary (identical names in all four)

Because the names are shared, switching direction later is a change of one
`Tokens.axaml` file, not a change of every view. That is deliberate: it is the same argument
D16 used for carrying channels in the poll engine from day one.

| Token | What it is for |
|---|---|
| `Surface.Window` | the frame the panels sit on |
| `Surface.Panel` | a panel's own ground — the default reading surface |
| `Surface.Raised` | headers, toolbars, selected rows, chip grounds' base |
| `Surface.Sunken` | the wire log and any other recessed data well |
| `Stroke.Border` | decorative panel outline — **never** the only cue for a control |
| `Stroke.Divider` | row rules and column separators |
| `Stroke.Control` | the boundary of an actual control (>= 3:1, WCAG 1.4.11) |
| `Stroke.Focus` | the keyboard focus ring |
| `Text.Primary` `Text.Secondary` `Text.Disabled` `Text.Data` | the whole prose/data hierarchy |
| `Accent` `Accent.Ground` `Text.OnAccent` | the primary action |
| `Wire.Tx` `Wire.TxGround` `Wire.Rx` | direction on the wire |
| `Wire.Wakeup` `Wire.WakeupGround` | the bit-8 flag on an address byte |
| `Status.Pass` `Status.Fail` `Status.Skip` | verdicts |
| `Status.Undecoded` | known but undecoded — a real third state |
| `Status.Unknown` | we do not know |
| `Status.Warning` | something happened that a reader must weigh |
| `Prov.Proven` `Prov.Unverified` (+ grounds) | what the link is worth |
| `Text.OnFail` | the label on the one solid-filled chip |

Chip grounds are the status colour composited over `Surface.Panel` at **14 % alpha in sRGB
space** — which is what Avalonia/Skia actually does, so the hexes below are the hexes that will
render. Every ground is quoted as a literal so nothing depends on a runtime alpha.

### 0.3 The accessibility contract (binding on all four)

Colour is never the only channel. Every status carries **three** redundant cues:

| State | Word | Fill treatment | Vector glyph |
|---|---|---|---|
| PASS | `PASS` | outlined, tinted ground | check |
| FAIL | `FAIL` | **solid fill**, inverted label | cross |
| SKIP | `SKIP` | outlined + 3 DIP leading bar | slashed circle |
| UNDECODED | `UNDECODED` | **dashed** outline, no ground | question |
| UNKNOWN | `UNKNOWN` | no outline, flat neutral ground | en dash |
| WARNING | `WARNING` | outlined, tinted ground | exclamation |

FAIL is the only solid-filled chip in the entire application. That single figure/ground
inversion is what makes a failure findable in a greyscale printout of a screenshot, and it is
the mitigation for the colour-vision numbers reported per proposal below — which are bad, in
every proposal, and which no palette can fix.

Glyphs are drawn as `Path` geometry, **not** font characters, so a minimal Linux font set
cannot silently turn them into tofu.

Additional binding rules:

* Minimum interactive target 32x32 DIP (24 DIP for a dense in-row control that also has a
  keyboard route). Sub-24 DIP targets are not permitted.
* Focus ring is `Stroke.Focus`, 2 DIP, 1 DIP offset, and is drawn on top of any row tint.
* Every panel has an `AutomationProperties.Name`; every status chip's automation name is the
  word plus the reason, e.g. `SKIP — egm.validation: this environment provides no 'egm.validation'`.
* The provenance banner is a live region: when the link goes from proven to unverified, the
  announcement fires.

### 0.4 The two sizes, and the real one

`MainWindow` is `1180x760` DIP default, `900x600` DIP minimum. Avalonia sizes in DIPs, so 125 %
scaling does not shrink the layout — it shrinks the *screen*:

* 1920x1080 physical at 125 % = 1536x864 DIP usable, minus a ~40 DIP taskbar = **1536x824**.
  The 760 DIP default window fits with 64 DIP to spare. Vertical space is the scarce resource.
* 1366x768 physical at 125 % = 1092x614 DIP. The default window **does not fit** and is
  clamped to roughly 1092x614 — barely above the stated minimum.

So the honest conclusion is: **the 900x600 layout is the common case on a scaled laptop, not
the edge case.** Every proposal below is designed at ~944x610 first and allowed to expand,
not designed at 1180x760 and allowed to degrade.

Budget at 900x600: chrome takes 56 (connection) + 30 (provenance) + 26 (footer) + 16 (margins)
= 128 DIP, leaving **472 DIP** of content. At an 18 DIP data row that is 26 wire lines visible.
At 1180x760 the same chrome leaves 632 DIP, or 35 lines.

### 0.5 The specimen content

Every wireframe below uses the same real captured output from `docs/M1-thin-vertical-slice.md`
and the same real catalogue entries from `src/SasWorkbench.Core/Protocol/`. Nothing is invented.

```
provenance   over tcp 127.0.0.1:9760 (listen), proven, stimulus none
verdict      PASS  1 passed in 7.0s
test         the link comes up and the machine identifies itself (6.9s, 5 assert(s))
skip         SKIP  10.1.1 standard validation configuration
                   egm.validation: this environment provides no 'egm.validation'
counters     34 sync · 34 general · 2 long · 1 exceptions · 0 silences
```

The decoded `$1F` reply below is the captured 24 bytes read field by field against the real
layout (`SasLayouts.cs`, `Command = 0x1F`) — `51 58` is `QX`, `39 32 31 30` is `9210`. The
`$54` reply's length byte says three, and the three are the version, so the serial number is
genuinely absent — the M1 finding, rendered as UNKNOWN rather than as an empty cell.

---
## 1 · Proposal A — **BENCH**

### 1.1 Character

**A piece of measuring equipment.** A fixed bezel with an instrument header, a recessed trace
well, and readouts in drawers rather than tabs — because an instrument does not hide a readout
behind a tab you have to remember to click. Chrome is achromatic; **all chroma is reserved for
data and state**, so the accent is a graphite/bone neutral and the only coloured things on
screen are the wire, the verdicts and the provenance.

* **Flatters most:** the user — a senior protocol engineer who reads hex. The trace well is the
  largest, brightest, highest-contrast object on screen and it is never covered.
* **Serves worst:** customers and integrators. It looks like test equipment, which is honest but
  unwelcoming, and a screenshot of it in a report reads as "internal tooling" rather than as
  evidence a regulator would file.

### 1.2 Layout

Four horizontal bands, all fixed-height, plus a two-column body.

* **Band 0 — instrument header, 56 DIP.** Product mark, then the link identity as a row of
  bezel-style field groups (`LINK`, endpoint, `ADDR`, `CH`, `POLL`), then the primary action.
  Never wraps; the endpoint field is the only elastic one.
* **Band 1 — provenance rail, 30 DIP.** Not a line of grey text. A full-width rail with a
  provenance seal at the left (`PROVEN` or `UNVERIFIED`), the link description, the stimulus
  backend, the caveat, and the link lamp at the right. Its ground is `Prov.ProvenGround` or
  `Prov.UnverifiedGround`, so the whole rail changes colour with the strength of the claim.
* **Band 2 — body.** Left `TRACE` well at 46 % (min 420 DIP), right a **drawer stack**. Drawers
  are `Expander`s: DECODE, RUN, METERS, EXCEPTIONS. A collapsed drawer still shows a one-line
  summary in its header, so nothing is invisible. Above 1000 DIP more than one may be open;
  below that the stack becomes single-expand.
* **Band 3 — footer readout, 26 DIP.** Counters as a segmented mono readout, plus a compact
  repeat of the provenance triple so that a cropped screenshot still carries it.

Chrome is bands 0, 1, 3 and the drawer headers. Content is the trace well and the drawer bodies.
The split is a `GridSplitter`, remembered per session.

**1180x760**

```
+--------------------------------------------------------------------------------------------------+
|  SASWORKBENCH   |LINK tcp  v| |127.0.0.1:9760      | |ADDR 01| |CH 1| |POLL 200ms|  [ CONNECT   ] |
+====================================================================================================+
| [ PROVEN ] over tcp 127.0.0.1:9760 (listen)  ·  stimulus none  ·  no caveat        LINK   (*) UP   |
+---------------------------------------------+------------------------------------------------------+
| TRACE                        [x] Follow  Clr |  v  DECODE            lp $1F machine_id   30.7 ms   |
|- - - - - - - - - - - - - - - - - - - - - - -|     game_id            QX                            |
|   5927ms  TX  1.80  sync poll - address zero |     additional_id      001                           |
|   5954ms  TX  1.81  general poll             |     denomination       2                    code     |
|   5985ms  RX  0.18                           |     max_bet            255                 credits   |
|   6126ms  TX  1.80  sync poll - address zero |     progressive_group  0                     index   |
|   6157ms  TX  1.01 0.1F   long poll $1F      |     game_options       0000           little-endian  |
|   6189ms  RX  0.01 0.1F 0.51 0.58 0.30 0.30  |     paytable_id        001                           |
|                 0.31 0.02 0.FF 0.00 0.00     |     base_percentage    9210        hundredths of %   |
|                 0.00 0.30 0.30 0.31 0.00     |  - - - - - - - - - - - - - - - - - - - - - - - - -   |
|                 0.00 0.00 0.39 0.32 0.31     |  v  RUN   tests/regression        [PASS] 1 in 7.0s   |
|                 0.30 0.86 0.3F               |    [ PASS ] the link comes up and the machine        |
|   6730ms  TX  1.80  sync poll - address zero |             identifies itself     6.9s, 5 assert(s)  |
|   6761ms  TX  1.01 0.54   long poll $54      |    [ SKIP ] 10.1.1 standard validation configuration |
|   6791ms  RX  0.01 0.54 0.03 0.36 0.30 0.32  |             egm.validation: this environment         |
|                 0.D0 0.8D                    |             provides no 'egm.validation'             |
|                                              |  - - - - - - - - - - - - - - - - - - - - - - - - -   |
|                                              |  >  METERS       17 read  ·  1 excluded, not a meter |
|                                              |  >  EXCEPTIONS    2 queued  ·  last $4A at 6.2 s     |
+---------------------------------------------+------------------------------------------------------+
| 34 sync   34 general   2 long   1 exc   0 silences                    tcp · proven · stimulus none |
+--------------------------------------------------------------------------------------------------+
```

**900x600** — drawers become single-expand, the trace well holds its 46 %, the header field
groups drop their captions and keep their values (the caption moves to a tooltip and to
`AutomationProperties.Name`), the footer reduces the counter labels to initials.

```
+------------------------------------------------------------------------+
| SASWB  |tcp v| |127.0.0.1:9760 | |01| |1| |200ms|      [ CONNECT     ]  |
+========================================================================+
| [ PROVEN ] tcp 127.0.0.1:9760 · stimulus none          LINK   (*) UP   |
+---------------------------------+--------------------------------------+
| TRACE               [x] Follow  |  v  RUN            [PASS] 1 in 7.0s  |
|- - - - - - - - - - - - - - - - -|   [ PASS ] the link comes up and the |
|  5927ms TX 1.80  sync poll -    |            machine identifies itself |
|                  address zero   |            6.9s, 5 assert(s)         |
|  5954ms TX 1.81  general poll   |   [ SKIP ] 10.1.1 standard           |
|  5985ms RX 0.18                 |            validation configuration  |
|  6126ms TX 1.80  sync poll -    |            egm.validation: this      |
|                  address zero   |            environment provides no   |
|  6157ms TX 1.01 0.1F  lp $1F    |            'egm.validation'          |
|  6189ms RX 0.01 0.1F 0.51 0.58  |  - - - - - - - - - - - - - - - - - - |
|            0.30 0.30 0.31 0.02  |  >  DECODE   lp $1F        30.7 ms   |
|            0.FF 0.00 0.00 0.00  |  >  METERS   17 read                 |
|            0.30 0.30 0.31 0.00  |  >  EXCEPTIONS  2 queued             |
+---------------------------------+--------------------------------------+
| 34s 34g 2l 1e 0sil                        tcp · proven · none          |
+------------------------------------------------------------------------+
```

**At 125 %** nothing reflows — DIPs are unchanged — but on a 1366x768 panel the window is
clamped near the minimum, so the 900x600 arrangement above is what the user actually sees. Band
heights (56/30/26) are chosen so the three of them plus margins never exceed 22 % of a 600 DIP
window.

### 1.3 Tokens

| Token | Light | Dark |
|---|---|---|
| `Surface.Window` | `#EDF0F2` | `#0D1013` |
| `Surface.Panel` | `#FFFFFF` | `#14191E` |
| `Surface.Raised` | `#F7F9FA` | `#1C232A` |
| `Surface.Sunken` | `#E4E9EC` | `#070909` |
| `Stroke.Border` | `#B4C0C8` | `#2C363F` |
| `Stroke.Divider` | `#DCE3E7` | `#1E262C` |
| `Stroke.Control` | `#7D8B97` | `#657079` |
| `Stroke.Focus` | `#10171C` | `#FFFFFF` |
| `Text.Primary` | `#10171C` | `#E6ECF1` |
| `Text.Secondary` | `#46545E` | `#AAB8C2` |
| `Text.Disabled` | `#6E7C85` | `#79878F` |
| `Text.Data` | `#14202A` | `#DCE7EF` |
| `Accent` | `#22303A` | `#C7D2DA` |
| `Accent.Ground` | `#E0E2E3` | `#2D3338` |
| `Text.OnAccent` | `#FFFFFF` | `#0D1013` |
| `Wire.Tx` | `#8A5300` | `#F0A93C` |
| `Wire.TxGround` | `#EFE7DB` | `#332D22` |
| `Wire.Rx` | `#0A5F70` | `#55CADC` |
| `Wire.Wakeup` | `#6B4A00` | `#FFD466` |
| `Wire.WakeupGround` | `#EAE6DB` | `#353328` |
| `Status.Pass` | `#16704A` | `#5FC98A` |
| `Status.PassGround` | `#DEEBE6` | `#1E322D` |
| `Status.Fail` | `#941515` | `#FF7A7A` |
| `Status.FailGround` | `#F0DEDE` | `#35272B` |
| `Text.OnFail` | `#FFFFFF` | `#000000` |
| `Status.Skip` | `#5A4595` | `#B9A6E8` |
| `Status.SkipGround` | `#E8E5F0` | `#2B2D3A` |
| `Status.Undecoded` | `#41525E` | `#93A6B4` |
| `Status.UndecodedGround` | `#E4E7E8` | `#262D33` |
| `Status.Unknown` | `#696969` | `#9A9A9A` |
| `Status.UnknownGround` | `#EAEAEA` | `#272B2F` |
| `Status.Warning` | `#8A5A00` | `#F2C14E` |
| `Status.WarningGround` | `#EFE8DB` | `#333125` |
| `Prov.Proven` | `#0E6A50` | `#6FD0A8` |
| `Prov.ProvenGround` | `#DDEAE6` | `#213331` |
| `Prov.Unverified` | `#8A4B00` | `#F2A93B` |
| `Prov.UnverifiedGround` | `#EFE6DB` | `#332D22` |

`Status.Unknown` is achromatic in every proposal, by rule: we have no information, so it gets no
hue.

### 1.4 Contrast audit

Computed with the WCAG 2.x relative-luminance formula; grounds are the literal hexes above.

| Pair | Light | Dark | Target |
|---|---|---|---|
| Text.Primary on Surface.Window | 15.80:1 | 16.02:1 | 4.5 |
| Text.Primary on Surface.Panel | 18.08:1 | 14.85:1 | 4.5 |
| Text.Primary on Surface.Raised | 17.12:1 | 13.33:1 | 4.5 |
| Text.Primary on Surface.Sunken | 14.78:1 | 16.76:1 | 4.5 |
| Text.Secondary on Surface.Panel | 7.81:1 | 8.71:1 | 4.5 |
| Text.Secondary on Surface.Raised | 7.39:1 | 7.82:1 | 4.5 |
| Text.Disabled on Surface.Panel | 4.30:1 | 4.78:1 | 3.0 |
| Text.Data on Surface.Panel | 16.54:1 | 14.08:1 | 4.5 |
| Text.Data on Surface.Sunken | 13.52:1 | 15.89:1 | 4.5 |
| Wire.Tx on Surface.Sunken | 5.17:1 | 9.93:1 | 4.5 |
| Wire.Tx on Wire.TxGround | 5.16:1 | 6.79:1 | 4.5 |
| Wire.Rx on Surface.Sunken | 5.95:1 | 10.33:1 | 4.5 |
| Wire.Wakeup on Surface.Sunken | 6.59:1 | 14.13:1 | 4.5 |
| Wire.Wakeup on Wire.WakeupGround | 6.47:1 | 8.98:1 | 4.5 |
| Status.Pass on Surface.Panel | 6.09:1 | 8.60:1 | 4.5 |
| Status.Pass on Status.PassGround | 4.96:1 | 6.59:1 | 4.5 |
| Status.Fail on Surface.Panel | 8.83:1 | 7.00:1 | 4.5 |
| Status.Fail on Status.FailGround | 6.82:1 | 5.63:1 | 4.5 |
| Text.OnFail on Status.Fail (solid chip) | 8.83:1 | 8.32:1 | 4.5 |
| Status.Skip on Surface.Panel | 7.72:1 | 8.15:1 | 4.5 |
| Status.Skip on Status.SkipGround | 6.21:1 | 6.29:1 | 4.5 |
| Status.Undecoded on Surface.Panel | 8.10:1 | 7.03:1 | 4.5 |
| Status.Undecoded on Status.UndecodedGround | 6.51:1 | 5.55:1 | 4.5 |
| Status.Unknown on Surface.Panel | 5.49:1 | 6.28:1 | 4.5 |
| Status.Unknown on Status.UnknownGround | 4.56:1 | 5.07:1 | 4.5 |
| Status.Warning on Surface.Panel | 5.93:1 | 10.54:1 | 4.5 |
| Status.Warning on Status.WarningGround | 4.86:1 | 7.79:1 | 4.5 |
| Prov.Proven on Prov.ProvenGround | 5.31:1 | 7.12:1 | 4.5 |
| Prov.Unverified on Prov.UnverifiedGround | 5.51:1 | 6.83:1 | 4.5 |
| Stroke.Control on Surface.Panel | 3.49:1 | 3.49:1 | 3.0 |
| Stroke.Control on Surface.Raised | 3.31:1 | 3.13:1 | 3.0 |
| Stroke.Focus on Surface.Panel | 18.08:1 | 17.68:1 | 3.0 |
| Accent on Surface.Panel | 13.53:1 | 11.50:1 | 3.0 |
| Text.OnAccent on Accent | 13.53:1 | 12.41:1 | 4.5 |

**No pair fails.** Two things were changed to make that true rather than asserted:

1. `Stroke.Border` (`#B4C0C8` light) computes to **1.86:1** on white and could not reach 3:1
   without turning every panel outline into a hard rule. The token was therefore split:
   `Stroke.Border` and `Stroke.Divider` are declared decorative, and a separate `Stroke.Control`
   at >= 3:1 is mandatory for anything whose boundary identifies a control. WCAG 1.4.11 exempts
   purely decorative boundaries; the split makes the exemption explicit rather than convenient.
2. Chip grounds were first specified as a 16 % tint composited in linear light. That is not what
   Skia does, and at 16 % the dark grounds pushed nine pairs below 4.5:1 — `Status.Unknown`
   bottomed out at **3.06:1**. Grounds are now 14 % in sRGB space and are quoted as literals.

**Greyscale and colour vision** (Machado et al. 2009, severity 1.0, applied in linear RGB):

| | PASS:FAIL luminance | protanopia | deuteranopia |
|---|---|---|---|
| Light | 1.45:1 | `#6F6748` vs `#403913` = 2.05:1 | `#645F4C` vs `#5F540D` = 1.19:1 |
| Dark | 1.23:1 | `#C8BB86` vs `#999279` = 1.62:1 | `#B9B08E` vs `#BAAD77` = **1.04:1** |

Read that honestly: **under deuteranopia in the dark theme, PASS and FAIL are the same colour.**
1.04:1 is indistinguishable. This is not a fixable palette problem — green and red are one hue to
a deuteranope, and darkening FAIL far enough to separate them by luminance would take it below
the 4.5:1 floor against a near-black panel. FAIL's light value was moved from `#B01B1B` to
`#941515` to buy 1.45:1 there, which was the most available for free.

The distinction is therefore carried by the **word**, by the **solid-fill inversion** unique to
FAIL, and by the **glyph** — all three of which survive both simulations and a greyscale print
unchanged. Colour is a fourth channel for the majority who can use it, never the channel.

### 1.5 Typography

| Role | Family | Size | Weight | Line height | Token |
|---|---|---|---|---|---|
| Product mark | Sans | 13 | 600, +1.0 tracking, uppercase | 16 | `Text.Primary` |
| Band caption (`TRACE`, drawer headers) | Sans | 11 | 700, +0.8 tracking, uppercase | 14 | `Text.Secondary` |
| Field caption (`ADDR`, `POLL`) | Sans | 11 | 600, +0.6 tracking, uppercase | 13 | `Text.Secondary` |
| Prose, notes, exception meanings | Sans | 12.5 | 400 | 17 | `Text.Secondary` |
| Field and meter **names** | Mono | 12.5 | 400 | 18 | `Text.Data` |
| Field and meter **values** | Mono | 12.5 | 600 | 18 | `Text.Primary` |
| Wire hex | Mono | 12.5 | 400 | 18 | `Text.Data` |
| Wire timestamp | Mono | 11.5 | 400 tabular | 18 | `Text.Secondary` |
| Source location (`file:13`) | Mono | 11 | 400 | 15 | `Text.Secondary` |
| Chip label | Mono | 11 | 700, +0.6 tracking | 14 | per status |
| Footer readout | Mono | 11.5 | 500 tabular | 16 | `Text.Secondary` |

```
Sans : Segoe UI, Inter, Noto Sans, DejaVu Sans, Liberation Sans, sans-serif
Mono : Cascadia Mono, Consolas, DejaVu Sans Mono, Liberation Mono, Noto Sans Mono, monospace
```

Both stacks resolve on stock Windows 10/11 (Segoe UI, Consolas) and on stock Debian, Ubuntu and
Fedora (DejaVu Sans, DejaVu Sans Mono). For a product shipped to customers I recommend embedding
**IBM Plex Sans** and **IBM Plex Mono** (SIL OFL 1.1, redistributable) as `avares://` resources
so a screenshot from a customer's Linux box is identical to one from the lab, keeping the system
stack as the fallback.

**How mono is treated versus prose.** Mono is not a decoration meaning "this is code"; it is the
substance, so it takes the *strongest* token, not the weakest.

* Hex, field names, meter names, values and timestamps are mono at `Text.Data` or
  `Text.Primary`. None of them is `Text.Secondary` except the timestamp, which is an index rather
  than a measurement.
* Prose — an exception's meaning, a field's note, a skip's reason — is sans at `Text.Secondary`.
  It is the annotation; the number is the finding.
* This inverts the current window, where the decoded value and its unit are rendered as the same
  class of thing (`Value` bold, `Note` at 0.55 opacity) and where the hex symbols — the single
  most important text in the application — carry no emphasis at all.
* All numeric mono uses tabular figures (`FontFeatures="+tnum"`) so meter and timestamp columns
  align without hand-padding.

### 1.6 Component specimens

**Status chip.** 78 DIP minimum width, 18 DIP tall, 2 DIP radius, glyph plus word.

```
light:  [v PASS ]      outline #16704A   ground #DEEBE6   label #16704A
        [x FAIL ]      SOLID   #941515   (no outline)     label #FFFFFF
        [o SKIP ]      outline #5A4595   ground #E8E5F0   label #5A4595  + 3 DIP left bar
        [? UNDECODED]  DASHED  #41525E   no ground        label #41525E
        [- UNKNOWN ]   no outline        ground #EAEAEA   label #696969
        [! WARNING ]   outline #8A5A00   ground #EFE8DB   label #8A5A00
```

**Provenance banner.** Band 1, full width, 30 DIP. Proven:

```
+-----------------------------------------------------------------------------------+
| [ PROVEN ]  over tcp 127.0.0.1:9760 (listen)  ·  stimulus none         (*) LINK UP |
+-----------------------------------------------------------------------------------+
  ground Prov.ProvenGround · 3 DIP left rule in Prov.Proven · seal label 11/700/+0.8
```

Unverified — same geometry, `Prov.UnverifiedGround`, and the caveat is promoted from a footnote
into the banner body:

```
+-----------------------------------------------------------------------------------+
| [ UNVERIFIED ]  over serial COM3  ·  stimulus none                     (*) LINK UP |
|                 no adapter has been measured; a pass here is not evidence about    |
|                 hardware                                                           |
+-----------------------------------------------------------------------------------+
  band grows to 44 DIP when a caveat is present. It never scrolls out of view.
```

**Wire log row.** 18 DIP, four columns plus wrap. The wakeup flag is styled *per symbol*:

```
   6157ms  TX  1.01 0.1F     long poll $1F
   ^       ^   ^ ^  ^        ^
   |       |   | |  |        note - sans 12.5, Text.Secondary
   |       |   | |  data symbol - mono, Text.Data
   |       |   | wakeup digit - Wire.Wakeup on Wire.WakeupGround, 1 DIP radius
   |       direction - mono 12.5/700, Wire.Tx for TX or Wire.Rx for RX
   timestamp - mono 11.5 tabular, Text.Secondary, right-aligned
```

TX rows carry a 2 DIP left rule in `Wire.Tx` and a `Wire.TxGround` row tint; RX rows carry no
tint and full-strength `Text.Data` hex. The asymmetry is deliberate: **the machine's answer is
the evidence, our own poll is context.** Channel appears only when more than one channel is open
(the M4 slot), as a 16 DIP `c2` badge before the direction.

**Decoded field row.** 18 DIP, three columns.

```
   game_options          0000                   little-endian
   base_percentage       9210                   hundredths of a percent
   serial_number         [- UNKNOWN ]           length says 3 bytes follow, all version
   ^                     ^                      ^
   mono 12.5 Text.Data   mono 12.5/600          sans 11.5 Text.Secondary
                         Text.Primary
```

A constraint violation replaces the note with a `WARNING` chip and its reason while the value
stays visible, because `SasMessageDecoder` deliberately continues past a violation.

**Meter row.** 18 DIP, four columns, value right-aligned and tabular.

```
   total_bill_value      $20        2000     credits
   bills_20              $1E           1     count
   games_played          $15          14     count
   current_credits       $1A         340     credits
   ^                     ^             ^     ^
   mono Text.Data        mono 11.5     |     sans 11.5 Text.Secondary
                         Text.Secondary mono 12.5/600 tabular, right
```

**Run step row.** 20 DIP for a test, 18 for a step, indented 16.

```
   [v PASS ]  the link comes up and the machine identifies itself
              6.9s, 5 assert(s)                     tests/regression/wire/link-up.sas:1
   [o SKIP ]  10.1.1 standard validation configuration
              egm.validation: this environment provides no 'egm.validation'
                                        tests/conformance/10.1/10.1.1-standard...sas:1
```

The location is mono 11 `Text.Secondary`, right-aligned and middle-ellipsised — present because
D3 requires errors to name the line, but never competing with the verdict.

**Connection bar.** Bezel field groups: 1 DIP `Stroke.Control` box, `Surface.Raised` ground,
caption above at 11/600 uppercase `Text.Secondary`, value inside at 12.5 mono. `Connect` is the
accent button (`Accent` fill, `Text.OnAccent` label); `Disconnect` is a `Stroke.Control` outline
button. Two buttons rather than a toggle, so the enabled one is unambiguous.

**Empty state.** The trace well before a link exists — centred, no illustration:

```
                          No link.

           Choose a link and press CONNECT. loopback and tcp are
           proven; serial is unverified and will say so.
```

**Error state.** Errors appear where they were caused, never in a toast. A bad address puts a
1 DIP `Status.Fail` outline on the `ADDR` field and the message under the connection bar:

```
+----------------------------------------------------------------------------------+
| [x] '0G' is not a poll address. Expected 00 to 7F.                                |
+----------------------------------------------------------------------------------+
   ground Status.FailGround · 3 DIP left rule Status.Fail · text Text.Primary 12.5
```

A refused transport uses the same strip with the refusal's own words, because D-principle 2
makes the refusal the feature — it must read as a decision, not as a crash. A script parse error
renders in the RUN drawer with the location first:

```
   tests/regression/meters/note-moves-bill-meters.sas:13
   [x FAIL ]  expected an exception name here
```

### 1.7 Motion and feedback

The default is that nothing moves.

* **Animated:** drawer expand and collapse, 120 ms `CubicEaseOut` on height only. Chip and row
  colour changes, 90 ms cross-fade on `Background` and `Foreground` — enough to catch the eye
  when a verdict flips, too short to read as an effect. The busy state of `Connect`, `Send` and
  `Run` uses the stock indeterminate `ProgressBar`, 2 DIP tall, under the connection bar.
* **Not animated:** the wire log. No row-entry transition, no smooth scroll, no fade. Follow-mode
  scrolling is an instant jump to the end.
* **Not animated, deliberately:** the unverified banner does not pulse, blink or breathe. A
  warning that moves gets muted by the reader within a minute; a warning that is simply always
  the wrong colour does not.
* **The 200 ms poll loop.** Five wire events a second, each currently one
  `Dispatcher.UIThread.Post` and one `ObservableCollection.Add`, is both visual noise and layout
  churn. Two fixes:
  1. **Coalesce.** Buffer wire lines on the poll thread and flush on a 100 ms `DispatcherTimer`,
     so the list re-lays out at most ten times a second regardless of link speed and the
     trim-to-2000 happens once per flush instead of once per line.
  2. **Do not animate the heartbeat.** Liveness is shown by exactly one thing: the `LINK` lamp, an
     8 DIP ring, filled when up and hollow when down, plus the footer counters updating at 4 Hz.
     A poll that arrives on time is not an event and gets no pixel of its own.
* Sound: none, ever.

### 1.8 Avalonia implementation notes

* **Controls.** `DockPanel` for the four bands; `Grid` plus `GridSplitter` for the body;
  `Expander` per drawer with a `ControlTheme` giving it the caption bar and summary line;
  `ListBox` (default `VirtualizingStackPanel`) for the trace well and run steps; a nested
  `ItemsControl` for the symbols in a wire row. Meters and decoded fields eventually want
  sortable columns — that is `TreeDataGrid` from the separate `Avalonia.Controls.TreeDataGrid`
  package, and it is the only extra dependency any proposal here needs.
* **Use `SelectableTextBlock`, not `TextBlock`, for hex and field values.** A protocol tool whose
  hex cannot be copied is a toy. One word per template, and it is not optional.
* **Avalonia cannot dash a `Border`** — there is no `BorderDashArray`. The UNDECODED chip is
  therefore a `Grid` holding a `Rectangle` with `StrokeDashArray` behind the label; the same
  technique covers any dashed rule.
* **Per-symbol wakeup styling requires a view-model change.** `WireLine.Symbols` is one joined
  `string` today, so bit 8 cannot be highlighted. It must become `IReadOnlyList<WireSymbol>` with
  `Text` and `IsWakeup`. Building that on the poll thread is fine — the record is already
  constructed there — whereas building `Inlines` is not, because inlines must be created on the
  UI thread. If profiling shows the nested `ItemsControl` is too heavy at ten flushes a second,
  replace it with a small custom `Control` that draws the run in `Render(DrawingContext)`; that
  is the fallback, not the starting point.
* **`Classes.tx="{Binding IsTransmit}"`** drives row styling with no converter. Add `IsTransmit`
  to `WireLine`.
* **Theme structure.** `Themes/Tokens.axaml` holds the `ThemeDictionaries`;
  `Themes/Components.axaml` holds the `Styles`; `App.axaml` merges both and keeps
  `RequestedThemeVariant="Default"` so the OS decides, with a user override writing
  `Application.Current.RequestedThemeVariant`. Wrap previews in
  `<ThemeVariantScope RequestedThemeVariant="Dark">` so both variants are visible in the designer.

`Themes/Tokens.axaml` — the dictionary shape, abridged to one band of tokens. Every key must
exist in **both** variants or a `DynamicResource` silently resolves to nothing at runtime:

```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

  <FontFamily x:Key="FontSans">Segoe UI, Inter, Noto Sans, DejaVu Sans, sans-serif</FontFamily>
  <FontFamily x:Key="FontMono">Cascadia Mono, Consolas, DejaVu Sans Mono, monospace</FontFamily>

  <ResourceDictionary.ThemeDictionaries>

    <ResourceDictionary x:Key="Light">
      <SolidColorBrush x:Key="SurfaceWindow"        Color="#EDF0F2" />
      <SolidColorBrush x:Key="SurfacePanel"         Color="#FFFFFF" />
      <SolidColorBrush x:Key="SurfaceRaised"        Color="#F7F9FA" />
      <SolidColorBrush x:Key="SurfaceSunken"        Color="#E4E9EC" />
      <SolidColorBrush x:Key="StrokeDivider"        Color="#DCE3E7" />
      <SolidColorBrush x:Key="StrokeControl"        Color="#7D8B97" />
      <SolidColorBrush x:Key="StrokeFocus"          Color="#10171C" />
      <SolidColorBrush x:Key="TextPrimary"          Color="#10171C" />
      <SolidColorBrush x:Key="TextSecondary"        Color="#46545E" />
      <SolidColorBrush x:Key="TextData"             Color="#14202A" />
      <SolidColorBrush x:Key="WireTx"               Color="#8A5300" />
      <SolidColorBrush x:Key="WireTxGround"         Color="#EFE7DB" />
      <SolidColorBrush x:Key="WireRx"               Color="#0A5F70" />
      <SolidColorBrush x:Key="WireWakeup"           Color="#6B4A00" />
      <SolidColorBrush x:Key="WireWakeupGround"     Color="#EAE6DB" />
      <SolidColorBrush x:Key="StatusPass"           Color="#16704A" />
      <SolidColorBrush x:Key="StatusPassGround"     Color="#DEEBE6" />
      <SolidColorBrush x:Key="StatusFail"           Color="#941515" />
      <SolidColorBrush x:Key="TextOnFail"           Color="#FFFFFF" />
      <SolidColorBrush x:Key="StatusSkip"           Color="#5A4595" />
      <SolidColorBrush x:Key="StatusSkipGround"     Color="#E8E5F0" />
      <SolidColorBrush x:Key="StatusUndecoded"      Color="#41525E" />
      <SolidColorBrush x:Key="StatusUnknown"        Color="#696969" />
      <SolidColorBrush x:Key="StatusUnknownGround"  Color="#EAEAEA" />
      <SolidColorBrush x:Key="ProvProven"           Color="#0E6A50" />
      <SolidColorBrush x:Key="ProvProvenGround"     Color="#DDEAE6" />
      <SolidColorBrush x:Key="ProvUnverified"       Color="#8A4B00" />
      <SolidColorBrush x:Key="ProvUnverifiedGround" Color="#EFE6DB" />
    </ResourceDictionary>

    <ResourceDictionary x:Key="Dark">
      <SolidColorBrush x:Key="SurfaceWindow"        Color="#0D1013" />
      <SolidColorBrush x:Key="SurfacePanel"         Color="#14191E" />
      <SolidColorBrush x:Key="SurfaceRaised"        Color="#1C232A" />
      <SolidColorBrush x:Key="SurfaceSunken"        Color="#070909" />
      <SolidColorBrush x:Key="StrokeDivider"        Color="#1E262C" />
      <SolidColorBrush x:Key="StrokeControl"        Color="#657079" />
      <SolidColorBrush x:Key="StrokeFocus"          Color="#FFFFFF" />
      <SolidColorBrush x:Key="TextPrimary"          Color="#E6ECF1" />
      <SolidColorBrush x:Key="TextSecondary"        Color="#AAB8C2" />
      <SolidColorBrush x:Key="TextData"             Color="#DCE7EF" />
      <SolidColorBrush x:Key="WireTx"               Color="#F0A93C" />
      <SolidColorBrush x:Key="WireTxGround"         Color="#332D22" />
      <SolidColorBrush x:Key="WireRx"               Color="#55CADC" />
      <SolidColorBrush x:Key="WireWakeup"           Color="#FFD466" />
      <SolidColorBrush x:Key="WireWakeupGround"     Color="#353328" />
      <SolidColorBrush x:Key="StatusPass"           Color="#5FC98A" />
      <SolidColorBrush x:Key="StatusPassGround"     Color="#1E322D" />
      <SolidColorBrush x:Key="StatusFail"           Color="#FF7A7A" />
      <SolidColorBrush x:Key="TextOnFail"           Color="#000000" />
      <SolidColorBrush x:Key="StatusSkip"           Color="#B9A6E8" />
      <SolidColorBrush x:Key="StatusSkipGround"     Color="#2B2D3A" />
      <SolidColorBrush x:Key="StatusUndecoded"      Color="#93A6B4" />
      <SolidColorBrush x:Key="StatusUnknown"        Color="#9A9A9A" />
      <SolidColorBrush x:Key="StatusUnknownGround"  Color="#272B2F" />
      <SolidColorBrush x:Key="ProvProven"           Color="#6FD0A8" />
      <SolidColorBrush x:Key="ProvProvenGround"     Color="#213331" />
      <SolidColorBrush x:Key="ProvUnverified"       Color="#F2A93B" />
      <SolidColorBrush x:Key="ProvUnverifiedGround" Color="#332D22" />
    </ResourceDictionary>

  </ResourceDictionary.ThemeDictionaries>
</ResourceDictionary>
```

`Themes/Components.axaml` — the wire row and the chip, styled entirely from tokens:

```xml
<Styles xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

  <!-- Data text: mono, tabular figures, full-strength token. Never an Opacity setter. -->
  <Style Selector="TextBlock.data">
    <Setter Property="FontFamily" Value="{DynamicResource FontMono}" />
    <Setter Property="FontSize" Value="12.5" />
    <Setter Property="LineHeight" Value="18" />
    <Setter Property="FontFeatures" Value="+tnum" />
    <Setter Property="Foreground" Value="{DynamicResource TextData}" />
  </Style>

  <Style Selector="TextBlock.caption">
    <Setter Property="FontFamily" Value="{DynamicResource FontSans}" />
    <Setter Property="FontSize" Value="11" />
    <Setter Property="FontWeight" Value="Bold" />
    <Setter Property="LetterSpacing" Value="0.8" />
    <Setter Property="Foreground" Value="{DynamicResource TextSecondary}" />
  </Style>

  <!-- One wire row. Classes.tx is bound to WireLine.IsTransmit. -->
  <Style Selector="Border.wirerow">
    <Setter Property="BorderThickness" Value="2,0,0,0" />
    <Setter Property="BorderBrush" Value="Transparent" />
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="Padding" Value="6,0,6,0" />
    <Setter Property="MinHeight" Value="18" />
  </Style>
  <Style Selector="Border.wirerow.tx">
    <Setter Property="BorderBrush" Value="{DynamicResource WireTx}" />
    <Setter Property="Background" Value="{DynamicResource WireTxGround}" />
  </Style>
  <Style Selector="Border.wirerow.tx TextBlock.dir">
    <Setter Property="Foreground" Value="{DynamicResource WireTx}" />
  </Style>
  <Style Selector="Border.wirerow:not(.tx) TextBlock.dir">
    <Setter Property="Foreground" Value="{DynamicResource WireRx}" />
  </Style>

  <!-- One symbol. Classes.wakeup is bound to WireSymbol.IsWakeup. -->
  <Style Selector="Border.sym">
    <Setter Property="CornerRadius" Value="1" />
    <Setter Property="Padding" Value="2,0,2,0" />
    <Setter Property="Margin" Value="0,0,4,0" />
  </Style>
  <Style Selector="Border.sym.wakeup">
    <Setter Property="Background" Value="{DynamicResource WireWakeupGround}" />
  </Style>
  <Style Selector="Border.sym.wakeup > TextBlock">
    <Setter Property="Foreground" Value="{DynamicResource WireWakeup}" />
    <Setter Property="FontWeight" Value="SemiBold" />
  </Style>

  <!-- Status chip: word and fill treatment carry the state; colour is the fourth channel. -->
  <Style Selector="Border.chip">
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="CornerRadius" Value="2" />
    <Setter Property="Padding" Value="6,1,6,2" />
    <Setter Property="MinWidth" Value="78" />
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="BorderBrush" Value="{DynamicResource StrokeControl}" />
  </Style>
  <Style Selector="Border.chip > TextBlock">
    <Setter Property="FontFamily" Value="{DynamicResource FontMono}" />
    <Setter Property="FontSize" Value="11" />
    <Setter Property="FontWeight" Value="Bold" />
    <Setter Property="LetterSpacing" Value="0.6" />
    <Setter Property="TextAlignment" Value="Center" />
    <Setter Property="Foreground" Value="{DynamicResource TextSecondary}" />
  </Style>

  <Style Selector="Border.chip.pass">
    <Setter Property="BorderBrush" Value="{DynamicResource StatusPass}" />
    <Setter Property="Background" Value="{DynamicResource StatusPassGround}" />
  </Style>
  <Style Selector="Border.chip.pass > TextBlock">
    <Setter Property="Foreground" Value="{DynamicResource StatusPass}" />
  </Style>

  <!-- FAIL is the only solid chip in the application: the figure/ground inversion survives
       greyscale, deuteranopia and a printed report. -->
  <Style Selector="Border.chip.fail">
    <Setter Property="BorderBrush" Value="{DynamicResource StatusFail}" />
    <Setter Property="Background" Value="{DynamicResource StatusFail}" />
  </Style>
  <Style Selector="Border.chip.fail > TextBlock">
    <Setter Property="Foreground" Value="{DynamicResource TextOnFail}" />
  </Style>

  <Style Selector="Border.chip.skip">
    <Setter Property="BorderBrush" Value="{DynamicResource StatusSkip}" />
    <Setter Property="Background" Value="{DynamicResource StatusSkipGround}" />
    <Setter Property="BorderThickness" Value="3,1,1,1" />
  </Style>
  <Style Selector="Border.chip.skip > TextBlock">
    <Setter Property="Foreground" Value="{DynamicResource StatusSkip}" />
  </Style>

  <Style Selector="Border.chip.unknown">
    <Setter Property="BorderBrush" Value="Transparent" />
    <Setter Property="Background" Value="{DynamicResource StatusUnknownGround}" />
  </Style>
  <Style Selector="Border.chip.unknown > TextBlock">
    <Setter Property="Foreground" Value="{DynamicResource StatusUnknown}" />
  </Style>

</Styles>
```

The UNDECODED chip cannot be a `Border`, so it is its own small template:

```xml
<Grid MinWidth="90" Height="18">
  <Rectangle Stroke="{DynamicResource StatusUndecoded}" StrokeThickness="1"
             StrokeDashArray="3,2" RadiusX="2" RadiusY="2" />
  <TextBlock Text="UNDECODED" VerticalAlignment="Center" TextAlignment="Center"
             FontFamily="{DynamicResource FontMono}" FontSize="11" FontWeight="Bold"
             LetterSpacing="0.6" Foreground="{DynamicResource StatusUndecoded}" />
</Grid>
```

---
## 2 · Proposal B — **CONSOLE**

### 2.1 Character

**A docked development shell.** An activity rail down the left, a tabbed document area in the
middle, a context inspector on the right, and a bottom dock that behaves like a problems panel.
The shape a QSP developer already has open on the other monitor. Its distinguishing bet is
**growth**: every M2–M4 surface is a rail entry and a dock tab, and adding one costs nothing
structurally.

* **Flatters most:** other QSP/libsas developers. It reads as a debugger, panels are dockable and
  closable, and the inspector is exactly where they expect the decode of the selected wire row.
* **Serves worst:** customers and regulators. It is the most obviously *developer* of the four —
  a screenshot looks like somebody's IDE, and the provenance has to fight a busy chrome for
  attention. It is also the direction most at risk of the panel sprawl that makes QA engineers
  give up and use the CLI.

### 2.2 Layout

* **Command bar, 40 DIP.** The connection collapses to a single pill — lamp, endpoint, address,
  channel, interval — that opens a popover to edit. Then `Disconnect`, then the provenance chip,
  then a `Ctrl+K` command entry. Reclaiming 16 DIP of vertical from Proposal A matters at 614 DIP.
* **Activity rail, 56 DIP wide** (icon over an 9 DIP label, so it is legible rather than a
  guessing game): WIRE · DECODE · METERS · EXCEPTIONS · RUN, a divider, then the future slots
  CATALOGUE (M4) · STIMULUS (M2) · TICKETS (M3). Slots are visible and disabled with a reason on
  hover, which is the same "never silently absent" discipline the runner applies to a SKIP.
* **Document area.** A `TabControl` of open surfaces. Two can be split side by side.
* **Inspector, 300 DIP, collapsible.** Shows the decode of whatever is selected in the document
  area — select an RX row in the wire and its fields appear here. This is the single best idea in
  this direction and it is not available in any of the others: **the wire and the decode are the
  same object seen twice.**
* **Bottom dock, 160 DIP, resizable, collapsible.** Tabs RUN · PROBLEMS · OUTPUT. Run steps live
  here permanently, so a run's verdict is visible whatever the document area is showing.
* **Status bar, 24 DIP.** Counters, provenance triple, theme toggle.

**1180x760**

```
+-------------------------------------------------------------------------------------------------+
| (*) tcp 127.0.0.1:9760 · 01 · ch1 · 200ms  [Disconnect]  [ PROVEN ]           Ctrl+K  commands   |
+-----+------------------------------------------------------+--------------------------------------+
|WIRE | WIRE x | DECODE $1F x |                              | INSPECTOR     lp $1F machine_id      |
|- - -|------------------------------------------------------|--------------------------------------|
|DECOD|   5927ms  TX  1.80  sync poll - address zero          | game_id            QX                |
|- - -|   5954ms  TX  1.81  general poll                      | additional_id      001               |
|METER|   5985ms  RX  0.18                                    | denomination       2          code   |
|- - -|   6126ms  TX  1.80  sync poll - address zero          | max_bet            255     credits   |
|EXCEP|   6157ms  TX  1.01 0.1F   long poll $1F               | progressive_group  0         index   |
|- - -| > 6189ms  RX  0.01 0.1F 0.51 0.58 0.30 0.30 0.31      | game_options       0000  little-end. |
|RUN  |             0.02 0.FF 0.00 0.00 0.00 0.30 0.30        | paytable_id        001               |
|=====|             0.31 0.00 0.00 0.00 0.39 0.32 0.31        | base_percentage    9210  hundredths  |
|CATLG|             0.30 0.86 0.3F                            |                             of a %   |
|STIM |   6730ms  TX  1.80  sync poll - address zero          |--------------------------------------|
|TICKT|   6761ms  TX  1.01 0.54   long poll $54               | from lp $54                          |
|     |   6791ms  RX  0.01 0.54 0.03 0.36 0.30 0.32 0.D0 0.8D | sas_version        602               |
|     |                                                       | serial_number   [- UNKNOWN ]         |
|     |                                                       |   length says 3 bytes follow, and    |
|     |                                                       |   all three are the version          |
+-----+------------------------------------------------------+--------------------------------------+
| RUN | PROBLEMS 1 | OUTPUT                    tests/regression        [ PASS ]  1 passed in 7.0s   |
|  [v PASS ] the link comes up and the machine identifies itself                                    |
|            6.9s, 5 assert(s)                              tests/regression/wire/link-up.sas:1     |
|  [o SKIP ] 10.1.1 standard validation configuration                                               |
|            egm.validation: this environment provides no 'egm.validation'                          |
|                                              tests/conformance/10.1/10.1.1-standard-validation:1  |
+---------------------------------------------------------------------------------------------------+
| 34 sync  34 general  2 long  1 exc  0 silences      tcp · proven · stimulus none        [light]   |
+---------------------------------------------------------------------------------------------------+
```

**900x600** — the inspector becomes an overlay flyout from the right edge (opened by selecting a
row, closed with Escape), the rail keeps its labels because 56 DIP is affordable and unlabelled
icons are a memory test, and the bottom dock drops to 110 DIP showing three step rows.

```
+------------------------------------------------------------------------+
| (*) tcp ...9760 · 01 · ch1 [Disconnect] [ PROVEN ]        Ctrl+K        |
+-----+------------------------------------------------------------------+
|WIRE | WIRE x | DECODE $1F x |                                          |
|- - -|------------------------------------------------------------------|
|DECOD|  5927ms  TX  1.80  sync poll - address zero                       |
|- - -|  5954ms  TX  1.81  general poll                                   |
|METER|  5985ms  RX  0.18                                                 |
|- - -|  6126ms  TX  1.80  sync poll - address zero                       |
|EXCEP|  6157ms  TX  1.01 0.1F   long poll $1F                            |
|- - -|> 6189ms  RX  0.01 0.1F 0.51 0.58 0.30 0.30 0.31 0.02 0.FF 0.00    |
|RUN  |            0.00 0.00 0.30 0.30 0.31 0.00 0.00 0.00 0.39 0.32      |
|=====|            0.31 0.30 0.86 0.3F                                    |
|CATLG|  6730ms  TX  1.80  sync poll - address zero                       |
|STIM |  6761ms  TX  1.01 0.54   long poll $54                            |
|TICKT|  6791ms  RX  0.01 0.54 0.03 0.36 0.30 0.32 0.D0 0.8D              |
+-----+------------------------------------------------------------------+
| RUN | PROBLEMS 1 | OUTPUT                   [ PASS ]  1 passed in 7.0s  |
|  [v PASS ] the link comes up and the machine identifies itself          |
|  [o SKIP ] 10.1.1 standard validation configuration                     |
|            egm.validation: this environment provides no 'egm.validation'|
+------------------------------------------------------------------------+
| 34s 34g 2l 1e 0sil            tcp · proven · none              [light]  |
+------------------------------------------------------------------------+
```

**At 125 %** this is the direction that survives clamping best, because the inspector and the
bottom dock are both collapsible and both remember their state — the layout has two real degrees
of freedom that A and D do not. The cost is that the provenance chip in the command bar is a
36x18 DIP object competing with six other things, which is a demotion relative to Proposal A's
full-width rail.

### 2.3 Tokens

| Token | Light | Dark |
|---|---|---|
| `Surface.Window` | `#F0F2F5` | `#1B1D21` |
| `Surface.Panel` | `#FFFFFF` | `#212429` |
| `Surface.Raised` | `#F6F8FA` | `#2A2E34` |
| `Surface.Sunken` | `#EAEEF2` | `#16181B` |
| `Stroke.Border` | `#AFB8C1` | `#3A4048` |
| `Stroke.Divider` | `#D8DEE4` | `#292D33` |
| `Stroke.Control` | `#838B95` | `#737980` |
| `Stroke.Focus` | `#0969DA` | `#6CB6FF` |
| `Text.Primary` | `#1F2328` | `#E7E9EC` |
| `Text.Secondary` | `#57606A` | `#A9AFB8` |
| `Text.Disabled` | `#818B95` | `#767D86` |
| `Text.Data` | `#24292F` | `#DDE3EA` |
| `Accent` | `#0969DA` | `#6CB6FF` |
| `Accent.Ground` | `#DDEAFA` | `#2C3847` |
| `Text.OnAccent` | `#FFFFFF` | `#0B1620` |
| `Wire.Tx` | `#0550AE` | `#6CB6FF` |
| `Wire.TxGround` | `#DCE6F4` | `#2C3847` |
| `Wire.Rx` | `#4C5A67` | `#9FB0C0` |
| `Wire.Wakeup` | `#6E4E00` | `#E3B341` |
| `Wire.WakeupGround` | `#EBE6DB` | `#3C382C` |
| `Status.Pass` | `#197835` | `#3FB950` |
| `Status.PassGround` | `#DFECE3` | `#25392E` |
| `Status.Fail` | `#A71923` | `#F97B77` |
| `Status.FailGround` | `#F3DFE0` | `#3F3034` |
| `Text.OnFail` | `#FFFFFF` | `#000000` |
| `Status.Skip` | `#6639BA` | `#B28EF8` |
| `Status.SkipGround` | `#EAE3F5` | `#353346` |
| `Status.Undecoded` | `#4E5964` | `#A0AAB4` |
| `Status.UndecodedGround` | `#E6E8E9` | `#33373C` |
| `Status.Unknown` | `#696969` | `#A1A1A1` |
| `Status.UnknownGround` | `#EAEAEA` | `#33363A` |
| `Status.Warning` | `#8A5D00` | `#D29922` |
| `Status.WarningGround` | `#EFE8DB` | `#3A3428` |
| `Prov.Proven` | `#197835` | `#3FB950` |
| `Prov.ProvenGround` | `#DFECE3` | `#25392E` |
| `Prov.Unverified` | `#8A5D00` | `#D29922` |
| `Prov.UnverifiedGround` | `#EFE8DB` | `#3A3428` |

Two identities here are deliberate, not accidents:

* `Prov.Unverified` **is** `Status.Warning`. An unverified link is a warning-class fact, and
  giving it a private hue would suggest it is a fifth category rather than a caution.
* `Prov.Proven` **is** `Status.Pass`. Proven means the link has been measured; that is the same
  quality of claim as a passing assertion.

One collision is accepted rather than deliberate: in the dark variant `Wire.Tx`, `Accent` and
`Stroke.Focus` are all `#6CB6FF`. In an IDE-shaped application a single accent blue used
everywhere is idiomatic, and the three never carry meaning in the same place — focus is a 2 DIP
offset ring, TX is a 2 DIP left gutter, accent is a button fill. It is still the weakest point in
this palette and it is the reason A and D reserve chroma for data only.

### 2.4 Contrast audit

| Pair | Light | Dark | Target |
|---|---|---|---|
| Text.Primary on Surface.Window | 14.09:1 | 13.87:1 | 4.5 |
| Text.Primary on Surface.Panel | 15.80:1 | 12.80:1 | 4.5 |
| Text.Primary on Surface.Raised | 14.84:1 | 11.22:1 | 4.5 |
| Text.Primary on Surface.Sunken | 13.55:1 | 14.62:1 | 4.5 |
| Text.Secondary on Surface.Panel | 6.39:1 | 7.05:1 | 4.5 |
| Text.Secondary on Surface.Raised | 6.00:1 | 6.18:1 | 4.5 |
| Text.Disabled on Surface.Panel | 3.47:1 | 3.74:1 | 3.0 |
| Text.Data on Surface.Panel | 14.65:1 | 12.05:1 | 4.5 |
| Text.Data on Surface.Sunken | 12.57:1 | 13.76:1 | 4.5 |
| Wire.Tx on Surface.Sunken | 6.51:1 | 8.28:1 | 4.5 |
| Wire.Tx on Wire.TxGround | 6.03:1 | 5.54:1 | 4.5 |
| Wire.Rx on Surface.Sunken | 6.07:1 | 8.00:1 | 4.5 |
| Wire.Wakeup on Surface.Sunken | 6.54:1 | 9.14:1 | 4.5 |
| Wire.Wakeup on Wire.WakeupGround | 6.13:1 | 6.01:1 | 4.5 |
| Status.Pass on Surface.Panel | 5.56:1 | 6.13:1 | 4.5 |
| Status.Pass on Status.PassGround | 4.56:1 | 4.85:1 | 4.5 |
| Status.Fail on Surface.Panel | 7.48:1 | 6.02:1 | 4.5 |
| Status.Fail on Status.FailGround | 5.86:1 | 4.83:1 | 4.5 |
| Text.OnFail on Status.Fail (solid chip) | 7.48:1 | 8.13:1 | 4.5 |
| Status.Skip on Surface.Panel | 7.34:1 | 6.02:1 | 4.5 |
| Status.Skip on Status.SkipGround | 5.88:1 | 4.74:1 | 4.5 |
| Status.Undecoded on Surface.Panel | 7.15:1 | 6.60:1 | 4.5 |
| Status.Undecoded on Status.UndecodedGround | 5.82:1 | 5.08:1 | 4.5 |
| Status.Unknown on Surface.Panel | 5.49:1 | 6.03:1 | 4.5 |
| Status.Unknown on Status.UnknownGround | 4.56:1 | 4.70:1 | 4.5 |
| Status.Warning on Surface.Panel | 5.76:1 | 6.17:1 | 4.5 |
| Status.Warning on Status.WarningGround | 4.73:1 | 4.89:1 | 4.5 |
| Prov.Proven on Prov.ProvenGround | 4.56:1 | 4.85:1 | 4.5 |
| Prov.Unverified on Prov.UnverifiedGround | 4.73:1 | 4.89:1 | 4.5 |
| Stroke.Control on Surface.Panel | 3.45:1 | 3.54:1 | 3.0 |
| Stroke.Control on Surface.Raised | 3.24:1 | 3.10:1 | 3.0 |
| Stroke.Focus on Surface.Panel | 5.19:1 | 7.24:1 | 3.0 |
| Accent on Surface.Panel | 5.19:1 | 7.24:1 | 3.0 |
| Text.OnAccent on Accent | 5.19:1 | 8.50:1 | 4.5 |

No pair fails, but this palette has the **least headroom** of the four: eight pairs sit between
4.5 and 5.0. `Status.Pass` on its ground is 4.56:1 light and 4.85:1 dark — a single "let's
lighten the green a touch" would break it. That is the price of adopting values close to a
familiar developer palette rather than deriving them from the contrast targets.

`Text.Disabled` at 3.47:1 light is the lowest disabled value of the four; it clears the 3:1 UI
threshold and disabled text is exempt from 1.4.3 anyway, but it is worth knowing.

**Greyscale and colour vision:**

| | PASS:FAIL luminance | protanopia | deuteranopia |
|---|---|---|---|
| Light | 1.35:1 | `#796D2F` vs `#484122` = 1.97:1 | `#6E653A` vs `#6B5F1D` = 1.09:1 |
| Dark | **1.02:1** | `#BCA946` vs `#989176` = 1.34:1 | `#AD9E58` vs `#B7AB74` = 1.16:1 |

The dark theme's 1.02:1 is the worst PASS/FAIL luminance separation of any variant in this
document — the two are the same brightness, so a greyscale screenshot of the dark theme
distinguishes them by nothing but the chip fill and the word. Both are present, so the contract
holds, but this palette leans on that mitigation harder than the others do.

### 2.5 Typography

| Role | Family | Size | Weight | Line height | Token |
|---|---|---|---|---|---|
| Rail label | Sans | 10 | 600, +0.4 tracking, uppercase | 12 | `Text.Secondary` |
| Tab label | Sans | 12 | 500 | 16 | `Text.Secondary`, selected `Text.Primary` |
| Panel caption (`INSPECTOR`) | Sans | 11 | 700, +0.8 tracking, uppercase | 14 | `Text.Secondary` |
| Prose, notes, meanings | Sans | 12.5 | 400 | 17 | `Text.Secondary` |
| Field and meter names | Mono | 12.5 | 400 | 18 | `Text.Data` |
| Field and meter values | Mono | 12.5 | 600 | 18 | `Text.Primary` |
| Wire hex | Mono | 12.5 | 400 | 18 | `Text.Data` |
| Wire timestamp | Mono | 11.5 | 400 tabular | 18 | `Text.Secondary` |
| Location | Mono | 11 | 400 | 15 | `Text.Secondary` |
| Chip label | Mono | 11 | 700, +0.6 tracking | 14 | per status |
| Status bar | Sans | 11 | 400 | 14 | `Text.Secondary` |

Same two font stacks as Proposal A. Prose is slightly more prominent here than in A because the
inspector carries longer descriptions, but the rule is identical: **mono data outranks sans
prose**, and no hierarchy is ever expressed with `Opacity`.

### 2.6 Component specimens

Chips, wire rows, decoded field rows, meter rows and step rows are geometrically the same as
Proposal A — the difference is the palette and where they live. What is specific to B:

**The wire row is selectable and drives the inspector.** A selected row takes
`Surface.Raised` plus a 2 DIP `Accent` left rule; the `>` marker in the wireframe is the
selection caret. Selecting an RX row decodes it into the inspector. Selecting a TX row shows the
encoded request. This is the mechanism that makes the M4 trace-replay and catalogue-diff views
cheap: they are just more things the inspector can be pointed at.

**The rail entry.** 56x48 DIP, icon 20 DIP over a 10 DIP label. States: default
`Text.Secondary`; hover `Surface.Raised`; selected `Surface.Panel` with a 3 DIP `Accent` left
rule and `Text.Primary`; **unavailable** `Text.Disabled` with a tooltip carrying the reason
("stimulus: M2 — no backend is wired yet"). An unavailable rail entry is not hidden, for the same
reason a skipped test is not deleted.

**The provenance chip in the command bar.** 18 DIP tall, and it is a button: clicking it opens a
popover with the full provenance — link, fidelity, stimulus backend, caveat, and the sentence
about what a pass over this link is and is not evidence for. The compact form is
`[ PROVEN ]` or `[ UNVERIFIED ]`; when unverified, the command bar's own background becomes
`Prov.UnverifiedGround` so the demotion to a chip does not cost the warning its visibility.

**The bottom dock's PROBLEMS tab** carries a count badge and aggregates: parse errors, refused
transports, constraint violations, and the M1 finding class — an exception queued before the host
asked for anything:

```
  [! WARNING ]  $18  ac_power_lost  arrived before the host had asked for anything
                AC Power was lost from gaming machine
                a test that begins by asserting an empty exception queue will fail here
                for reasons unrelated to what it is testing
```

**Empty state** (document area, no link):

```
     No link.

     Connect from the command bar, or press Ctrl+K and type "connect".
     loopback and tcp are proven. serial is unverified and will say so.
```

**Error state** — the bottom dock's PROBLEMS tab, with the location first and the expectation
named, matching what the parser already produces:

```
  [x FAIL ]  tests/regression/meters/note-moves-bill-meters.sas:13
             expected an exception name here
```

### 2.7 Motion and feedback

* **Animated:** panel collapse/expand and the inspector flyout, 140 ms `CubicEaseOut` on width;
  tab selection underline, 100 ms slide; the same 90 ms chip cross-fade as A.
* **Not animated:** the wire log, the rail, the status bar.
* **The 200 ms poll loop:** identical to A — coalesce on a 100 ms `DispatcherTimer`, no per-row
  animation, liveness carried by the command-bar lamp and by counters at 4 Hz. In this direction
  there is a second temptation to resist: a "network activity" spinner in the status bar. It
  would spin permanently while polling and therefore mean nothing.
* The bottom dock does **not** auto-open on a new problem while a run is in progress. It opens
  once, at the end of the run, if the run's verdict is not PASS.

### 2.8 Avalonia implementation notes

* **Controls.** `SplitView` for the rail (`DisplayMode="CompactInline"`, `CompactPaneLength="56"`),
  `TabControl` for the document area and the bottom dock, `GridSplitter` between document and
  inspector and between document and dock, `ListBox` throughout. The inspector's flyout form at
  narrow widths is a second `SplitView` with `DisplayMode="Overlay"` and
  `PanePlacement="Right"` — no custom control needed.
* **Do not reach for a docking library.** Dock.Avalonia would give real tear-off docking and cost
  a large third-party dependency in a product that ships to gaming customers. Fixed regions with
  splitters cover everything M2–M4 needs.
* The rail's disabled entries are `ListBoxItem`s with `IsEnabled="False"` and a
  `ToolTip.Tip`; note that Avalonia does not show tooltips on disabled controls by default, so
  the entry must stay enabled and refuse on click with the reason, which is better behaviour
  anyway.
* Same `Tokens.axaml` / `Components.axaml` structure and the same `Border.chip` styles as
  Proposal A — only the hexes differ. The rail item theme is the one additional control theme:

```xml
<Style Selector="ListBoxItem.rail">
  <Setter Property="Height" Value="48" />
  <Setter Property="Padding" Value="0" />
  <Setter Property="BorderThickness" Value="3,0,0,0" />
  <Setter Property="BorderBrush" Value="Transparent" />
  <Setter Property="Background" Value="Transparent" />
  <Setter Property="Foreground" Value="{DynamicResource TextSecondary}" />
</Style>
<Style Selector="ListBoxItem.rail:pointerover /template/ ContentPresenter">
  <Setter Property="Background" Value="{DynamicResource SurfaceRaised}" />
</Style>
<Style Selector="ListBoxItem.rail:selected">
  <Setter Property="BorderBrush" Value="{DynamicResource Accent}" />
  <Setter Property="Foreground" Value="{DynamicResource TextPrimary}" />
</Style>
<Style Selector="ListBoxItem.rail:selected /template/ ContentPresenter">
  <Setter Property="Background" Value="{DynamicResource SurfacePanel}" />
</Style>
```

(The `/template/ ContentPresenter` hop is required: `ListBoxItem`'s own `Background` is consumed
by its default control theme, so setting it on the item has no visible effect.)

---
## 3 · Proposal C — **DOSSIER**

### 3.1 Character

**A document of record.** The window is laid out as the run report it is about to produce: a
masthead that states what the run was carried over *before* any result, then numbered sections —
VERDICT, TESTS, ASSERTIONS, DECODED REPLIES, METERS, EXCEPTIONS — set in ruled tables on a warm
paper ground, with the live wire running down the right margin like a printer tape. Light is the
primary variant; the dark variant is a night ledger, not a terminal.

The argument for it is not aesthetic. D13 already commits to a self-contained HTML report as the
default output, and D-principle 4 says the reader must not have to hunt for what a run was worth.
If the screen and the report share a layout, then the screenshot **is** the report, the two cannot
drift, and a regulator reading either one meets the provenance in the same place.

* **Flatters most:** customers, integrators and regulators. A screenshot pasted into a submission
  reads as evidence rather than as tooling. QA engineers are a close second — the numbered
  sections are a triage order.
* **Serves worst:** the user. A protocol engineer chasing a byte wants the wire at full width and
  a decode next to it, and this direction gives the wire a 300 DIP margin column. It is the
  direction most likely to be abandoned for the CLI during hard debugging.

### 3.2 Layout

* **Masthead, 108 DIP.** Product mark, document title `RUN RECORD`, a heavy rule, then a
  two-column definition list: Link / Address / Started on the left, Provenance / Stimulus /
  Caveat on the right. **The provenance is in the masthead, in the same type size as the link
  itself, above the fold and above every result.** It cannot be scrolled away because the masthead
  is pinned.
* **Body, single scrolling column, measure capped at 760 DIP.** Numbered sections with a
  sans small-caps section head, a hairline rule, and dense ruled tables. This is the one place
  in this document where surplus width becomes margin rather than content, and it is functional:
  a 760 DIP measure is what makes the assertion table readable as a table rather than as a smear.
* **Margin tape, 300 DIP, right.** The live wire, set narrow and wrapped, like a paper roll. It
  is deliberately *not* the focus; it is the raw evidence the sections are derived from.
* **Footer, 26 DIP.** Counters and the provenance triple, so a cropped screenshot still carries
  it.

Chrome is the masthead rule, the section heads and the footer. Everything else is content.

**1180x760**

```
+---------------------------------------------------------------------------------------------------+
|  SasWorkbench                                                        [ light | dark ]    [ Print ] |
|  RUN RECORD                                                                                        |
|  ================================================================================================= |
|  Link       tcp 127.0.0.1:9760 (listen)        Provenance   [ PROVEN ]                             |
|  Address    01 · channel 1 · poll every 200ms  Stimulus     none                                   |
|  Started    2026-09-04 10:12:41                Caveat       none                                   |
+---------------------------------------------------------------+-----------------------------------+
|                                                                 | WIRE                             |
|  1.  VERDICT                                                    | 5927 TX 1.80  sync poll -        |
|      [ PASS ]   1 passed in 7.0 s                               |               address zero       |
|                                                                 | 5954 TX 1.81  general poll       |
|  2.  TESTS                                                      | 5985 RX 0.18                     |
|      [v PASS ]  the link comes up and the machine identifies    | 6126 TX 1.80  sync poll -        |
|                 itself                                          |               address zero       |
|                 6.9 s, 5 assert(s)                              | 6157 TX 1.01 0.1F               |
|                 tests/regression/wire/link-up.sas:1             |               long poll $1F      |
|      [o SKIP ]  10.1.1 standard validation configuration        | 6189 RX 0.01 0.1F 0.51 0.58      |
|                 egm.validation: this environment provides       |         0.30 0.30 0.31 0.02      |
|                 no 'egm.validation'                             |         0.FF 0.00 0.00 0.00      |
|                                                                 |         0.30 0.30 0.31 0.00      |
|  3.  ASSERTIONS                                                 |         0.00 0.00 0.39 0.32      |
|      -------------------------------------------------------   |         0.31 0.30 0.86 0.3F      |
|      Assertion                      Expected    Actual          | 6730 TX 1.80  sync poll -        |
|      -------------------------------------------------------   |               address zero       |
|      expect link up within 5s       Up          Up              | 6761 TX 1.01 0.54                |
|      send lp 1F                     a reply     30.7 ms         |               long poll $54      |
|      field game_id == QX            QX          QX              | 6791 RX 0.01 0.54 0.03 0.36      |
|      field base_percentage >= 7500  7500        9210            |         0.30 0.32 0.D0 0.8D      |
|      field sas_version of lp 54     602         602             |                                  |
|      -------------------------------------------------------   |                                  |
|                                                                 |                                  |
|  4.  DECODED REPLIES                                            |                                  |
+---------------------------------------------------------------+-----------------------------------+
| 34 sync · 34 general · 2 long · 1 exception · 0 silences       tcp · proven · stimulus none        |
+---------------------------------------------------------------------------------------------------+
```

Section 4 continued, scrolled:

```
|  4.  DECODED REPLIES                                            |
|      lp $1F  machine_id                              30.7 ms    |
|      -------------------------------------------------------   |
|      game_id             QX                                     |
|      additional_id       001                                    |
|      denomination        2                            code      |
|      max_bet             255                          credits   |
|      progressive_group   0                            index     |
|      game_options        0000                    little-endian  |
|      paytable_id         001                                    |
|      base_percentage     9210              hundredths of a %    |
|                                                                 |
|      lp $54  version_and_serial                                 |
|      -------------------------------------------------------   |
|      length              3                            count     |
|      sas_version         602                                    |
|      serial_number       [- UNKNOWN ]   length says 3 bytes     |
|                                         follow, all version     |
|                                                                 |
|  5.  METERS                                            17 read  |
|      -------------------------------------------------------   |
|      Meter                  From      Value      Unit           |
|      total_bill_value       $20        2000      credits        |
|      bills_20               $1E           1      count          |
|      games_played           $15          14      count          |
|      current_credits        $1A         340      credits        |
|      total_coin_in          $11        1250      credits        |
|                                                                 |
|  6.  EXCEPTIONS                                       2 queued  |
|      -------------------------------------------------------   |
|      $18  ac_power_lost        AC Power was lost from the       |
|           [! WARNING ]         gaming machine — queued before   |
|                                the host had asked for anything  |
|      $4A  bill_accepted_20     $20.00 bill accepted             |
|                                (non-RTE only)          6.2 s    |
|                                                                 |
|  7.  NOT DECODED                                                |
|      -------------------------------------------------------   |
|      $4A  [? UNDECODED ]  send total credit amount of all       |
|                           bills currently in the stacker        |
|                           this build has no layout for the      |
|                           reply; a test needing it is skipped   |
```

**900x600** — the margin tape drops below the body as a collapsible section 0 (`WIRE`, pinned
open by default), the masthead's definition list stacks to one column and loses 24 DIP, and the
body measure takes the full width.

```
+------------------------------------------------------------------------+
|  SasWorkbench                              [ light | dark ]   [ Print ] |
|  RUN RECORD                                                             |
|  ===================================================================    |
|  Link  tcp 127.0.0.1:9760 (listen)   ·  Address 01 · ch 1 · 200ms       |
|  Provenance  [ PROVEN ]    Stimulus  none    Caveat  none               |
+------------------------------------------------------------------------+
|  1.  VERDICT                                                            |
|      [ PASS ]   1 passed in 7.0 s                                       |
|                                                                         |
|  2.  TESTS                                                              |
|      [v PASS ]  the link comes up and the machine identifies itself     |
|                 6.9 s, 5 assert(s)  tests/regression/wire/link-up.sas:1 |
|      [o SKIP ]  10.1.1 standard validation configuration                |
|                 egm.validation: this environment provides no            |
|                 'egm.validation'                                        |
|                                                                         |
|  3.  ASSERTIONS                                                         |
|      ----------------------------------------------------------------  |
|      Assertion                       Expected     Actual                |
|      expect link up within 5s        Up           Up                    |
|      send lp 1F                      a reply      30.7 ms               |
|      field game_id == QX             QX           QX                    |
|      field base_percentage >= 7500   7500         9210                  |
|      field sas_version of lp 54      602          602                   |
|                                                                         |
|  v   WIRE                                                     34 lines  |
|      5927 TX 1.80  sync poll - address zero                             |
|      5954 TX 1.81  general poll                                         |
|      5985 RX 0.18                                                       |
+------------------------------------------------------------------------+
| 34 sync · 34 general · 2 long · 1 exc · 0 sil    tcp · proven · none    |
+------------------------------------------------------------------------+
```

**At 125 %** this is the direction that degrades most gracefully, because a single scrolling
column has no columns to lose — it simply becomes taller. It is also the direction that loses the
most: at 614 DIP of height the wire is below the fold, and a protocol engineer watching a live
link has to scroll to see it.

### 3.3 Tokens

| Token | Light | Dark |
|---|---|---|
| `Surface.Window` | `#E8E6E1` | `#1A1815` |
| `Surface.Panel` | `#FDFCFA` | `#22201B` |
| `Surface.Raised` | `#FFFFFF` | `#2A2721` |
| `Surface.Sunken` | `#F1EFEA` | `#141210` |
| `Stroke.Border` | `#BCB6A9` | `#433D33` |
| `Stroke.Divider` | `#E0DCD3` | `#2E2B24` |
| `Stroke.Control` | `#898275` | `#767168` |
| `Stroke.Focus` | `#1F3A5F` | `#9FC0E8` |
| `Text.Primary` | `#1A1A17` | `#F0EBE1` |
| `Text.Secondary` | `#4E4B43` | `#B7AF9F` |
| `Text.Disabled` | `#7C786F` | `#8A8375` |
| `Text.Data` | `#23231F` | `#EDE7DC` |
| `Accent` | `#1F3A5F` | `#9FC0E8` |
| `Accent.Ground` | `#DEE1E4` | `#343638` |
| `Text.OnAccent` | `#FDFCFA` | `#1A1815` |
| `Wire.Tx` | `#2F4C6D` | `#9FC0E8` |
| `Wire.TxGround` | `#E0E3E6` | `#343638` |
| `Wire.Rx` | `#1A1A17` | `#F0EBE1` |
| `Wire.Wakeup` | `#7A3E00` | `#E7C07A` |
| `Wire.WakeupGround` | `#EBE1D7` | `#3E3628` |
| `Status.Pass` | `#1D6034` | `#6FC08A` |
| `Status.PassGround` | `#DEE6DE` | `#2D362B` |
| `Status.Fail` | `#9E1C1C` | `#F08585` |
| `Status.FailGround` | `#F0DDDB` | `#3F2E2A` |
| `Text.OnFail` | `#FFFFFF` | `#000000` |
| `Status.Skip` | `#4A3E86` | `#B7A3EA` |
| `Status.SkipGround` | `#E4E1EA` | `#373238` |
| `Status.Undecoded` | `#55524A` | `#AEA595` |
| `Status.UndecodedGround` | `#E5E4E1` | `#36332C` |
| `Status.Unknown` | `#676767` | `#9E9E9E` |
| `Status.UnknownGround` | `#E8E7E5` | `#33322D` |
| `Status.Warning` | `#7E5207` | `#E0AE55` |
| `Status.WarningGround` | `#EBE4D8` | `#3D3423` |
| `Prov.Proven` | `#1D6034` | `#6FC08A` |
| `Prov.ProvenGround` | `#DEE6DE` | `#2D362B` |
| `Prov.Unverified` | `#7E5207` | `#E0AE55` |
| `Prov.UnverifiedGround` | `#EBE4D8` | `#3D3423` |

`Wire.Rx` **is** `Text.Primary` — full ink. In a document the machine's answer is the quoted
source; the host's own poll (`Wire.Tx`, ink-navy) is the citation around it. This is the same
idea as Proposal A's asymmetric TX tint, expressed the way a printed page would express it.

### 3.4 Contrast audit

| Pair | Light | Dark | Target |
|---|---|---|---|
| Text.Primary on Surface.Window | 13.98:1 | 14.91:1 | 4.5 |
| Text.Primary on Surface.Panel | 17.01:1 | 13.70:1 | 4.5 |
| Text.Primary on Surface.Raised | 17.44:1 | 12.53:1 | 4.5 |
| Text.Primary on Surface.Sunken | 15.18:1 | 15.73:1 | 4.5 |
| Text.Secondary on Surface.Panel | 8.49:1 | 7.47:1 | 4.5 |
| Text.Secondary on Surface.Raised | 8.71:1 | 6.84:1 | 4.5 |
| Text.Disabled on Surface.Panel | 4.29:1 | 4.33:1 | 3.0 |
| Text.Data on Surface.Panel | 15.38:1 | 13.22:1 | 4.5 |
| Text.Data on Surface.Sunken | 13.72:1 | 15.19:1 | 4.5 |
| Wire.Tx on Surface.Sunken | 7.69:1 | 9.95:1 | 4.5 |
| Wire.Tx on Wire.TxGround | 6.86:1 | 6.46:1 | 4.5 |
| Wire.Rx on Surface.Sunken | 15.18:1 | 15.73:1 | 4.5 |
| Wire.Wakeup on Surface.Sunken | 7.26:1 | 10.87:1 | 4.5 |
| Wire.Wakeup on Wire.WakeupGround | 6.47:1 | 6.93:1 | 4.5 |
| Status.Pass on Surface.Panel | 7.38:1 | 7.43:1 | 4.5 |
| Status.Pass on Status.PassGround | 5.94:1 | 5.72:1 | 4.5 |
| Status.Fail on Surface.Panel | 7.77:1 | 6.51:1 | 4.5 |
| Status.Fail on Status.FailGround | 6.09:1 | 5.14:1 | 4.5 |
| Text.OnFail on Status.Fail (solid chip) | 7.96:1 | 8.40:1 | 4.5 |
| Status.Skip on Surface.Panel | 8.81:1 | 7.32:1 | 4.5 |
| Status.Skip on Status.SkipGround | 6.99:1 | 5.63:1 | 4.5 |
| Status.Undecoded on Surface.Panel | 7.61:1 | 6.68:1 | 4.5 |
| Status.Undecoded on Status.UndecodedGround | 6.14:1 | 5.17:1 | 4.5 |
| Status.Unknown on Surface.Panel | 5.52:1 | 6.07:1 | 4.5 |
| Status.Unknown on Status.UnknownGround | 4.58:1 | 4.79:1 | 4.5 |
| Status.Warning on Surface.Panel | 6.61:1 | 8.02:1 | 4.5 |
| Status.Warning on Status.WarningGround | 5.37:1 | 6.04:1 | 4.5 |
| Prov.Proven on Prov.ProvenGround | 5.94:1 | 5.72:1 | 4.5 |
| Prov.Unverified on Prov.UnverifiedGround | 5.37:1 | 6.04:1 | 4.5 |
| Stroke.Control on Surface.Panel | 3.71:1 | 3.36:1 | 3.0 |
| Stroke.Control on Surface.Raised | 3.81:1 | 3.07:1 | 3.0 |
| Stroke.Focus on Surface.Panel | 11.20:1 | 8.66:1 | 3.0 |
| Accent on Surface.Panel | 11.20:1 | 8.66:1 | 3.0 |
| Text.OnAccent on Accent | 11.20:1 | 9.43:1 | 4.5 |

No pair fails, and this palette has good headroom — the lowest text pair is `Status.Unknown` on
its own ground at 4.58:1 and the next lowest is 5.14:1.

One thing this direction must *not* do: `Surface.Panel` is `#FDFCFA`, an off-white with a warm
cast. It is tempting to warm it further towards a paper cream; every step in that direction
lowers every ratio in the light column simultaneously, and at roughly `#F7F2E8` the
`Status.Unknown` pair breaks. The warmth budget is spent.

**Greyscale and colour vision:**

| | PASS:FAIL luminance | protanopia | deuteranopia |
|---|---|---|---|
| Light | 1.05:1 | `#605831` vs `#463F1A` = 1.48:1 | `#585237` vs `#665B15` = 1.15:1 |
| Dark | 1.14:1 | `#C0B487` vs `#9C9784` = 1.41:1 | `#B4AC8D` vs `#B6AC83` = **1.00:1** |

`1.00:1` in the dark variant is a perfect collision: PASS and FAIL are the *identical* colour to
a deuteranope. This direction is the one where that matters least, because the document form puts
the verdict in a numbered section headed `1. VERDICT` with the word spelled out at 15 DIP — the
chip is the least of the cues. It is still the reason FAIL keeps its solid fill even here, where
a solid block is the least document-like element on the page.

### 3.5 Typography

This is the only proposal with three families, and the serif is doing a job: it distinguishes the
document's own voice (masthead, section heads) from the machine's output (everything else). It is
never used for data.

| Role | Family | Size | Weight | Line height | Token |
|---|---|---|---|---|---|
| Document title (`RUN RECORD`) | Serif | 20 | 400, +0.5 tracking | 26 | `Text.Primary` |
| Product mark | Sans | 12 | 600 | 16 | `Text.Secondary` |
| Masthead label (`Link`, `Provenance`) | Sans | 11 | 600, uppercase, +0.6 | 15 | `Text.Secondary` |
| Masthead value | Mono | 12.5 | 500 | 18 | `Text.Primary` |
| Section head (`3. ASSERTIONS`) | Sans | 12 | 700, uppercase, +1.0 | 16 | `Text.Primary` |
| Table column head | Sans | 11 | 600, uppercase, +0.4 | 15 | `Text.Secondary` |
| Table cell, prose | Sans | 13 | 400 | 20 | `Text.Secondary` |
| Table cell, data | Mono | 12.5 | 400 | 20 | `Text.Data` |
| Table cell, value | Mono | 12.5 | 600 | 20 | `Text.Primary` |
| Wire tape | Mono | 11.5 | 400 tabular | 16 | `Text.Data` |
| Location | Mono | 11 | 400 | 15 | `Text.Secondary` |
| Chip label | Sans | 11 | 700, +0.6 | 14 | per status |

```
Serif : Georgia, Noto Serif, DejaVu Serif, Liberation Serif, serif
Sans  : Segoe UI, Inter, Noto Sans, DejaVu Sans, Liberation Sans, sans-serif
Mono  : Cascadia Mono, Consolas, DejaVu Sans Mono, Liberation Mono, monospace
```

Georgia ships with Windows; Noto Serif and DejaVu Serif are on essentially every desktop Linux.
If the embedded-font route is taken, **IBM Plex Serif** completes the Plex set under the same
OFL 1.1 licence.

Row height is 20 DIP here rather than 18, because a document's tables are read across rather than
scanned down, and because the assertion table's three columns need the extra leading to stay
legible at a 760 DIP measure. That costs about four visible rows per screen against Proposal A —
the one deliberate density concession in this document, and it is confined to the tables. The
wire tape stays at 16 DIP.

**Mono versus prose** follows the same rule as everywhere else, sharpened by the serif: serif is
the document's voice, sans is annotation, mono is the machine's own words, and mono is never
faded.

### 3.6 Component specimens

**The masthead is the provenance banner.** There is no separate banner component in this
direction — the provenance is a masthead field, in the same size and weight as the link. When the
link is unverified the masthead rule (the `=====` in the wireframe) changes from `Stroke.Border`
to a 3 DIP `Prov.Unverified` rule, the `Caveat` row fills with the caveat text, and the whole
masthead ground becomes `Prov.UnverifiedGround`:

```
+---------------------------------------------------------------------------------+
|  SasWorkbench                                            [ light | dark ]        |
|  RUN RECORD                                                                      |
|  =============================================================================== |  <- 3 DIP amber
|  Link       serial COM3                        Provenance   [ UNVERIFIED ]       |
|  Address    01 · channel 1 · poll every 200ms  Stimulus     none                 |
|  Started    2026-09-04 10:12:41                Caveat       no adapter has been  |
|                                                             measured; a pass     |
|                                                             here is not evidence |
|                                                             about hardware       |
+---------------------------------------------------------------------------------+
```

**Section head.** 12/700 uppercase with a number, a 1 DIP `Stroke.Divider` rule beneath spanning
the measure, and an optional right-aligned count in 11 `Text.Secondary` (`17 read`, `2 queued`).

**Ruled table.** 1 DIP `Stroke.Divider` above the header row and below it; no vertical rules; no
zebra striping — zebra is a screen affordance and reads as noise in a printed record. Row
separation comes from the 20 DIP leading. Numeric columns right-aligned and tabular.

**Assertion row** — this is the specimen that makes the direction's case, because it is the one
component that has no equivalent in the other three:

```
   Assertion                            Expected      Actual
   ---------------------------------------------------------------
   expect link up within 5s             Up            Up
   send lp 1F                           a reply       30.7 ms
   expect field game_id == QX           QX            QX
   expect field base_percentage >= 7500 7500          9210
   expect field sas_version of lp 54    602           602
```

Expected and Actual are both mono. When they differ, `Actual` takes `Status.Fail` and a FAIL chip
appears in a fourth column — the difference is the finding, so it is the only coloured thing in
the table.

**Wire tape row.** 16 DIP, three columns, wrapped at the tape width. Wakeup digit highlighted as
in Proposal A. TX carries no ground tint here — the tape is already narrow, and a tint at 300 DIP
wide reads as a block. Direction is carried by the `TX`/`RX` label in `Wire.Tx`/`Wire.Rx` and by
indentation of continuation lines.

**Chips** are as specified in section 0.3, with one adjustment: chip labels are **sans**, not
mono, because in a document the chip is a word rather than a value.

**Empty state** — the sections exist and say what they are waiting for, which is more useful than
a blank page and is the same discipline as reporting a SKIP:

```
   1.  VERDICT
       No run yet.

   2.  TESTS
       No run yet.  Set a script path and press Run.

   0.  WIRE
       No link.  loopback and tcp are proven; serial is unverified.
```

**Error state.** A parse error becomes a section of its own, numbered `0` and placed above the
verdict, because a suite that would not parse has no verdict:

```
   0.  COULD NOT RUN
       -------------------------------------------------------------
       tests/regression/meters/note-moves-bill-meters.sas:13
       [x FAIL ]  expected an exception name here
```

A refused transport renders in the masthead's `Caveat` row, in `Status.Fail`, with the refusal's
own words — which puts it exactly where a reader looking for "what was this run worth" is already
looking.

### 3.7 Motion and feedback

Least motion of the four, on principle: documents do not animate.

* **Animated:** section collapse (the wire tape at narrow widths), 120 ms on height. Nothing else.
* **Not animated:** chips do not cross-fade here; a verdict either is or is not. Scrolling is
  native. The masthead never transitions between proven and unverified — it re-renders, because a
  fade between two truth claims is exactly the wrong metaphor.
* **The 200 ms poll loop:** same 100 ms coalescing flush. Liveness is a single 8 DIP filled/hollow
  ring next to the `Link` masthead value plus the footer counters at 4 Hz. The tape does not
  auto-scroll by default in this direction — a document that moves while you read it is hostile —
  so `Follow` is an explicit toggle, off by default, and a `34 new` affordance appears at the
  bottom of the tape when it is off and lines have arrived.
* Print: `Ctrl+P` produces the HTML report via the existing reporting path, not a screen capture.
  The button is in the masthead because in this direction the whole window is a print preview.

### 3.8 Avalonia implementation notes

* **Controls.** `DockPanel` masthead/body/footer; a single `ScrollViewer` over an `ItemsControl`
  of sections for the body; `Grid` with `ColumnDefinitions="*,300"` for body and tape;
  `Expander` for the collapsible tape at narrow widths. The ruled tables want real columns and
  eventually sorting — `TreeDataGrid`, with its grid lines turned off and the rules drawn as
  `Border` bottom edges on the header, because `TreeDataGrid`'s own gridlines are heavier than
  this direction wants.
* **The measure cap** is `MaxWidth="760"` with `HorizontalAlignment="Left"` on the body's inner
  `StackPanel` — not a centred column. Left-aligned keeps the tape adjacent and stops the body
  drifting away from the wire on a wide monitor.
* **Serif in the theme.** Add a third `FontFamily` resource, and guard it: Avalonia falls back
  silently through a comma list, so a missing Georgia and a missing Noto Serif on a minimal
  container land on the generic `serif`, which on some Linux configurations is a sans. That is
  cosmetic, not functional, but it means the serif must never be the only thing distinguishing
  two pieces of information — it is paired with size and weight everywhere it appears.
* **`Print`** should call the existing `sasctl` reporting path in-process and open the resulting
  file, not attempt to render the window. Avalonia has no cross-platform print API worth building
  on, and D13 already produces the artefact.
* Same `Tokens.axaml` structure. The section head and rule:

```xml
<Style Selector="TextBlock.section">
  <Setter Property="FontFamily" Value="{DynamicResource FontSans}" />
  <Setter Property="FontSize" Value="12" />
  <Setter Property="FontWeight" Value="Bold" />
  <Setter Property="LetterSpacing" Value="1.0" />
  <Setter Property="Foreground" Value="{DynamicResource TextPrimary}" />
  <Setter Property="Margin" Value="0,20,0,4" />
</Style>

<Style Selector="Border.rule">
  <Setter Property="BorderThickness" Value="0,0,0,1" />
  <Setter Property="BorderBrush" Value="{DynamicResource StrokeDivider}" />
  <Setter Property="Margin" Value="0,0,0,8" />
</Style>

<!-- masthead: the whole band carries the strength of the claim -->
<Style Selector="Border.masthead">
  <Setter Property="Background" Value="{DynamicResource SurfacePanel}" />
  <Setter Property="BorderThickness" Value="0,0,0,2" />
  <Setter Property="BorderBrush" Value="{DynamicResource StrokeBorder}" />
  <Setter Property="Padding" Value="20,14,20,14" />
</Style>
<Style Selector="Border.masthead.unverified">
  <Setter Property="Background" Value="{DynamicResource ProvUnverifiedGround}" />
  <Setter Property="BorderThickness" Value="0,0,0,3" />
  <Setter Property="BorderBrush" Value="{DynamicResource ProvUnverified}" />
</Style>
```

---
## 4 · Proposal D — **ANNUNCIATOR**

### 4.1 Character

**A control-room panel.** Three annunciator tiles across the top state the only three things a
person walking up to the machine needs — *is the link up, what is this link worth, what did the
run say* — and below them a fixed grid of framed zones, each with an uppercase caption bar, a
count, and its own scrollbar. **Nothing is behind a tab and nothing is behind a drawer.** Zones
never move; you learn where a thing is once.

The colour discipline is the strongest of the four and it is a real idea rather than a palette:
**the three results own the chroma, the three non-results share a neutral family, and the chrome
is achromatic.** PASS green, FAIL red and WARNING amber are the only saturated things on the
panel besides the wire. SKIP, UNDECODED and UNKNOWN are slate, steel and grey — because none of
them is a result, and colouring them would imply they were.

* **Flatters most:** QA engineers. Everything is on one screen, the top band answers triage in
  one glance, and nothing requires knowing which tab the meters live in. The user is a close
  second — the wire is a full-height column.
* **Serves worst:** other QSP/libsas developers. Fixed zones mean no full-width wire, no
  side-by-side decode of two replies, and no room to open a sixth thing. Deep debugging fights
  the layout.

### 4.2 Layout

* **Annunciator band, 76 DIP, three equal tiles.** LINK, PROVENANCE, RUN. Each tile is a framed
  block with a caption, a large state line, and two supporting lines. These are the only large
  type in the application. The PROVENANCE tile's ground is `Prov.ProvenGround` or
  `Prov.UnverifiedGround`, so a third of the top of the window changes colour with the claim —
  the loudest treatment of D-principle 4 in any of these proposals.
* **Zone grid, three columns.** Left `WIRE` full height. Centre split vertically: `DECODE` over
  `METERS`. Right split vertically: `EXCEPTIONS` over `RUN STEPS`. Each zone has a caption bar
  (28 DIP: uppercase caption, right-aligned count, and at most one control), a 1 DIP
  `Stroke.Border` frame, and internal scroll. Zone proportions are fixed; only the two column
  boundaries are draggable.
* **Footer, 26 DIP.** Counters and the provenance triple.

M2–M4 surfaces become new zones, and this is the direction's structural weakness: a fixed grid of
six zones has no obvious room for a seventh. The answer is that a zone gains a **segment control**
in its caption bar (`DECODE | CATALOGUE`, `METERS | BASELINE`, `EXCEPTIONS | TICKETS`) rather than
the panel gaining a column — which is a tab by another name, admitted honestly.

**1180x760**

```
+--------------------------------------------------------------------------------------------------+
| LINK                          | PROVENANCE                    | RUN                               |
|                               |                               |                                   |
|  (*) UP                       |  [ PROVEN ]                   |  [ PASS ]  1 passed in 7.0 s      |
|  tcp 127.0.0.1:9760 (listen)  |  stimulus none                |  1 pass · 0 fail · 1 skip         |
|  addr 01 · ch 1 · every 200ms |  no caveat                    |  tests/regression   [ RUN ]       |
+-------------------------------+-------------------------------+-----------------------------------+
| WIRE                     34   | DECODE   lp $1F     30.7 ms   | EXCEPTIONS                    2   |
|-------------------------------|-------------------------------|-----------------------------------|
| 5927 TX 1.80 sync poll -      | game_id            QX         | $18  ac_power_lost                |
|              address zero     | additional_id      001        |      [! WARNING ]  queued before  |
| 5954 TX 1.81 general poll     | denomination       2    code  |      the host had asked for       |
| 5985 RX 0.18                  | max_bet          255 credits  |      anything                     |
| 6126 TX 1.80 sync poll -      | progressive_group    0 index  | $4A  bill_accepted_20      6.2 s  |
|              address zero     | game_options      0000        |      $20.00 bill accepted         |
| 6157 TX 1.01 0.1F             |              little-endian    |      (non-RTE only)               |
|              long poll $1F    | paytable_id        001        |-----------------------------------|
| 6189 RX 0.01 0.1F 0.51 0.58   | base_percentage   9210        | RUN STEPS                     2   |
|         0.30 0.30 0.31 0.02   |         hundredths of a %     |-----------------------------------|
|         0.FF 0.00 0.00 0.00   |-------------------------------| [v PASS ] the link comes up and   |
|         0.30 0.30 0.31 0.00   | METERS        17 read [READ]  |           the machine identifies  |
|         0.00 0.00 0.39 0.32   |-------------------------------|           itself                  |
|         0.31 0.30 0.86 0.3F   | total_bill_value $20    2000  |           6.9s, 5 assert(s)       |
| 6730 TX 1.80 sync poll -      | bills_20         $1E       1  |           .../wire/link-up.sas:1  |
|              address zero     | games_played     $15      14  | [o SKIP ] 10.1.1 standard         |
| 6761 TX 1.01 0.54             | current_credits  $1A     340  |           validation              |
|              long poll $54    | total_coin_in    $11    1250  |           configuration           |
| 6791 RX 0.01 0.54 0.03 0.36   | total_coin_out   $12     900  |           egm.validation: this    |
|         0.30 0.32 0.D0 0.8D   | games_won        $16       6  |           environment provides    |
|                               | games_lost       $17       8  |           no 'egm.validation'     |
+-------------------------------+-------------------------------+-----------------------------------+
| 34 sync · 34 general · 2 long · 1 exception · 0 silences       tcp · proven · stimulus none       |
+--------------------------------------------------------------------------------------------------+
```

**900x600** — two columns. The right column's two zones move under the centre column's two, so
the order top-to-bottom becomes DECODE, METERS, EXCEPTIONS, RUN STEPS in one scrolling stack
beside a full-height WIRE. The annunciator band drops to 64 DIP by losing its third supporting
line. Six zones do not fit at 614 DIP, which is this direction's real cost: the second column
scrolls, so "everything visible at once" — the whole premise — is only true on the larger window.

```
+------------------------------------------------------------------------+
| LINK                    | PROVENANCE          | RUN                     |
|  (*) UP                 |  [ PROVEN ]         |  [ PASS ] 1 in 7.0 s    |
|  tcp ...:9760 · 01 · c1 |  stimulus none      |  1 pass 0 fail 1 skip   |
+-------------------------+---------------------+-------------------------+
| WIRE               34   | DECODE  lp $1F          30.7 ms               |
|-------------------------|-----------------------------------------------|
| 5927 TX 1.80 sync poll -| game_id            QX                         |
|         address zero    | additional_id      001                        |
| 5954 TX 1.81 general    | denomination       2                   code   |
| 5985 RX 0.18            | max_bet            255              credits   |
| 6126 TX 1.80 sync poll -| game_options       0000        little-endian  |
|         address zero    | base_percentage    9210   hundredths of a %   |
| 6157 TX 1.01 0.1F       |-----------------------------------------------|
|         long poll $1F   | METERS                  17 read      [ READ ] |
| 6189 RX 0.01 0.1F 0.51  |-----------------------------------------------|
|      0.58 0.30 0.30     | total_bill_value   $20         2000  credits  |
|      0.31 0.02 0.FF     | bills_20           $1E            1  count    |
|      0.00 0.00 0.00     | games_played       $15           14  count    |
|      0.30 0.30 0.31     |-----------------------------------------------|
|      0.00 0.00 0.00     | EXCEPTIONS                                2   |
|      0.39 0.32 0.31     |-----------------------------------------------|
|      0.30 0.86 0.3F     | $18 ac_power_lost  [! WARNING ] queued before |
| 6730 TX 1.80 sync poll -|     the host had asked for anything           |
+-------------------------+-----------------------------------------------+
| 34s 34g 2l 1e 0sil                    tcp · proven · stimulus none      |
+------------------------------------------------------------------------+
```

**At 125 %** the annunciator band is the thing that pays: 76 DIP of the 614 available is 12 % of
the window spent on three tiles. That is defensible only if the three tiles are genuinely the
three questions — which is the bet this direction is making.

### 4.3 Tokens

| Token | Light | Dark |
|---|---|---|
| `Surface.Window` | `#DDE3E8` | `#101418` |
| `Surface.Panel` | `#FFFFFF` | `#171C22` |
| `Surface.Raised` | `#F4F7F9` | `#202730` |
| `Surface.Sunken` | `#CFD7DE` | `#0B0E11` |
| `Stroke.Border` | `#9EAAB4` | `#343D47` |
| `Stroke.Divider` | `#D5DDE3` | `#232A31` |
| `Stroke.Control` | `#75818B` | `#68727C` |
| `Stroke.Focus` | `#0F161B` | `#FFFFFF` |
| `Text.Primary` | `#0F161B` | `#E9EEF3` |
| `Text.Secondary` | `#42505A` | `#AEBAC4` |
| `Text.Disabled` | `#6C7982` | `#7C8892` |
| `Text.Data` | `#101A21` | `#DFE9F1` |
| `Accent` | `#26323B` | `#CBD6DF` |
| `Accent.Ground` | `#E1E2E4` | `#30363C` |
| `Text.OnAccent` | `#FFFFFF` | `#101418` |
| `Wire.Tx` | `#8A4B00` | `#FFB454` |
| `Wire.TxGround` | `#EFE6DB` | `#373129` |
| `Wire.Rx` | `#0B5480` | `#62B8F0` |
| `Wire.Wakeup` | `#6E4600` | `#FFE08A` |
| `Wire.WakeupGround` | `#EBE5DB` | `#373731` |
| `Status.Pass` | `#17683F` | `#55C97A` |
| `Status.PassGround` | `#DFEAE4` | `#20342E` |
| `Status.Fail` | `#B21D14` | `#FF6B62` |
| `Status.FailGround` | `#F4DFDE` | `#37272B` |
| `Text.OnFail` | `#FFFFFF` | `#000000` |
| `Status.Skip` | `#3F4E5A` | `#A9B8C6` |
| `Status.SkipGround` | `#E4E6E8` | `#2B3239` |
| `Status.Undecoded` | `#4A5A66` | `#8FA0B0` |
| `Status.UndecodedGround` | `#E6E8EA` | `#282E36` |
| `Status.Unknown` | `#696969` | `#9A9A9A` |
| `Status.UnknownGround` | `#EAEAEA` | `#292E33` |
| `Status.Warning` | `#7E5209` | `#FFC44D` |
| `Status.WarningGround` | `#EDE7DD` | `#373428` |
| `Prov.Proven` | `#17683F` | `#55C97A` |
| `Prov.ProvenGround` | `#DFEAE4` | `#20342E` |
| `Prov.Unverified` | `#7E5209` | `#FFC44D` |
| `Prov.UnverifiedGround` | `#EDE7DD` | `#373428` |

`Accent` is achromatic — graphite in light, bone in dark — for the same reason as Proposal A and
for one more: `Wire.Rx` is blue here, and a blue accent would put the same hue on a button and on
the machine's reply. On a control panel the switches are metal and only the lamps are coloured.

### 4.4 Contrast audit

| Pair | Light | Dark | Target |
|---|---|---|---|
| Text.Primary on Surface.Window | 14.10:1 | 15.85:1 | 4.5 |
| Text.Primary on Surface.Panel | 18.25:1 | 14.68:1 | 4.5 |
| Text.Primary on Surface.Raised | 16.96:1 | 12.90:1 | 4.5 |
| Text.Primary on Surface.Sunken | 12.54:1 | 16.58:1 | 4.5 |
| Text.Secondary on Surface.Panel | 8.31:1 | 8.67:1 | 4.5 |
| Text.Secondary on Surface.Raised | 7.72:1 | 7.62:1 | 4.5 |
| Text.Disabled on Surface.Panel | 4.47:1 | 4.73:1 | 3.0 |
| Text.Data on Surface.Panel | 17.62:1 | 13.92:1 | 4.5 |
| Text.Data on Surface.Sunken | 12.11:1 | 15.72:1 | 4.5 |
| Wire.Tx on Surface.Sunken | 4.67:1 | 10.97:1 | 4.5 |
| Wire.Tx on Wire.TxGround | 5.51:1 | 7.29:1 | 4.5 |
| Wire.Rx on Surface.Sunken | 5.56:1 | 8.88:1 | 4.5 |
| Wire.Wakeup on Surface.Sunken | 5.68:1 | 15.00:1 | 4.5 |
| Wire.Wakeup on Wire.WakeupGround | 6.60:1 | 9.29:1 | 4.5 |
| Status.Pass on Surface.Panel | 6.80:1 | 8.18:1 | 4.5 |
| Status.Pass on Status.PassGround | 5.51:1 | 6.29:1 | 4.5 |
| Status.Fail on Surface.Panel | 6.82:1 | 6.14:1 | 4.5 |
| Status.Fail on Status.FailGround | 5.35:1 | 5.06:1 | 4.5 |
| Text.OnFail on Status.Fail (solid chip) | 6.82:1 | 7.53:1 | 4.5 |
| Status.Skip on Surface.Panel | 8.58:1 | 8.45:1 | 4.5 |
| Status.Skip on Status.SkipGround | 6.85:1 | 6.40:1 | 4.5 |
| Status.Undecoded on Surface.Panel | 7.13:1 | 6.38:1 | 4.5 |
| Status.Undecoded on Status.UndecodedGround | 5.80:1 | 5.10:1 | 4.5 |
| Status.Unknown on Surface.Panel | 5.49:1 | 6.09:1 | 4.5 |
| Status.Unknown on Status.UnknownGround | 4.56:1 | 4.87:1 | 4.5 |
| Status.Warning on Surface.Panel | 6.78:1 | 10.81:1 | 4.5 |
| Status.Warning on Status.WarningGround | 5.51:1 | 7.87:1 | 4.5 |
| Prov.Proven on Prov.ProvenGround | 5.51:1 | 6.29:1 | 4.5 |
| Prov.Unverified on Prov.UnverifiedGround | 5.51:1 | 7.87:1 | 4.5 |
| Stroke.Control on Surface.Panel | 3.99:1 | 3.50:1 | 3.0 |
| Stroke.Control on Surface.Raised | 3.70:1 | 3.07:1 | 3.0 |
| Stroke.Focus on Surface.Panel | 18.25:1 | 17.13:1 | 3.0 |
| Stroke.Focus on Surface.Sunken | 12.54:1 | 19.35:1 | 3.0 |
| Accent on Surface.Panel | 13.11:1 | 11.61:1 | 3.0 |
| Accent on Surface.Raised | 12.19:1 | 10.20:1 | 3.0 |
| Text.OnAccent on Accent | 13.11:1 | 12.53:1 | 4.5 |

No pair fails. The tightest is `Wire.Tx` on `Surface.Sunken` in the light theme at **4.67:1** —
`Surface.Sunken` is `#CFD7DE`, a genuinely recessed grey rather than a barely-tinted white, and
that darkness is what makes the wire well read as a well. It clears 4.5 with 0.17 to spare, which
means `Surface.Sunken` cannot be darkened further without moving `Wire.Tx` with it. Noted so that
a later "let's make the well a bit deeper" does not quietly break it.

**Greyscale and colour vision:**

| | PASS:FAIL luminance | protanopia | deuteranopia |
|---|---|---|---|
| Light | **1.00:1** | `#675F3C` vs `#504610` = 1.47:1 | `#5E5842` vs `#736607` = 1.23:1 |
| Dark | **1.33:1** | `#CABA75` vs `#918760` = 1.85:1 | `#BAAF7F` vs `#B6A75E` = 1.10:1 |

The dark variant has the **best** PASS/FAIL greyscale separation in this document (1.33:1) and
the best protanopia separation (1.85:1); the light variant has the worst luminance separation
(1.00:1 — identical brightness). Neither number is good enough to rely on, which is why the
non-colour channels are mandatory, but it is worth knowing that this direction's dark theme is
the most robust of the eight variants and its light theme the least.

The three-neutral / three-chroma split helps here in a way the ratios do not show: under either
simulation, PASS/FAIL/WARNING all become yellow-browns of similar hue, while SKIP, UNDECODED and
UNKNOWN stay unmistakably grey. So *"is this a result at all"* — which is the question
D-principle 1 is really about — survives colour-blindness intact even where *"which result"* does
not.

### 4.5 Typography

The only proposal with genuinely large type, confined to the three annunciator tiles.

| Role | Family | Size | Weight | Line height | Token |
|---|---|---|---|---|---|
| Tile caption (`LINK`) | Sans | 11 | 700, uppercase, +1.2 tracking | 14 | `Text.Secondary` |
| Tile state line | Sans | 17 | 600 | 22 | `Text.Primary` |
| Tile supporting line | Mono | 12 | 400 | 16 | `Text.Secondary` |
| Zone caption bar | Sans | 11 | 700, uppercase, +1.0 tracking | 14 | `Text.Secondary` |
| Zone count | Mono | 11 | 600 tabular | 14 | `Text.Secondary` |
| Prose, notes, meanings | Sans | 12.5 | 400 | 17 | `Text.Secondary` |
| Field and meter names | Mono | 12.5 | 400 | 17 | `Text.Data` |
| Field and meter values | Mono | 12.5 | 600 tabular | 17 | `Text.Primary` |
| Wire hex | Mono | 12.5 | 400 | 17 | `Text.Data` |
| Wire timestamp | Mono | 11.5 | 400 tabular | 17 | `Text.Secondary` |
| Chip label | Sans | 11 | 700, +0.8 tracking | 14 | per status |

Same Sans and Mono stacks as Proposal A. Row height is 17 DIP — the tightest of the four — which
is what buys the six-zone grid its content. At 17 DIP with a 12.5 DIP mono the descender clearance
is 1.5 DIP; that is tight but correct for DejaVu Sans Mono and Consolas, and it is the reason this
direction should not adopt a font with a taller x-height without re-measuring.

**Mono versus prose:** as everywhere. The one addition specific to D is that the *tile state
lines* are sans at 17/600 — the only place in the application where prose outranks data, and
justified because "UP", "PROVEN" and "PASS" are words, not measurements.

### 4.6 Component specimens

**Annunciator tile.** 76 DIP tall, 1 DIP `Stroke.Border` frame, `Surface.Panel` ground.

```
+-------------------------------+   +-------------------------------+
| LINK                          |   | PROVENANCE                    |
|                               |   |                               |
|  (*) UP                       |   |  [ PROVEN ]                   |
|  tcp 127.0.0.1:9760 (listen)  |   |  stimulus none                |
|  addr 01 · ch 1 · every 200ms |   |  no caveat                    |
+-------------------------------+   +-------------------------------+
   ground Surface.Panel                ground Prov.ProvenGround
   lamp: 10 DIP ring, filled =         3 DIP left rule Prov.Proven
   up, hollow = down, Status.Pass
```

Unverified — the tile's ground and rule change and the caveat replaces the third line:

```
+-------------------------------+
| PROVENANCE                    |
|                               |
|  [ UNVERIFIED ]               |
|  stimulus none                |
|  no adapter has been measured |
+-------------------------------+
   ground Prov.UnverifiedGround, 3 DIP left rule Prov.Unverified.
   The full caveat is the tile's ToolTip and its AutomationProperties.HelpText.
```

**Zone caption bar.** 28 DIP, `Surface.Raised` ground, 1 DIP `Stroke.Divider` beneath, caption
left, count right, at most one control (`READ`, `CLEAR`, `RUN`) as a 24 DIP outline button.

**The lamp.** 10 DIP `Ellipse`. Filled = the state is asserted; hollow = it is not. Used for the
link only. **There is no blinking lamp anywhere in this application.**

**SKIP as an unlit lamp.** The metaphor that makes this direction coherent: a SKIP is a lamp that
was never lit, so it is drawn at full contrast in `Status.Skip` slate with a hollow ring glyph and
a solid outline — prominent, unmistakably *not* a result, and unmistakably not a failure. The
three non-results differ from each other by outline weight: SKIP solid, UNDECODED dashed, UNKNOWN
none.

```
   [o SKIP ]        solid outline  #3F4E5A  ground #E4E6E8
   [? UNDECODED ]   dashed outline #4A5A66  no ground
   [- UNKNOWN ]     no outline              ground #EAEAEA
```

**Wire, decoded-field, meter and run-step rows** are as Proposal A at 17 DIP instead of 18, with
D's palette. The one difference: because `Surface.Sunken` here is a real recessed grey, the wire
well reads as a well without a border, so the WIRE zone drops its inner frame and keeps only the
caption bar rule.

**Empty state.** Every zone states what it is waiting for, in its own frame — the panel is never
blank, because a blank annunciator is indistinguishable from a broken one:

```
| WIRE                      0   |   | DECODE                        |
|-------------------------------|   |-------------------------------|
|                               |   |                               |
|   No link.                    |   |   No reply yet.               |
|   loopback and tcp are        |   |   Pick a command and press    |
|   proven; serial is           |   |   SEND.                       |
|   unverified.                 |   |                               |
```

**Error state.** The offending zone's caption bar takes a `Status.Fail` ground and its
`Text.OnFail` label, and the message is the zone's first row — so the failure is located, not
announced globally:

```
| WIRE                  FAILED  |     <- caption bar: Status.Fail ground
|-------------------------------|
| [x] '0G' is not a poll        |
|     address. Expected 00      |
|     to 7F.                    |
```

A refused transport uses the same treatment on the LINK tile, and the tile's state line becomes
`REFUSED` rather than `DOWN` — because refusing is not the same as being down and D-principle 2
says the refusal must not look like a failure to connect.

### 4.7 Motion and feedback

A control panel that moves is a control panel nobody watches.

* **Animated:** essentially nothing. The only transition is the 90 ms chip and caption-bar colour
  cross-fade when a zone changes state, and the stock indeterminate `ProgressBar` at 2 DIP inside
  a zone's caption bar while that zone is busy.
* **Explicitly forbidden:** blinking lamps, pulsing borders, sliding zones, animated counters,
  and any "activity" indicator tied to the poll loop. The gaming-floor convention of a flashing
  annunciator exists to summon a human across a room; here it would flash five times a second
  forever.
* **The 200 ms poll loop:** the same 100 ms coalescing flush. Liveness is the LINK tile's filled
  lamp plus the footer counters at 4 Hz. The wire zone's count in its caption bar updates on the
  same flush, so the number is the heartbeat.
* Zone scroll position is never stolen. If the user has scrolled the WIRE zone up, Follow
  disengages and the caption bar shows `34 new` until they scroll back to the end — the same
  affordance as Proposal C's tape, and for the same reason.

### 4.8 Avalonia implementation notes

* **Controls.** One outer `Grid` with `RowDefinitions="76,*,26"` and an inner
  `Grid ColumnDefinitions="*,4,*,4,*"` with two `GridSplitter`s. Each zone is a `Border` with a
  `DockPanel` inside: caption bar docked top, `ListBox` filling. No `TabControl`, no `Expander`,
  no `SplitView` — this is the simplest of the four to build and the simplest to get wrong later,
  because adding a seventh zone means re-cutting the grid.
* **The responsive step is a real reflow, not a style.** Going from three columns to two moves
  two zones into a different parent, which XAML alone cannot express. Implement it as two
  `Grid`s in the same cell, toggled on `Bounds.Width` — either with a small attached behaviour or
  with two `IsVisible` bindings against a `IsNarrow` view-model property fed by a `SizeChanged`
  handler. Do not attempt it with `Grid.Row`/`Grid.Column` setters in a style; Avalonia will
  apply them but the two layouts will fight over `RowDefinitions`.
* **Segment controls in caption bars** (the M2–M4 growth path) are a `ListBox` with
  `ItemsPanel` set to a horizontal `StackPanel` and a flat `ListBoxItem` theme — not a
  `TabControl`, so the zone body can stay a single `ContentControl` bound to the selection.
* The `Ellipse` lamp:

```xml
<Style Selector="Ellipse.lamp">
  <Setter Property="Width" Value="10" />
  <Setter Property="Height" Value="10" />
  <Setter Property="StrokeThickness" Value="1.5" />
  <Setter Property="Stroke" Value="{DynamicResource StrokeControl}" />
  <Setter Property="Fill" Value="Transparent" />
</Style>
<Style Selector="Ellipse.lamp.on">
  <Setter Property="Stroke" Value="{DynamicResource StatusPass}" />
  <Setter Property="Fill" Value="{DynamicResource StatusPass}" />
</Style>
```

* The zone caption bar, including its error state:

```xml
<Style Selector="Border.zonecap">
  <Setter Property="Height" Value="28" />
  <Setter Property="Background" Value="{DynamicResource SurfaceRaised}" />
  <Setter Property="BorderThickness" Value="0,0,0,1" />
  <Setter Property="BorderBrush" Value="{DynamicResource StrokeDivider}" />
  <Setter Property="Padding" Value="8,0,6,0" />
</Style>
<Style Selector="Border.zonecap.failed">
  <Setter Property="Background" Value="{DynamicResource StatusFail}" />
  <Setter Property="BorderBrush" Value="{DynamicResource StatusFail}" />
</Style>
<Style Selector="Border.zonecap.failed TextBlock">
  <Setter Property="Foreground" Value="{DynamicResource TextOnFail}" />
</Style>
```

* Same `Tokens.axaml` shape, same `Border.chip` styles, same `SelectableTextBlock` requirement,
  same `WireLine`/`WireSymbol` view-model change as Proposal A. None of those are
  direction-specific — they are the shared cost of doing this properly once.

---
## 5 · Comparison matrix

Scores are 1–5, 5 best. They are not averaged into a total, because the criteria are not of equal
weight for this product and pretending otherwise would hide the argument.

| | **A · BENCH** | **B · CONSOLE** | **C · DOSSIER** | **D · ANNUNCIATOR** |
|---|---|---|---|---|
| **Fit: the user** (protocol engineer, reads hex) | **5** — trace well is the largest, brightest object and is never covered; decode sits beside it | 4 — full debugger ergonomics and a real inspector, but the wire competes with a tab strip and a rail | 2 — the wire is a 300 DIP margin tape and at 614 DIP it is below the fold | 4 — full-height wire column, but no room to widen it and no second decode |
| **Fit: QSP/libsas developers** | 3 — drawers are good for reading, poor for comparing two things at once | **5** — closable panels, splittable documents, selection-driven inspector, command entry | 2 — a document is the wrong shape for chasing a byte | 2 — fixed zones; no side-by-side, no seventh surface without a tab |
| **Fit: QA engineers** (triage red, not protocol experts) | 4 — collapsed drawers carry summaries, so triage is one glance down the stack | 3 — powerful but requires knowing where things live; panel state is per-user and drifts | 4 — numbered sections *are* a triage order; VERDICT is section 1 | **5** — three tiles answer triage before you read anything; nothing hidden |
| **Fit: customers, integrators, regulators** | 3 — honest and sober, but reads as internal tooling | 2 — reads as somebody's IDE; provenance demoted to an 18 DIP chip | **5** — screen and report share a layout; provenance is masthead, above every result | 4 — the provenance tile is a third of the top band; unmistakable |
| **Information density** (data rows visible at ~944x610) | 4 — 26 wire rows + ~22 in one open drawer; 18 DIP rows | 3 — 22 wire rows + 3 step rows; 202 DIP of chrome is the most of the four | 2 — 24 rows at 20 DIP leading, single column, wire below the fold | **5** — 30 wire rows at 17 DIP plus ~28 in the second column; 90 DIP of chrome |
| **Screenshot in a report** | 3 | 2 | **5** — the screenshot *is* the report; nothing needs cropping or explaining | 4 — the top band crops well and carries link, provenance and verdict together |
| **Accessibility: contrast headroom** | 4 — lowest 4.5-target pair 4.56:1; one pair under 5.0 | 2 — lowest 4.56:1 with **eight** pairs under 5.0; a single tweak breaks it | **5** — lowest 4.58:1, only two pairs under 5.0, rest above 5.1 | 4 — lowest 4.56:1, three pairs under 5.0; `Wire.Tx` on `Surface.Sunken` at 4.67 caps how deep the well can go |
| **Accessibility: PASS/FAIL without colour** | **5** — best worst-case greyscale (1.23:1 dark, 1.45:1 light) | 2 — 1.02:1 dark; leans hardest on the fill/word mitigation | 3 — 1.00:1 dark, but the verdict is a spelled-out section head, so the chip is the least of the cues | 4 — best dark (1.33:1) and best protanopia (1.85:1), worst light (1.00:1); the neutral/chroma split preserves "is this a result at all" under both simulations |
| **Dark-mode quality** | **5** — `#070909` well with amber TX and cyan RX; the direction dark was designed for | 3 — competent and generic; the accent/TX/focus hue collision is worst here | 2 — a warm dark ledger is an odd artefact, and RX-as-full-ink makes the machine's reply the brightest thing on a dark page | 4 — high-contrast panel with real lamp colour; second only to A |
| **Effort to build (M1.5)** | 3 — four bands, `Expander` control theme, splitter | **1** — rail, two splitters, inspector wiring, dock tabs, command entry; most surface area by far | 3 — masthead states, serif, measure cap, tape; plus the report-parity discipline | **5** — one grid, six borders, no disclosure widgets at all |
| **Effort to maintain through M2–M4** | 4 — a new surface is a new drawer with a summary line; collapsed drawers cost no chrome | **5** — a new surface is a rail entry and a dock tab; the growth story is the whole point | 3 — a new surface is a numbered section, which is cheap, but live/interactive surfaces (manual stimulus prompt, two live wires) fit a document badly | **1** — a fixed six-zone grid has no seventh cell; M2 forces segment controls into caption bars, which is a tab readmitted through the back door |

Three things the matrix makes visible that are easy to miss:

* **B and D are opposites and both are extreme.** D is the cheapest to build and the most
  expensive to keep; B is the reverse. Neither is a comfortable place to be for a product whose
  next three milestones each add panels.
* **C wins the audience the product is most likely to be judged by** and loses the audience that
  uses it every day. That is a real trade, not a scoring artefact.
* **No proposal is both dense and document-like.** Density and report-quality are genuinely
  opposed here (D scores 5/4, C scores 2/5), and any recommendation has to pick which one is the
  default and which one is a mode.

---

## 6 · Recommendation

### Build **A · BENCH**, with two named grafts.

The two criteria that should decide this are not "which looks best". They are:

1. **D-principle 4 is a UI requirement**, and it is currently one line of 11 px grey text. Whatever
   is built must make the strength of the claim impossible to miss and impossible to crop away.
2. **M1.5 exists so that M2, M3 and M4 do not each trigger a restructure.** The milestone's own
   text says so. A direction that is cheap now and expensive at M2 has failed at its stated job.

Against those two, and against the four audiences:

* **A** gives provenance a full-width rail that changes colour with the claim — second only to C
  — while keeping the wire at full height for the person who uses the tool daily. Its drawer
  stack is a genuinely good growth mechanism: a new surface is a new `Expander` with a summary
  line, and a collapsed drawer costs 28 DIP of chrome and nothing else. It has the best
  worst-case greyscale separation of the four and the best dark theme. It is mid-effort to build
  and second-cheapest to maintain.
* **D** is tempting — it is the cheapest to build, the densest, and the best for QA — but its
  fixed grid fails criterion 2 outright. The honest note in its own section says it: M2 forces
  segment controls into caption bars, which is a `TabControl` with a different name, and the
  milestone would have bought nothing.
* **B** wins criterion 2 and loses criterion 1. Reducing the provenance to an 18 DIP chip in a
  crowded command bar is exactly the demotion this milestone exists to reverse, and it has the
  least contrast headroom and the worst dark greyscale of the four. It is also the largest build,
  which is the wrong place to spend M1.5 given that M2–M4 have real protocol work waiting.
* **C** is the best answer to a question that is not the primary one. It should not be discarded —
  which is the first graft.

**Graft 1, from C: a Report view as a mode, not as the application.** The document layout —
masthead, numbered sections, ruled assertion table — becomes a full-window mode toggled from the
provenance rail, rendered from the same `RunReport` the HTML report is rendered from. That gives
customers and regulators the screenshot C was designed for without giving up the engineer's wire,
and it forces screen/report parity to be structural rather than a discipline somebody has to
remember. It is also the cheapest possible version of C, because the data is already shaped for
it by `SasWorkbench.Core.Reporting`.

**Graft 2, from B: selection drives the decode.** Selecting an RX row in the trace well fills the
DECODE drawer with that reply's fields; selecting a TX row shows the encoded request. This is the
single best interaction idea in this document, it costs one `SelectedItem` binding plus a call
into the existing decoder, and it is what makes the M4 trace-replay and catalogue-diff views cheap
later — they become more things the decode drawer can be pointed at.

**Explicitly not taken:** B's activity rail and dockable panels (cost without a matching benefit
at four surfaces), D's fixed zone grid (fails at M2), C's serif and warm paper as the primary
skin (they belong to the Report mode, where the serif is doing a job).

### What I would build first

In this order. Each step is independently shippable and none of them blocks on the one after.

1. **`Themes/Tokens.axaml` with Proposal A's `ThemeDictionaries`, and delete the four
   opacity-based styles from `App.axaml`.** Nothing else changes; every existing view keeps
   working. This is the change that makes all the others possible, and it is the change that
   removes the seven-level opacity fog. Verify by switching `RequestedThemeVariant` between Light
   and Dark and confirming no view falls back to a FluentTheme brush.
2. **The status chip, all six states, all three channels** (word, fill treatment, `Path` glyph),
   plus `Text.OnFail`. Ship it into the existing Run tab first, because PASS and SKIP already
   render there as bare bold text and the improvement is immediately visible and immediately
   testable against the committed `10.1.1` skip.
3. **The provenance rail.** Replace the 11 px grey `TextBlock` with the full-width band: seal,
   description, stimulus, caveat, lamp, and the ground that changes with `TransportCapabilities.
   Fidelity`. Include the caveat-expansion behaviour, and wire the `AutomationProperties` live
   region. This is the load-bearing requirement and it is currently the weakest thing in the
   window.
4. **`WireLine` gains `IsTransmit` and `IReadOnlyList<WireSymbol>`, and the wire feed coalesces
   on a 100 ms `DispatcherTimer`.** One view-model change, one threading change. It unblocks the
   per-symbol wakeup highlight — the thing that makes the trace well worth looking at — and it
   removes five dispatcher posts a second.
5. **The four bands and the drawer stack**, replacing the `Grid`/`TabControl` in
   `MainWindow.axaml`. By this point the tokens, the chips and the rows already exist, so this
   step is pure layout and is the one that should be reviewed side by side against the current
   window before anything else is added.
6. **Then, and only then:** the Report mode (graft 1) and selection-driven decode (graft 2).

**Not first, deliberately:** `TreeDataGrid` (the meter and field lists do not need sorting until
M4 makes them long), embedded fonts (the system stacks are correct; embedding is a
reproducibility improvement, not a correctness one), and any M2/M3 panel — those are slots in
this contract and they should be designed against a milestone that has actually run.

### The one thing to watch

Every palette in this document fails to separate PASS from FAIL under deuteranopia — 1.00:1 to
1.19:1 across the eight variants. The mitigation is real and it is specified, but it only works if
it is never quietly dropped: **the word, the FAIL-only solid fill, and the vector glyph are
requirements, not decoration.** If a later change makes the chips icon-only to save space, or
makes FAIL outlined for visual consistency with the others, the application stops meeting its own
accessibility contract and the contrast table above becomes a document that says something untrue.
