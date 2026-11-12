# Chapter 23, section 23.14 - the validation meters, through $50 and by code.
#
# $50 names a validation type and gets back how many validations of that type the machine has done
# and their total in cents. Each type has a pair of selected meters, cents then count, which the
# document pairs as:
#   00 and 02 cashable ticket out    0086 0087      01 and 03 restricted ticket out  0088 0089
#   04 debit ticket out              008A 008B      10 hand pay cash out, receipt    008C 008D
#   20 hand pay win, receipt         008E 008F      40 hand pay cash out, no receipt 0090 0091
#   60 hand pay win, no receipt      0092 0093      80 cashable ticket in            0080 0081
#   81 restricted ticket in          0082 0083      82 non-restricted ticket in      0084 0085
#
# The document produces one validation of each kind before reading its type, which needs funds
# transfers, hand pay receipts and restricted credit this build cannot make. So this case reads all
# twelve types and all twenty codes at rest, asserts every reply's shape, and leaves the agreement
# between $50 and the pairs (host.compare_replies) and the producing of each kind as notes.

test "23.14 the validation meters agree with their selected meters" {
  tag @egm-test-scripts @chapter-23 @wire-only
  requires decoder.lp6F, decoder.lpAF, decoder.lp2F, decoder.lp50

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 1A000080008100820083008400850086008700880089008A008B00
  host expect field meters[0].code >= $80
  host expect field meters[11].code >= $80
  host expect field meters[11].code <= $8B
  host send lp AF data 1A000080008100820083008400850086008700880089008A008B00
  host expect field meters[11].code <= $8B

  host send lp 6F data 1200008C008D008E008F009000910092009300
  host expect field meters[0].code >= $8C
  host expect field meters[7].code >= $8C
  host expect field meters[7].code <= $93
  host send lp AF data 1200008C008D008E008F009000910092009300
  host expect field meters[7].code <= $93

  # The same twenty through $2F, ten at a time, the most the machine library answers in one.
  host send lp 2F data 0C000080818283848586878889
  host expect field meters[0].code >= $80
  host expect field meters[9].code <= $89
  host send lp 2F data 0C00008A8B8C8D8E8F90919293
  host expect field meters[0].code >= $8A
  host expect field meters[9].code >= $8A
  host expect field meters[9].code <= $93

  # Every type in the table; the reply names the type it was asked about.
  host send lp 50 with validation_type=$00
  host expect field validation_type == $00
  host send lp 50 with validation_type=$01
  host expect field validation_type == $01
  host send lp 50 with validation_type=$02
  host expect field validation_type == $02
  host send lp 50 with validation_type=$03
  host expect field validation_type == $03
  host send lp 50 with validation_type=$04
  host expect field validation_type == $04
  host send lp 50 with validation_type=$10
  host expect field validation_type == $10
  host send lp 50 with validation_type=$20
  host expect field validation_type == $20
  host send lp 50 with validation_type=$40
  host expect field validation_type == $40
  host send lp 50 with validation_type=$60
  host expect field validation_type == $60
  host send lp 50 with validation_type=$80
  host expect field validation_type == $80
  host send lp 50 with validation_type=$81
  host expect field validation_type == $81
  host send lp 50 with validation_type=$82
  host expect field validation_type == $82

  note "For each type the machine supports, the count and cents in the $50 reply equal its pair of meters in the $6F, $AF and $2F replies, using the table in this file's header (host.compare_replies)."
  note "Not run: producing one validation of each kind first - a cash out and a large win paid to ticket, cashable, restricted and debit tickets from a funds transfer, the four hand pays with and without a receipt, and each kind of ticket put back in (stimulus.aft, machine.handpay_receipts, machine.restricted_credits)."
}
