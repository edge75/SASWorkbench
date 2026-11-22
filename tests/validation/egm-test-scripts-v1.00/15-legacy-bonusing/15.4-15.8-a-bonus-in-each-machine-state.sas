# Chapter 15, sections 15.4 to 15.8 - a legacy bonus sent while the machine is doing something.
#
# The machine always acknowledges the bonus; what changes with its state is when it pays. In a
# game it waits for the game to end and the delay to run, while cashing out it waits for the cash
# out, in a hand pay it waits for the reset, and on a player's help screen it may pay at once or
# when the screen closes. Bonuses sent while it waits add up into one award. With a door open, in
# maintenance or in game recall it does not take the bonus at all, and answers busy.
#
# The software machine takes legacy bonuses; what it cannot do here is hold a game open
# (stimulus.game_in_progress), answer busy in a form this build asserts (host.busy_reply) or open a
# player's screen. The cases turn real-time reporting on, as 15.1 explains, and read the bonus out
# of the $7C event.

test "15.4 a bonus sent during a game is paid after it" {
  tag @egm-test-scripts @chapter-15 @needs-a-person
  requires machine.legacy_bonus, stimulus.game_in_progress, stimulus.real_time_events
  requires decoder.lp0E, decoder.event7F, decoder.event7C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  host forget exceptions within 2s
  host send lp 2E data 3200

  note "Before the run, enable legacy bonusing, and if the machine offers one, choose a card game or a double-up."
  note "Start a game and send a bonus of three non-deductible credits (8A data 00 00 00 03 01) before it ends: it is acknowledged. When the game ends, $7F comes first, the machine holds for five seconds, and then pays three credits with $7C carrying tax status 01 and bonus 3."
  note "Start another and send the same bonus three times before it ends: all three are acknowledged, and after $7F and the delay one award of nine credits arrives, tax status 01."
  note "Send a zero delay (2E data 00 00), play a game, and confirm no delay follows it."
  note "Then reach a winning hand before the draw, stop polling and send the bonus. After five seconds of silence the machine chirps its address every 200 ms (host.chirp). Finish the game: the machine holds five seconds, pays three credits, and on resuming polling reports $7F and then $7C with tax status 01 and bonus 3."
}

test "15.5 an idle machine pays at once, and a cash out comes first" {
  tag @egm-test-scripts @chapter-15 @needs-a-person
  requires machine.legacy_bonus, stimulus.real_time_events, host.meter_arithmetic
  requires stimulus.bill, stimulus.button.collect
  requires decoder.lp0E, decoder.event7C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  host forget exceptions within 2s

  host send lp 6F data 0400000C00
  host remember field meters[0].value as credits
  host send lp 8A data 0000000301
  host expect field tax_status of event legacy_bonus_pay_or_jackpot_awarded == 1 within 8s
  host expect field bonus_amount of event legacy_bonus_pay_or_jackpot_awarded == 3 within 8s
  host send lp 6F data 0400000C00
  host expect field meters[0].value == saved.credits + 3

  # After a cash out the bonus is paid as well. The software machine has printed its ticket before
  # any poll can follow the button, and with reporting on a long poll sent while that event is still
  # queued is answered with the event, so the ticket is taken first.
  egm bill insert 2000
  host forget exceptions within 2s
  egm press collect
  host expect exception cashout_ticket_printed within 10s
  host send lp 8A data 0000000301
  host expect field tax_status of event legacy_bonus_pay_or_jackpot_awarded == 1 within 15s
  host expect field bonus_amount of event legacy_bonus_pay_or_jackpot_awarded == 3 within 15s

  note "Before the run, enable legacy bonusing and leave the machine idle and playable."
  note "On a machine whose cash out takes time, send the bonus after pressing collect and before the ticket is printed or the coins paid: it is acknowledged, and $7C follows only once the cash out has finished, which is read in the trace."
}

test "15.6 a bonus sent in a hand pay is paid after the reset" {
  tag @egm-test-scripts @chapter-15 @needs-a-person
  requires machine.legacy_bonus, stimulus.real_time_events
  requires stimulus.handpay.jackpot, stimulus.handpay.reset, stimulus.settle
  requires decoder.lp0E, decoder.lp1B, decoder.event7C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host send lp 0E data 01
  host forget exceptions within 2s
  host send lp 2E data 3200

  # One wager-match bonus of six credits during the lockup.
  egm handpay jackpot 150000
  host expect exception handpay_is_pending within 10s
  host send lp 8A data 0000000602
  host send lp 1B
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host expect field tax_status of event legacy_bonus_pay_or_jackpot_awarded == 2 within 15s
  host expect field bonus_amount of event legacy_bonus_pay_or_jackpot_awarded == 6 within 15s
  host forget exceptions within 2s

  # Two of them during the next: one award of twelve.
  egm handpay jackpot 150000
  host expect exception handpay_is_pending within 10s
  host send lp 8A data 0000000602
  host send lp 8A data 0000000602
  host send lp 1B
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host expect field tax_status of event legacy_bonus_pay_or_jackpot_awarded == 2 within 15s
  host expect field bonus_amount of event legacy_bonus_pay_or_jackpot_awarded == 12 within 15s

  note "Before the run, enable legacy bonusing and set the jackpot limit low enough that a winning game locks up in a hand pay. The document wins the jackpot by play; the steps make one."
  note "After each reset the machine holds for the five-second delay before paying; that is a person's to time."
}

test "15.7 a bonus sent on a player's screen" {
  tag @egm-test-scripts @chapter-15 @needs-a-person
  requires machine.legacy_bonus, machine.player_screens

  note "Enable legacy bonusing and turn reporting on. With the machine idle, open its help screen and send a wager-match bonus of six credits (8A data 00 00 00 06 02): it is acknowledged, and six credits are paid either at once or when the screen is closed. $7C carries tax status 02 and bonus 6."
  note "Repeat for every screen a player can open."
}

test "15.8 a bonus sent with a door open is turned away" {
  tag @egm-test-scripts @chapter-15 @needs-a-person
  requires machine.legacy_bonus, stimulus.door.main, stimulus.real_time_events
  requires host.busy_reply, host.meter_arithmetic
  requires decoder.lp0E, decoder.event7C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  host forget exceptions within 2s

  # Door open: the bonus is answered busy and nothing is paid.
  egm door main open
  host forget exceptions within 2s
  host send lp 6F data 0400000C00
  host remember field meters[0].value as credits
  host send lp 8A data 0000000500
  host send lp 6F data 0400000C00
  host expect field meters[0].value == saved.credits

  # Door shut: the same bonus is taken and paid at once.
  egm door main close
  host forget exceptions within 2s
  host send lp 8A data 0000000500
  host expect field tax_status of event legacy_bonus_pay_or_jackpot_awarded == 0 within 8s
  host expect field bonus_amount of event legacy_bonus_pay_or_jackpot_awarded == 5 within 8s
  host send lp 6F data 0400000C00
  host expect field meters[0].value == saved.credits + 5

  note "Before the run, enable legacy bonusing and leave the machine idle."
  note "With the door open the answer to the bonus is the machine's address followed by 00 - busy - rather than an acknowledgement. This build cannot assert a busy answer as such (host.busy_reply), so it is read in the frames."
  note "Repeat for every other state that stops play and that the machine has: maintenance, game recall."
}
