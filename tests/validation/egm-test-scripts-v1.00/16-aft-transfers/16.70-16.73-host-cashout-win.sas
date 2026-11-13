# Chapter 16, sections 16.70 to 16.73 - the host taking a win the machine cannot keep.
#
# The same arrangement as the cashout button, triggered instead by a win that would take the
# credit meter over its limit. The machine raises $6B and offers the host the win as a transfer;
# unanswered, or with the feature off, it falls back to paying it the usual way, which on a win over
# the limit means a hand pay.
#
# Producing a win over the credit limit needs a person at the machine's configuration, and the
# transfer itself needs the funds-transfer command decoded, so none of these run here.

test "16.70 a win over the credit limit asks the host first" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus @needs-a-person
  requires decoder.lp72, machine.aft
  requires machine.credit_limit, machine.transfer_limit, stimulus.play, host.timing

  note "Set the credit limit low enough that one win goes over it. Load ten cents of each type, then send a zero transfer to the host with flags 0, 1 and 2 set; both are pending or complete ($40 or $00)."
  note "Play to a win over the limit: $6B comes on a general poll and repeats about every 800 ms for eight seconds, then the machine pays the usual way."
  note "Send the pair again with bit 2 clear and the transfer limit below five dollars: a win over the credit limit raises no $6B. With the limit restored above a hundred dollars and the credits reloaded, the next such win raises $6B for eight seconds before the machine pays."
}

test "16.71 the host answers by taking the win" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus @needs-a-person
  requires decoder.lp72, machine.aft
  requires machine.credit_limit, stimulus.bill, stimulus.play

  note "Credit limit low, jackpot limit high. A partial win-to-host transfer of everything enables cashout to the host and is answered pending or complete; the status read shows $00."
  note "Play to a win over the limit and, within five seconds, send the same partial win-to-host transfer with a new transaction number: every credit moves to the host, the status shows a partial success ($01), and $6B stops."
  note "Win over the limit again and answer with a zero full transfer to the host: the status is $00, $6B stops, and the machine pays the usual way."
}

test "16.72 the win request goes ahead of the queue" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus @needs-a-person
  requires decoder.lp72, machine.aft
  requires machine.credit_limit, stimulus.bill, stimulus.play, stimulus.door.main, host.poll_sequence

  note "With only long polls running, credit limit low, jackpot limit high, credits on and some ordinary exceptions queued, enable cashout to the host. Win over the limit and start general polls within five seconds: $6B comes first and the queue after it, $6B keeps repeating, and the machine pays."
  note "Again, then stop the round right after the win and send a synchronisation poll, three polls to the machine, a long poll for its identity and one more poll: the last one is answered with $6B again."
}

test "16.73 with cashout to the host off, a big win is a hand pay" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus @needs-a-person
  requires decoder.lp72, machine.aft
  requires machine.credit_limit, stimulus.bill, stimulus.play

  note "Send a zero transfer to the host with both host-cashout flags clear; the status reports cashout to the host disabled. With the credit limit low and the jackpot limit high, play to a win over the credit limit: no $6B, and the machine locks up for a hand pay."
}
