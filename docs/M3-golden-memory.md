# M3 — The golden memory image, and the poll that proves it took

**Status: a conformance case runs against a real gaming machine.** `10.1.1 standard validation
configuration` passes over the wire against `QSP_DemoOpenGL`, because the machine's memory now
carries a configured printer and a validation style, and because the reply that publishes that
style can now be decoded. Both halves were needed: an image nothing could read back would have
been a claim, and a decoder with no configured machine to point at would have had nothing to read.

`BLOCKERS.md §4` had named the memory image as a prerequisite since before M0.

---

## What a machine keeps in its memory, and why none of it can be sent to it

A machine's poll address, its asset number, which note acceptor and which printer it has, and
which of the three cashout validation styles it performs are all held in its non-volatile memory.
None of them is settable over SAS — a host may ask what they are and may not change them — and the
only shipped way to set them is the platform's own audit menu, which is a framebuffer a person
clicks through with the main and logic doors open.

That was done once, by hand, in M2, and saved. The result is `fixtures/nvram/golden.nvram.gz`:
eight megabytes that compress to eleven kilobytes, because almost all of it is zero.

What M2 did not set was the validation style, and that is what a real machine needed before it
could redeem a ticket at all. A machine performing no host validation is a machine that returns
every ticket put into it, which is exactly what M3's real-machine run observed and correctly
declined to explain at the time.

## Two fixtures, because a machine performs one style at a time

| Fixture | Validation style | Takes tickets | What it is for |
|---|---|---|---|
| `golden.nvram.gz` | standard | no | chapter 10.1, and every cashout the machine numbers itself |
| `secure-enhanced.nvram.gz` | secure enhanced | yes | redemption, which only the host-validated styles allow |

They are not variants of taste. Section 15 does not allow ticket redemption alongside standard
validation, and only standard validation answers the cashout ticket information poll, so a single
image cannot serve both halves of chapter 10. The machine's own audit menu enforces the same
pairing: choosing a host-validated style turns the ticket-in option on and choosing standard turns
it off.

## How the second one was made

Not by walking the menu again. `sasctl nvram validation` reads the first fixture, rewrites the
option word, and writes the second:

```
sasctl nvram validation secure_enhanced \
    --fixture fixtures/nvram/golden.nvram.gz \
    --out fixtures/nvram/secure-enhanced.nvram.gz
```

The result differs from what it was made from by **four bytes** — the option word in each of the
memory's two banks, and each bank's integrity mark. That number is the point. A fixture produced
by rewriting a memory has to be reviewable, and four bytes of difference from a memory a person
configured by hand is something somebody can check; eight megabytes of difference is something
they have to take on trust. A test asserts that nothing outside the option word and the marks moves.

### Why writing into it from outside is possible at all

The memory protects itself with a rule simple enough to maintain from outside, and the rule was
verified against the committed fixture before anything was written:

* it is **two identical banks** of four megabytes;
* the first sixteen-bit word of each bank is the **exclusive-or of every other sixteen-bit word in
  it**;
* the machine compares the banks at power-up, and where one of them disagrees with its own mark it
  copies the other over it and carries on.

So does this. A memory saved from under a machine that was killed rather than closed is routinely
caught mid-transaction, and it is read from whichever bank agrees with itself, saying which. A
memory in which neither bank agrees with itself is refused: there is no configuration in it to read.

### Where the records are, and how a wrong answer is prevented rather than risked

Where each record sits is not discoverable from the bytes. It follows from the order the platform
constructs them in, and it changes whenever any of them changes size. So the offsets are pinned to
the arrangement revision the memory itself records — 7, for both fixtures — and a memory in any
other revision is refused rather than written at a guess.

A revision number is one number, and two builds can agree on it and disagree about everything else,
so that guard is not trusted on its own. The records are read back before they are believed: a poll
address past 127, which SAS does not define, or a validation style reading as the fourth value,
which is assigned to no style, both say the offsets are not this memory's whatever its revision
claims, and both refuse.

The offsets themselves were established from a memory the machine wrote, not from arithmetic. The
venue name, the two address lines and the note acceptor's hash pin the configuration block; the
maker's two characters followed by the poll address, the asset number and the option word pin the
host-link block. Both landed on the arrangement the platform's own declaration order predicts.

### The trap that comes with the revision

**The committed fixtures cannot be restored onto a machine built from a QSP with a different
arrangement revision.** The machine clears any memory not written in the revision it was built for,
so restoring one produces a machine with no configuration at all — which looks exactly like a
machine that was never configured. Both directions are affected. The fixtures are revision 7, which
is what the M0 preset's QSP fetches; the QSP checkout beside it on this machine is revision 6.

`sasctl nvram show` says which revision each side holds, so the mismatch is a sentence rather than
a mystery.

