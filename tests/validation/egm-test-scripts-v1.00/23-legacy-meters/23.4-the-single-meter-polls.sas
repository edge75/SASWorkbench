# Chapter 23, section 23.4 - every single-meter poll against the selected-meter polls.
#
# Thirty long polls each carry one meter, and every one of those meters can also be asked for
# by code through $6F, $AF and $2F. The document reads them pair by pair; this case reads all the
# codes in three lists through each selected-meter poll and then every single-meter poll, which puts
# the same values on the line in fewer exchanges.
#
# Which code stands behind which poll, as the document pairs them:
#   $10 0004   $11 0000   $12 0001   $13 000A+000B   $14 0002   $15 0005   $16 0006   $17 0007
#   $18 0025 and 0026     $1A 000C   $20 000B, in whole currency units   $2A 0008   $2B 0009
#   $38 004A   $39 004B   $3A 0048   $3B 0045   $3C 004C   $3E 004D   $3F 004E   $40 004F
#   $41 0050   $42 0051   $43 0052   $44 0053   $45 0049   $46 000B   $47 0027   $49 003E
#   $4A 003F
# The bill counts $31 to $37 are left to 23.11, as the document leaves them.
#
# Each value is remembered from the $6F reply, then $AF, $2F and the single-meter poll must carry
# the same number. Each code is checked at its position first, so a machine that leaves a code out
# or answers out of order fails on the code rather than comparing the wrong meters.

