# Chapter 15, sections 15.1 to 15.3 - turning legacy bonusing on and off, holding the next game
# back, and paying a bonus.
#
# A legacy bonus is a single long poll, $8A, carrying an amount in accounting credits and a tax
# status: $00 deductible, $01 non-deductible, $02 wager match. The machine acknowledges it, adds
# the amount to the credit meter, and reports exception $7C - one byte with real-time reporting
# off, or with reporting on a message carrying the multiplier, the multiplied win, the tax status
# and the bonus. The game delay, long poll $2E, holds the next game back for a time in tenths of a
# second so a host has a window in which to pay.
#
# Both polls are ignored outright by a machine with legacy bonusing switched off. The machine
# library reads its legacy-bonus feature bit before anything else in both handlers, and the
# software machine offers no legacy bonusing at all, so that half of 15.1 runs here. Everything
# that needs a bonus paid states machine.legacy_bonus and skips.
#
# The document recommends running the chapter with real-time reporting on, and again with it off.
# The cases turn it on, because the event is the only place the bonus amount and tax status can be
# read; with it off, the same cases hold with $7C arriving as one byte.

test "15.1 legacy bonusing can be switched off" {
  tag @egm-test-scripts @chapter-15 @wire-only
  requires decoder.lpA0

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  host send lp A0 with game_number=0
  host expect field game_number == 0

  # Switched off, a bonus of five deductible credits and a five-second game delay are both
  # ignored - not refused.
  host expect no reply to lp 8A data 0000000500
  host expect no reply to lp 2E data 0050

  note "Before the run, find the operator setting for legacy bonusing and turn it off. Bit 2 of the first feature byte in the enabled-features reply is then clear; this build names no single bit of that byte, so read it in the frames."
  note "Then, by hand: turn it on. The bit is set; legacy and AFT bonusing are configured separately, and only one of the two may be on at a time. Send the bonus: it is acknowledged, five credits reach the credit meter, and $7C follows - with reporting on, carrying multiplier 0, multiplied win 0, tax status 00 and bonus 00 00 00 05."
  note "Also by hand, on a card game or a double-up: reach a winning hand before the draw, stop polling, send the bonus and wait five seconds, then finish the game and resume polling. The bonus is paid and $7C carries tax status 00 and five credits."
  note "Not run: the bonus sent with a broken check sequence must be ignored or refused (host.bad_crc)."
}

test "15.2 the game delay holds the next game back" {
  tag @egm-test-scripts @chapter-15 @needs-a-person
  requires machine.legacy_bonus, stimulus.real_time_events, stimulus.bill, stimulus.play
  requires decoder.lp0E, decoder.event7F, decoder.event7C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  egm bill insert 2000
  host forget exceptions within 2s

  # Five seconds after every game, then a bonus paid inside the window.
  host send lp 2E data 0050
  egm play games=1 bet=1 win=0
  host expect exception game_ended within 8s
  host send lp 8A data 0000000500
  host expect field tax_status of event legacy_bonus_pay_or_jackpot_awarded == 0 within 8s
  host expect field bonus_amount of event legacy_bonus_pay_or_jackpot_awarded == 5 within 8s

  # And off again.
  host send lp 2E data 0000

  note "Before the run, enable legacy bonusing and leave the machine idle and playable."
  note "Every timing in this case is a person's to check - this host cannot see a machine refuse to start a game. After each game the next one cannot start for five seconds. A ten-second delay sent during a five-second one starts a fresh delay of ten seconds from its acknowledgement. A delay of zero ends the current one at once and none follows the next game."
  note "And by hand: with the five-second delay set, play a game and send it again every two or three seconds for thirty seconds. Every one is acknowledged, and the machine stays held until five seconds after the last."
  note "Not run: the delay sent with a broken check sequence must be ignored or refused (host.bad_crc)."
}

test "15.3 an idle machine pays a bonus at once" {
  tag @egm-test-scripts @chapter-15 @needs-a-person
  requires machine.legacy_bonus, stimulus.real_time_events, host.meter_arithmetic
  requires decoder.lp0E, decoder.event7C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  host forget exceptions within 2s

  host send lp 6F data 0400000C00
  host remember field meters[0].value as credits
  host send lp 8A data 0000000500
  host expect field tax_status of event legacy_bonus_pay_or_jackpot_awarded == 0 within 8s
  host expect field bonus_amount of event legacy_bonus_pay_or_jackpot_awarded == 5 within 8s
  host send lp 6F data 0400000C00
  host expect field meters[0].value == saved.credits + 5

  note "Before the run, enable legacy bonusing and leave the machine idle and playable. With reporting off, $7C arrives as one byte and the credit meter still moves by five."
}
