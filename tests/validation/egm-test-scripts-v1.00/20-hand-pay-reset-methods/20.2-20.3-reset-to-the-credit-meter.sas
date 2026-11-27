# Chapter 20, sections 20.2 and 20.3 - letting a jackpot hand pay go to the credit meter.
#
# Long poll $A8 is the host saying how the next jackpot hand pay may be cleared: as a standard hand
# pay, or onto the credit meter. It holds for one hand pay only and the last one sent wins, so a host
# that wants the credit-meter route has to say so every time. The hand pay information reply shows
# which routes are open (reset_id 01 when the credit meter is), and the meters show which one was
# taken: onto the credit meter it is coin out and current credits that move, as a standard hand pay
# it is the jackpot meter.
#
# 20.2 clears the hand pay with the machine's own credit-meter key; 20.3 clears it from the host
# with $94. This build decodes $94 but not $A8, and has no step for the credit-meter key. The field
# names used below for the $A8 reply are provisional, chosen ahead of its decoder.

# The credit-meter key works only when the host has allowed it for this hand pay, so every step that
# matters is the key, which nothing here can turn.
test "20.2 the credit-meter key follows the host's choice of reset method" {
  tag @egm-test-scripts @chapter-20 @needs-a-person @needs-configuration @destructive
  requires stimulus.handpay.credit_reset
  requires decoder.lpA8, decoder.lp1B
  requires machine.handpay_reset_to_credits, stimulus.handpay.jackpot, stimulus.handpay.reset

  note "Configure the machine for remote hand pay reset and host-controlled reset to the credit meter, a low jackpot limit and a high credit limit."
  note "Win a jackpot hand pay and note the credit meter. Send $A8 asking for the credit meter: acknowledged with code 00. The hand pay information says reset_id 01. Turn the credit-meter key: the hand pay clears onto the credit meter, coin out and current credits rise by the amount in accounting units, and the jackpot meter does not move."
  note "Win another. Send $A8 asking for a standard reset: code 00, and reset_id now 00. Turn the credit-meter key: nothing happens and the hand pay stays. Clear it with the standard key: the credit meter stays, the jackpot meter rises by the amount."
  note "Win another. Send $A8 for standard and then for the credit meter: the second one wins, reset_id is 01, and the credit-meter key clears it onto the credit meter as in the first round."
  note "Turn off both remote reset and host-controlled credit reset at the machine. $A8 is now ignored, with or without a hand pay; the hand pay information says reset_id 00, the credit-meter key does nothing, and the standard key clears the hand pay onto the jackpot meter."
}

test "20.3 a remote reset of a jackpot onto the credit meter" {
  tag @egm-test-scripts @chapter-20 @needs-stimulus @needs-configuration @destructive
  requires decoder.lp94, decoder.lpA8
  requires machine.remote_handpay_reset, machine.handpay_reset_to_credits
  requires stimulus.handpay.jackpot, stimulus.settle
  requires decoder.lp1B
  machine accounting denomination 5c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  # Nothing to clear yet.
  host send lp 94
  host expect field reset_code of lp 94 == 2

  # Allowed onto the credit meter, then cleared from the host: credits and coin out rise, the
  # jackpot meter does not. Fifteen hundred dollars is thirty thousand five-cent units.
  egm handpay jackpot 150000
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  host expect field reset_id of lp 1B == 1
  host send lp A8 data 01
  host expect field ack_code of lp A8 == 0
  host send lp 94
  host expect field reset_code of lp 94 == 0
  host expect meter credits += 30000 within 5s
  host expect meter coin_out += 30000 within 2s
  host expect meter jackpot unchanged within 2s
  host forget exceptions within 2s

  # No $A8 this time, so the earlier choice does not carry over: a standard reset onto the jackpot
  # meter, with the credit meter left alone.
  egm handpay jackpot 150000
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  host send lp 94
  host expect meter jackpot += 30000 within 5s
  host expect meter credits unchanged within 2s
  host expect meter coin_out unchanged within 2s
  host forget exceptions within 2s

  # A standard reset asked for explicitly.
  egm handpay jackpot 150000
  host expect exception handpay_is_pending within 10s
  host send lp A8 data 00
  host expect field ack_code of lp A8 == 0
  host send lp 1B
  host send lp 94
  host expect field reset_code of lp 94 == 0
  host expect meter jackpot += 30000 within 5s
  host expect meter credits unchanged within 2s
  host expect meter coin_out unchanged within 2s
  host forget exceptions within 2s

  # Standard and then credit meter: the last one sent is the one used.
  egm handpay jackpot 150000
  host expect exception handpay_is_pending within 10s
  host send lp A8 data 00
  host send lp A8 data 01
  host expect field ack_code of lp A8 == 0
  host send lp 1B
  host send lp 94
  host expect field reset_code of lp 94 == 0
  host expect meter credits += 30000 within 5s
  host expect meter coin_out += 30000 within 2s
  host expect meter jackpot unchanged within 2s

  note "Before the run, configure the machine for remote hand pay reset and host-controlled reset to the credit meter, with a jackpot limit below fifteen hundred dollars, an upper limit above it for reset to the credit meter, and a high credit limit. The amounts are in cents. Meter 0003 must not move in any round; this build cannot read it by name."
  note "The document's second round, with no $A8 sent, gives the $94 reply code as 01 while also saying the hand pay clears onto the jackpot meter. Those two cannot both hold, so the code is not asserted there; only the meters are, which is what the round is about."
  note "Not done: two rounds where the credit meter route must be refused - a hand pay too large for a low credit limit, and a jackpot above the upper limit for reset to the credit meter. In both reset_id is 00, $A8 asking for the credit meter is answered with code 01, and $94 clears the hand pay onto the jackpot meter. Each needs the machine reconfigured mid-case, which has no step in the language."
}
