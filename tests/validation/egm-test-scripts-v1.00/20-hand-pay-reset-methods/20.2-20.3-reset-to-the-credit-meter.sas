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
# with $94. The platform has no credit-meter key, so 20.2 declares one.

# The credit-meter key works only when the host has allowed it for this hand pay. The platform has no
# such key, so the software machine is built with one (D62).
#
# @finding (20.2): after $A8 asks for a standard reset the hand pay information must say reset_id 00.
# The library records the method and leaves the reset ID the platform gave the hand pay when it was
# registered, 01 for any jackpot that fits (libsas lp.cpp:9681, QSP SasNetwork_Conditions.hpp:784).
test "20.2 the credit-meter key follows the host's choice of reset method" {
  tag @egm-test-scripts @chapter-20 @needs-a-person @needs-configuration @destructive @finding
  requires stimulus.handpay.credit_reset
  requires decoder.lpA8, decoder.lp1B
  requires machine.handpay_reset_to_credits, stimulus.handpay.jackpot, stimulus.handpay.reset
  requires stimulus.settle
  machine credit key on
  machine accounting denomination 5c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  # Allowed onto the credit meter, then the key: credits and coin out rise, the jackpot meter does
  # not. Fifteen hundred dollars is thirty thousand five-cent units.
  egm handpay jackpot 150000
  host expect exception handpay_is_pending within 10s
  host send lp A8 data 01
  host expect field ack_code of lp A8 == 0
  host send lp 1B
  host expect field reset_id of lp 1B == 1
  egm handpay reset credits
  host expect meter credits += 30000 within 5s
  host expect meter coin_out += 30000 within 2s
  host expect meter jackpot unchanged within 2s
  host forget exceptions within 2s

  # A standard reset asked for: the key does nothing, and the standard key pays the jackpot meter.
  egm handpay jackpot 150000
  host expect exception handpay_is_pending within 10s
  host send lp A8 data 00
  host expect field ack_code of lp A8 == 0
  host send lp 1B
  host expect field reset_id of lp 1B == 0
  egm handpay reset credits
  host expect meter credits unchanged within 3s
  # The hand pay is still there: the standard key pays it onto the jackpot meter.
  egm handpay reset
  host expect meter jackpot += 30000 within 5s
  host expect meter credits unchanged within 2s
  host forget exceptions within 2s

  # Standard and then credit meter: the second wins, and the key clears it onto the credit meter.
  egm handpay jackpot 150000
  host expect exception handpay_is_pending within 10s
  host send lp A8 data 00
  host send lp A8 data 01
  host expect field ack_code of lp A8 == 0
  host send lp 1B
  host expect field reset_id of lp 1B == 1
  egm handpay reset credits
  host expect meter credits += 30000 within 5s
  host expect meter coin_out += 30000 within 2s
  host expect meter jackpot unchanged within 2s

  note "Before the run, configure the machine for remote hand pay reset and host-controlled reset to the credit meter, with a jackpot limit below fifteen hundred dollars and a high credit limit. Meter 0003 must not move in any round; this build cannot read it by name."
  note "Not done: the document's last round, with remote reset and host-controlled reset to the credit meter turned off at the machine part way through. That is a reconfiguration in the operator's menu the language has no step for."
}

test "20.3 a remote reset of a jackpot onto the credit meter" {
  tag @egm-test-scripts @chapter-20 @needs-stimulus @needs-configuration @destructive
  requires decoder.lp94, decoder.lpA8
  requires machine.remote_handpay_reset, machine.handpay_reset_to_credits
  requires stimulus.handpay.jackpot, stimulus.settle, stimulus.credit_limit
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
  host forget exceptions within 2s

  # A credit limit the machine is already at: the jackpot no longer fits on the credit meter, so only
  # the standard reset is on offer, $A8 asking for the credit meter is refused, and $94 pays it as a
  # standard hand pay. The rounds above left three thousand dollars on the meter.
  egm set credit limit 300000
  host forget exceptions within 2s
  egm handpay jackpot 150000
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  host expect field reset_id of lp 1B == 0
  host send lp A8 data 01
  host expect field ack_code of lp A8 == 1
  host send lp 94
  host expect field reset_code of lp 94 == 0
  host expect meter jackpot += 30000 within 5s
  host expect meter credits unchanged within 2s
  host expect meter coin_out unchanged within 2s

  note "Before the run, configure the machine for remote hand pay reset and host-controlled reset to the credit meter, with a jackpot limit below fifteen hundred dollars, an upper limit above it for reset to the credit meter, and a high credit limit. The amounts are in cents. Meter 0003 must not move in any round; this build cannot read it by name."
  note "The document's second round, with no $A8 sent, gives the $94 reply code as 01 while also saying the hand pay clears onto the jackpot meter. Those two cannot both hold, so the code is not asserted there; only the meters are, which is what the round is about."
  note "Not done: the last round, a jackpot above the upper limit for reset to the credit meter, where reset_id is 00, $A8 asking for the credit meter is answered with code 01, and $94 clears the hand pay onto the jackpot meter. The platform has no such limit the operator can set, so it has no step in the language."
}