## Two numberings for the same three styles, and they are not the same numbering

The machine **stores** its validation style as none, secure enhanced, system. It **reports** it on
the wire as standard, system, secure enhanced. Two of the three swap places — and both of those are
the styles that can redeem a ticket.

A stored value used as a wire value therefore stays in range, decodes without complaint, and is
wrong about precisely the thing a ticket test is asking. Both directions go through one table, and
a round trip through it is asserted for all three styles.

## The poll that makes the image checkable

The enabled-features reply, long poll `$A0`, is where a machine publishes its validation style. It
is now decoded, which changes three things:

* a case may require a validation style and have the requirement **answered by the machine** rather
  than by our own fixture's configuration;
* the software machine answers the same poll, so the same question reaches both peers the same way
  — a disagreement between them becomes a finding rather than an artefact of one being asked and
  the other being told;
* `10.1.1` can assert what the corpus actually asks for.

### What it needed in the model

A run of bits inside another field. The validation style is bits six and five of one byte, and a
model that can only name whole bytes has to describe that either as a mask compared by hand, which
cannot tell one two-bit value from another, or as a decoder written for that one command. So a
field may now say which bits of which earlier field it is — the same family as a field whose length
comes from an earlier one, and a group that repeats as many times as an earlier one says. It
consumes no bytes of its own, in either direction: the reply is twelve bytes whatever the flags say.

The layout was read from the library's handler and then checked against twelve bytes captured from
a machine that is not ours, in the conformance corpus's own recording of this exchange:

```
HOST   [01] A0 00 00 6C 13
EGM     01  A0 00 00 82 CD 03 00 00 00 F3 00
```

The three zero bytes after the flags are the reserved ones the specification has not assigned, and
a reader who stopped after the flags would take them for a check sequence.

### What it does not know

The answer is about the channel it came back on, not about the machine. The library clears the bits
of any category the answering channel has not been given, so a link that does not carry validation
reports the standard style whatever the machine is configured in. That is the honest answer for a
test running on that link, and it is all one link can know.

## 10.1.1, fixed

The case used to send long poll `$4C`, which is the command that **sets** a secure enhanced
validation identifier — a setter for one of the two styles the case is about the absence of. It
skipped, so nothing was ever reported wrongly, but it would have asserted the wrong thing the
moment the reply it named could be decoded. The corpus's own pass criterion is the enabled-features
reply, feature codes byte 1, bits 6 to 5 reading zero, and that is what it asserts now.

## Measured against a real machine

Both images were installed in turn, the demo was booted on each, and it was asked the same question.

| | `golden` | `secure-enhanced` |
|---|---|---|
| enabled-features reply | `01 A0 00 00 06 CD 03 00 00 00 FD 15` | `01 A0 00 00 D6 CD 03 00 00 00 3E 50` |
| validation style, bits 6–5 | `00` — standard | `10` — secure enhanced |
| ticket redemption, bit 7 | clear | set |
| validation extensions, bit 4 | clear | set |
| exception `$3F` raised, in one boot | **0** | **10** |

The last row is the independent half. `$3F` is the machine asking a host to give it a validation
identifier, which a machine in secure enhanced validation raises at power-up and every fifteen
seconds until one arrives. The image was never told to produce that; it is the machine acting on
the style it was put in, and it is behaviour the standard image never produces.

The conformance tree then reports honestly in both configurations:

```
sasctl run tests/conformance --tcp 127.0.0.1:9760      # standard
  PASS  10.1.1 standard validation configuration (2.5s, 5 assert(s))
  SKIP  10.1.3 Cashout Ticket 1
        stimulus.bill: this environment provides no 'stimulus.bill'

sasctl run tests/conformance --tcp 127.0.0.1:9760      # secure enhanced
  SKIP  10.1.1 standard validation configuration
        egm.validation: 'egm.validation' is 'secure_enhanced', not 'standard'
```

A machine in another style is skipped by name rather than failed, and the name comes off the wire.

## What is deliberately not claimed

* **A ticket has not been redeemed on a real machine.** The image is what made that possible; it is
  not what makes it done. A machine in secure enhanced validation will not derive a ticket number
  until a host sets its validation identifier with long poll `$4C`, and that command is not
  modelled here — which is why the machine spends the run asking for one. The identifier mark in
  the memory is deliberately left clear in both fixtures, because only a host setting one may raise
  it; a fixture that raised it would silence the request without there being an identifier.
* **The validation extensions bit is a known disagreement between the two peers.** A real machine
  sets it alongside the enhanced styles; the software machine leaves it clear, because the commands
  it advertises are not implemented there. Stated so that a case run against both does not assert
  on it until they are.
* **The `$A0` request is only ever sent for game zero.** Per-game features are a real question and
  the layout carries the game number both ways, but no case asks one yet.