test "23.4 each single-meter poll and its selected-meter code" {
  tag @egm-test-scripts @chapter-23 @wire-only
  requires decoder.lp6F, decoder.lpAF, decoder.lp2F
  requires host.compare_replies, host.meter_arithmetic

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # The first twelve codes, 0000 to 000C without 0003.
  host send lp 6F data 1A00000000010002000400050006000700080009000A000B000C00
  host expect field meters[0].code == $00
  host remember field meters[0].value as m00
  host expect field meters[1].code == $01
  host remember field meters[1].value as m01
  host expect field meters[2].code == $02
  host remember field meters[2].value as m02
  host expect field meters[3].code == $04
  host remember field meters[3].value as m04
  host expect field meters[4].code == $05
  host remember field meters[4].value as m05
  host expect field meters[5].code == $06
  host remember field meters[5].value as m06
  host expect field meters[6].code == $07
  host remember field meters[6].value as m07
  host expect field meters[7].code == $08
  host remember field meters[7].value as m08
  host expect field meters[8].code == $09
  host remember field meters[8].value as m09
  host expect field meters[9].code == $0A
  host remember field meters[9].value as m0A
  host expect field meters[10].code == $0B
  host remember field meters[10].value as m0B
  host expect field meters[11].code == $0C
  host remember field meters[11].value as m0C
  host send lp AF data 1A00000000010002000400050006000700080009000A000B000C00
  host expect field meters[0].value == saved.m00
  host expect field meters[1].value == saved.m01
  host expect field meters[2].value == saved.m02
  host expect field meters[3].value == saved.m04
  host expect field meters[4].value == saved.m05
  host expect field meters[5].value == saved.m06
  host expect field meters[6].value == saved.m07
  host expect field meters[7].value == saved.m08
  host expect field meters[8].value == saved.m09
  host expect field meters[9].value == saved.m0A
  host expect field meters[10].value == saved.m0B
  host expect field meters[11].value == saved.m0C

  # The next twelve, 0025 to 004D.
  host send lp 6F data 1A00002500260027003E003F004500480049004A004B004C004D00
  host expect field meters[0].code == $25
  host remember field meters[0].value as m25
  host expect field meters[1].code == $26
  host remember field meters[1].value as m26
  host expect field meters[2].code == $27
  host remember field meters[2].value as m27
  host expect field meters[3].code == $3E
  host remember field meters[3].value as m3E
  host expect field meters[4].code == $3F
  host remember field meters[4].value as m3F
  host expect field meters[5].code == $45
  host remember field meters[5].value as m45
  host expect field meters[6].code == $48
  host remember field meters[6].value as m48
  host expect field meters[7].code == $49
  host remember field meters[7].value as m49
  host expect field meters[8].code == $4A
  host remember field meters[8].value as m4A
  host expect field meters[9].code == $4B
  host remember field meters[9].value as m4B
  host expect field meters[10].code == $4C
  host remember field meters[10].value as m4C
  host expect field meters[11].code == $4D
  host remember field meters[11].value as m4D
  host send lp AF data 1A00002500260027003E003F004500480049004A004B004C004D00
  host expect field meters[0].value == saved.m25
  host expect field meters[1].value == saved.m26
  host expect field meters[2].value == saved.m27
  host expect field meters[3].value == saved.m3E
  host expect field meters[4].value == saved.m3F
  host expect field meters[5].value == saved.m45
  host expect field meters[6].value == saved.m48
  host expect field meters[7].value == saved.m49
  host expect field meters[8].value == saved.m4A
  host expect field meters[9].value == saved.m4B
  host expect field meters[10].value == saved.m4C
  host expect field meters[11].value == saved.m4D

  # The last six, 004E to 0053.
  host send lp 6F data 0E00004E004F005000510052005300
  host expect field meters[0].code == $4E
  host remember field meters[0].value as m4E
  host expect field meters[1].code == $4F
  host remember field meters[1].value as m4F
  host expect field meters[2].code == $50
  host remember field meters[2].value as m50
  host expect field meters[3].code == $51
  host remember field meters[3].value as m51
  host expect field meters[4].code == $52
  host remember field meters[4].value as m52
  host expect field meters[5].code == $53
  host remember field meters[5].value as m53
  host send lp AF data 0E00004E004F005000510052005300
  host expect field meters[0].value == saved.m4E
  host expect field meters[1].value == saved.m4F
  host expect field meters[2].value == saved.m50
  host expect field meters[3].value == saved.m51
  host expect field meters[4].value == saved.m52
  host expect field meters[5].value == saved.m53

  # Total drop as the machine library keeps it, for $13 below.
  host send lp 6F data 0400002400
  host expect field meters[0].code == $24
  host remember field meters[0].value as m24

  # The same thirty codes through $2F, ten at a time: the machine library answers no more than
  # ten in one request and ignores a longer one.
  host send lp 2F data 0C00000001020405060708090A
  host expect field meters[0].code == $00
  host expect field meters[0].value == saved.m00
  host expect field meters[1].code == $01
  host expect field meters[1].value == saved.m01
  host expect field meters[2].code == $02
  host expect field meters[2].value == saved.m02
  host expect field meters[3].code == $04
  host expect field meters[3].value == saved.m04
  host expect field meters[4].code == $05
  host expect field meters[4].value == saved.m05
  host expect field meters[5].code == $06
  host expect field meters[5].value == saved.m06
  host expect field meters[6].code == $07
  host expect field meters[6].value == saved.m07
  host expect field meters[7].code == $08
  host expect field meters[7].value == saved.m08
  host expect field meters[8].code == $09
  host expect field meters[8].value == saved.m09
  host expect field meters[9].code == $0A
  host expect field meters[9].value == saved.m0A
  host send lp 2F data 0C00000B0C2526273E3F454849
  host expect field meters[0].code == $0B
  host expect field meters[0].value == saved.m0B
  host expect field meters[1].code == $0C
  host expect field meters[1].value == saved.m0C
  host expect field meters[2].code == $25
  host expect field meters[2].value == saved.m25
  host expect field meters[3].code == $26
  host expect field meters[3].value == saved.m26
  host expect field meters[4].code == $27
  host expect field meters[4].value == saved.m27
  host expect field meters[5].code == $3E
  host expect field meters[5].value == saved.m3E
  host expect field meters[6].code == $3F
  host expect field meters[6].value == saved.m3F
  host expect field meters[7].code == $45
  host expect field meters[7].value == saved.m45
  host expect field meters[8].code == $48
  host expect field meters[8].value == saved.m48
  host expect field meters[9].code == $49
  host expect field meters[9].value == saved.m49
  host send lp 2F data 0C00004A4B4C4D4E4F50515253
  host expect field meters[0].code == $4A
  host expect field meters[0].value == saved.m4A
  host expect field meters[1].code == $4B
  host expect field meters[1].value == saved.m4B
  host expect field meters[2].code == $4C
  host expect field meters[2].value == saved.m4C
  host expect field meters[3].code == $4D
  host expect field meters[3].value == saved.m4D
  host expect field meters[4].code == $4E
  host expect field meters[4].value == saved.m4E
  host expect field meters[5].code == $4F
  host expect field meters[5].value == saved.m4F
  host expect field meters[6].code == $50
  host expect field meters[6].value == saved.m50
  host expect field meters[7].code == $51
  host expect field meters[7].value == saved.m51
  host expect field meters[8].code == $52
  host expect field meters[8].value == saved.m52
  host expect field meters[9].code == $53
  host expect field meters[9].value == saved.m53

  # And the thirty single-meter polls, each answered with its paired code's value.
  host send lp 10
  host expect field total_cancelled_credits == saved.m04
  host send lp 11
  host expect field total_coin_in == saved.m00
  host send lp 12
  host expect field total_coin_out == saved.m01
  host send lp 13
  host expect field total_drop == saved.m24
  host expect field total_drop >= saved.m0A + saved.m0B
  host send lp 14
  host expect field total_jackpot == saved.m02
  host send lp 15
  host expect field games_played == saved.m05
  host send lp 16
  host expect field games_won == saved.m06
  host send lp 17
  host expect field games_lost == saved.m07
  host send lp 18
  host expect field games_since_power_up == saved.m25
  host expect field games_since_door_closure == saved.m26
  host send lp 1A
  host expect field current_credits == saved.m0C
  host send lp 20
  host expect field total_bill_value >= 0
  host send lp 2A
  host expect field true_coin_in == saved.m08
  host send lp 2B
  host expect field true_coin_out == saved.m09
  host send lp 38
  host expect field bills_500 == saved.m4A
  host send lp 39
  host expect field bills_1000 == saved.m4B
  host send lp 3A
  host expect field bills_200 == saved.m48
  host send lp 3B
  host expect field bills_25 == saved.m45
  host send lp 3C
  host expect field bills_2000 == saved.m4C
  host send lp 3E
  host expect field bills_2500 == saved.m4D
  host send lp 3F
  host expect field bills_5000 == saved.m4E
  host send lp 40
  host expect field bills_10000 == saved.m4F
  host send lp 41
  host expect field bills_20000 == saved.m50
  host send lp 42
  host expect field bills_25000 == saved.m51
  host send lp 43
  host expect field bills_50000 == saved.m52
  host send lp 44
  host expect field bills_100000 == saved.m53
  host send lp 45
  host expect field bills_250 == saved.m49
  host send lp 46
  host expect field bills_accepted_credits == saved.m0B
  host send lp 47
  host expect field external_coin_acceptor_credits == saved.m27
  host send lp 49
  host expect field bills_in_stacker == saved.m3E
  host send lp 4A
  host expect field stacker_credits == saved.m3F

  note "A meter the machine does not support reads zero in all four replies."
  note "$13 is compared with 0024, the total drop the machine library reports, and must be at least 000A plus 000B; it equals that sum only while no ticket or electronic money has gone to the drop."
  note "Not asserted: $20 against 000B. $20 is in whole currency units and 000B in accounting credits, and the language cannot scale one to the other."
  note "Several single-meter polls are divided by the accounting denomination under some of the library's meter modes and the selected-meter polls are not (see sasctl catalogue readable-meters). A pair that differs by exactly that factor is the meter mode, not a fault; ask how the machine is configured before calling it one."
}
