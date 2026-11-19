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
# twenty codes and all twelve types at rest: each code is remembered from $6F, $AF and $2F must
# agree, and each $50 count and amount must equal its pair. Producing each kind first is a note.

test "23.14 the validation meters agree with their selected meters" {
  tag @egm-test-scripts @chapter-23 @wire-only
  requires decoder.lp6F, decoder.lpAF, decoder.lp2F, decoder.lp50
  requires host.compare_replies, host.meter_arithmetic

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 1A000080008100820083008400850086008700880089008A008B00
  host expect field meters[0].code == $80
  host remember field meters[0].value as m80
  host expect field meters[1].code == $81
  host remember field meters[1].value as m81
  host expect field meters[2].code == $82
  host remember field meters[2].value as m82
  host expect field meters[3].code == $83
  host remember field meters[3].value as m83
  host expect field meters[4].code == $84
  host remember field meters[4].value as m84
  host expect field meters[5].code == $85
  host remember field meters[5].value as m85
  host expect field meters[6].code == $86
  host remember field meters[6].value as m86
  host expect field meters[7].code == $87
  host remember field meters[7].value as m87
  host expect field meters[8].code == $88
  host remember field meters[8].value as m88
  host expect field meters[9].code == $89
  host remember field meters[9].value as m89
  host expect field meters[10].code == $8A
  host remember field meters[10].value as m8A
  host expect field meters[11].code == $8B
  host remember field meters[11].value as m8B
  host send lp AF data 1A000080008100820083008400850086008700880089008A008B00
  host expect field meters[0].code == $80
  host expect field meters[0].value == saved.m80
  host expect field meters[1].code == $81
  host expect field meters[1].value == saved.m81
  host expect field meters[2].code == $82
  host expect field meters[2].value == saved.m82
  host expect field meters[3].code == $83
  host expect field meters[3].value == saved.m83
  host expect field meters[4].code == $84
  host expect field meters[4].value == saved.m84
  host expect field meters[5].code == $85
  host expect field meters[5].value == saved.m85
  host expect field meters[6].code == $86
  host expect field meters[6].value == saved.m86
  host expect field meters[7].code == $87
  host expect field meters[7].value == saved.m87
  host expect field meters[8].code == $88
  host expect field meters[8].value == saved.m88
  host expect field meters[9].code == $89
  host expect field meters[9].value == saved.m89
  host expect field meters[10].code == $8A
  host expect field meters[10].value == saved.m8A
  host expect field meters[11].code == $8B
  host expect field meters[11].value == saved.m8B

  host send lp 6F data 1200008C008D008E008F009000910092009300
  host expect field meters[0].code == $8C
  host remember field meters[0].value as m8C
  host expect field meters[1].code == $8D
  host remember field meters[1].value as m8D
  host expect field meters[2].code == $8E
  host remember field meters[2].value as m8E
  host expect field meters[3].code == $8F
  host remember field meters[3].value as m8F
  host expect field meters[4].code == $90
  host remember field meters[4].value as m90
  host expect field meters[5].code == $91
  host remember field meters[5].value as m91
  host expect field meters[6].code == $92
  host remember field meters[6].value as m92
  host expect field meters[7].code == $93
  host remember field meters[7].value as m93
  host send lp AF data 1200008C008D008E008F009000910092009300
  host expect field meters[0].code == $8C
  host expect field meters[0].value == saved.m8C
  host expect field meters[1].code == $8D
  host expect field meters[1].value == saved.m8D
  host expect field meters[2].code == $8E
  host expect field meters[2].value == saved.m8E
  host expect field meters[3].code == $8F
  host expect field meters[3].value == saved.m8F
  host expect field meters[4].code == $90
  host expect field meters[4].value == saved.m90
  host expect field meters[5].code == $91
  host expect field meters[5].value == saved.m91
  host expect field meters[6].code == $92
  host expect field meters[6].value == saved.m92
  host expect field meters[7].code == $93
  host expect field meters[7].value == saved.m93

  # The same twenty through $2F, ten at a time, the most the machine library answers in one.
  host send lp 2F data 0C000080818283848586878889
  host expect field meters[0].code == $80
  host expect field meters[0].value == saved.m80
  host expect field meters[1].code == $81
  host expect field meters[1].value == saved.m81
  host expect field meters[2].code == $82
  host expect field meters[2].value == saved.m82
  host expect field meters[3].code == $83
  host expect field meters[3].value == saved.m83
  host expect field meters[4].code == $84
  host expect field meters[4].value == saved.m84
  host expect field meters[5].code == $85
  host expect field meters[5].value == saved.m85
  host expect field meters[6].code == $86
  host expect field meters[6].value == saved.m86
  host expect field meters[7].code == $87
  host expect field meters[7].value == saved.m87
  host expect field meters[8].code == $88
  host expect field meters[8].value == saved.m88
  host expect field meters[9].code == $89
  host expect field meters[9].value == saved.m89
  host send lp 2F data 0C00008A8B8C8D8E8F90919293
  host expect field meters[0].code == $8A
  host expect field meters[0].value == saved.m8A
  host expect field meters[1].code == $8B
  host expect field meters[1].value == saved.m8B
  host expect field meters[2].code == $8C
  host expect field meters[2].value == saved.m8C
  host expect field meters[3].code == $8D
  host expect field meters[3].value == saved.m8D
  host expect field meters[4].code == $8E
  host expect field meters[4].value == saved.m8E
  host expect field meters[5].code == $8F
  host expect field meters[5].value == saved.m8F
  host expect field meters[6].code == $90
  host expect field meters[6].value == saved.m90
  host expect field meters[7].code == $91
  host expect field meters[7].value == saved.m91
  host expect field meters[8].code == $92
  host expect field meters[8].value == saved.m92
  host expect field meters[9].code == $93
  host expect field meters[9].value == saved.m93

  # Every type in the table; the reply names the type it was asked about. Cashable and restricted
  # tickets out each have two types, one for a cash out and one for a funds transfer, sharing one
  # pair of meters, so the second of each pair must make up the rest of the meter.
  host send lp 50 with validation_type=$00
  host expect field validation_type == $00
  host remember field quantity as cashable_out_count
  host remember field amount as cashable_out_cents
  host send lp 50 with validation_type=$01
  host expect field validation_type == $01
  host remember field quantity as restricted_out_count
  host remember field amount as restricted_out_cents
  host send lp 50 with validation_type=$02
  host expect field validation_type == $02
  host expect field quantity == saved.m87 - saved.cashable_out_count
  host expect field amount == saved.m86 - saved.cashable_out_cents
  host send lp 50 with validation_type=$03
  host expect field validation_type == $03
  host expect field quantity == saved.m89 - saved.restricted_out_count
  host expect field amount == saved.m88 - saved.restricted_out_cents
  host send lp 50 with validation_type=$04
  host expect field validation_type == $04
  host expect field quantity == saved.m8B
  host expect field amount == saved.m8A
  host send lp 50 with validation_type=$10
  host expect field validation_type == $10
  host expect field quantity == saved.m8D
  host expect field amount == saved.m8C
  host send lp 50 with validation_type=$20
  host expect field validation_type == $20
  host expect field quantity == saved.m8F
  host expect field amount == saved.m8E
  host send lp 50 with validation_type=$40
  host expect field validation_type == $40
  host expect field quantity == saved.m91
  host expect field amount == saved.m90
  host send lp 50 with validation_type=$60
  host expect field validation_type == $60
  host expect field quantity == saved.m93
  host expect field amount == saved.m92
  host send lp 50 with validation_type=$80
  host expect field validation_type == $80
  host expect field quantity == saved.m81
  host expect field amount == saved.m80
  host send lp 50 with validation_type=$81
  host expect field validation_type == $81
  host expect field quantity == saved.m83
  host expect field amount == saved.m82
  host send lp 50 with validation_type=$82
  host expect field validation_type == $82
  host expect field quantity == saved.m85
  host expect field amount == saved.m84

  note "Not run: producing one validation of each kind first - a cash out and a large win paid to ticket, cashable, restricted and debit tickets from a funds transfer, the four hand pays with and without a receipt, and each kind of ticket put back in (machine.aft, machine.handpay_receipts, machine.restricted_credits)."
}
