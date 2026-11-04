# Section 4 of the plan: what must scale with the accounting denomination.
#
# Every case here runs at configuration B - ten centavos - and every expected value is the cents
# amount divided by ten. A value that comes back undivided over-reports money to the host.
#
# The selected-meter reply is walked, never indexed by offset: each meter states its own width and
# a meter the machine does not support is left out of the reply altogether. The code of each entry
# is asserted alongside its value, so a shifted reply fails on the code rather than quietly
# comparing the wrong meter.

test "AD-16 the credits meters are reported in accounting units" {
  tag @accounting-denomination @needs-stimulus @blocking
  requires decoder.lp6F
  requires machine.accounting_denomination
  requires stimulus.bill
  requires stimulus.play

  machine accounting denomination 10c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  # Five hundred pesos in notes, then five hundred pesos of coin in. The plan asks for a thousand
  # pesos of headroom, which a machine that plays exactly what it is told does not need.
  egm bill insert 10000
  egm bill insert 10000
  egm bill insert 10000
  egm bill insert 10000
  egm bill insert 10000

  # A game credit is one peso, so five games at a hundred credits wager fifty thousand centavos.
  egm play games=5 bet=100

  # The eight credits meters the case names: total coin in, total coin out, current credits,
  # total ticket in, total ticket out, machine paid paytable win, won credits, hand paid credits.
  # Two bytes per code, least significant byte first, after two bytes of game number.
  host send lp 6F data 120000000001000C00150016001C0022002300

  # Five hundred pesos is fifty thousand centavos, which at ten centavos is five thousand units.
  # A reply of 50000 here means the conversion did not happen.
  host expect field meters[0].code == 0
  host expect field meters[0].value == 5000
}

# The machine sums its games in cents and converts once. Converting inside the per-game loop would
# truncate each game separately and lose a remainder from every one of them.
test "AD-17 the machine total is the sum converted once, not the sum of conversions" {
  tag @accounting-denomination @needs-stimulus
  requires decoder.lp6F
  requires machine.accounting_denomination
  requires machine.multiple_games
  requires stimulus.play

  machine accounting denomination 10c

  note "Configuration B. Contrive coin in of twenty-five centavos on each of four games."

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  # Each game holds twenty-five centavos, which is two units with five centavos truncated.
  host send lp 6F data 0400010000
  host expect field meters[0].value == 2

  # The machine as a whole holds one hundred centavos, which is ten units - not the eight that
  # summing the four per-game readings would give.
  host send lp 6F data 0400000000
  host expect field meters[0].value == 10
}

test "AD-18 a real-time game start reports credits wagered and coin in in different units" {
  tag @accounting-denomination @needs-stimulus
  requires decoder.lp7F
  requires machine.accounting_denomination
  requires stimulus.play
  requires stimulus.real_time_events

  machine accounting denomination 10c

  note "Configuration B, real-time events enabled. Play one game with a known wager and a known win."
  note "Credits wagered is a number of game credits and is unchanged by the denomination, while total coin in on the same event is in tenths of a peso. The game end event carries the win in tenths of a peso."
}

test "AD-19 a legacy bonus is read and reported in accounting units" {
  tag @accounting-denomination @needs-stimulus
  requires decoder.lp9A
  requires machine.accounting_denomination
  requires stimulus.legacy_bonus

  machine accounting denomination 10c

  note "Configuration B. Award a legacy bonus of five hundred units, which is fifty pesos."
  note "The inbound amount is read as accounting units, so the credit meter rises by fifty pesos and the bonus meters report five hundred rather than five thousand."
}

test "AD-20 the wager category coin in is in accounting units" {
  tag @accounting-denomination @needs-stimulus
  requires decoder.lpB4
  requires machine.accounting_denomination
  requires stimulus.play

  # The software machine answers no wager category at all - it refuses a command it has no
  # values for - so a case about what that reply reports needs a machine that has some. This
  # requirement was added when the reply became decodable: until then decoder.lpB4 was what
  # held the case back, and the case turned out never to have been written.
  requires machine.wager_categories

  machine accounting denomination 10c

  note "Configuration B, with a known coin in on wager category zero of game one."
}

# The handpay cases. Table 7.8 puts the amount in accounting units unless a progressive
# contributed to it, and puts the partial pay in accounting units unconditionally. Getting the
# progressive one left alone matters exactly as much as getting the others converted.
test "AD-21 a cancelled-credits handpay reports its amount in accounting units" {
  tag @accounting-denomination @needs-stimulus @blocking
  requires decoder.lp1B
  requires machine.accounting_denomination
  requires stimulus.handpay

  machine accounting denomination 10c

  requires stimulus.handpay.reset

  note "Configuration B. Force a cancelled-credits handpay of one hundred pesos."

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  egm handpay 10000
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  host expect field level == 128
  host expect field amount == 1000

  egm handpay reset
}

test "AD-22 a non-progressive top win handpay reports its amount in accounting units" {
  tag @accounting-denomination @needs-stimulus
  requires decoder.lp1B
  requires machine.accounting_denomination
  requires stimulus.handpay.top_award

  machine accounting denomination 10c

  note "Configuration B. Force a handpay reported at level 40 hex, of one hundred pesos."

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  host send lp 1B
  host expect field level == 64
  host expect field amount == 1000
}

# The one handpay that must NOT be converted. Table 7.8 makes the amount cents whenever any part
# of the win came from a progressive.
test "AD-23 a handpay with a progressive component stays in cents" {
  tag @accounting-denomination @needs-stimulus @blocking
  requires decoder.lp1B
  requires machine.accounting_denomination
  requires stimulus.handpay
  requires stimulus.progressive

  machine accounting denomination 10c

  note "Configuration B. Force a handpay of one hundred pesos with a progressive component, at level 01."

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  host send lp 1B
  host expect field level == 1
  host expect field amount == 10000
}

test "AD-24 a partial pay is in accounting units whatever the amount field is doing" {
  tag @accounting-denomination @needs-stimulus @blocking
  requires decoder.lp1B
  requires machine.accounting_denomination
  requires stimulus.handpay.partial_pay

  machine accounting denomination 10c

  note "Configuration B. Force a handpay of one hundred pesos with a partial pay of fifty, once at level 01 and once at level 00. The partial pay reads the same both times."

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  host send lp 1B
  host expect field partial_pay == 500
}
