# Chapter 16, sections 16.82 to 16.86 - the lock a transfer can demand, and the history it leaves.
#
# A host can insist that a transfer only happen while it holds the machine locked, so the lock and
# status command and the transfer command have to agree: unlocked, such a transfer fails; locked
# for the right kind of transfer and inside the timeout, it succeeds. Every completed transfer
# then lands in a numbered history the host can read back by position, which has a fixed depth,
# wraps when full, holds nothing a refusal produced, and is empty after a memory clear.
#
# The lock and status command is decoded; the transfer command is not, and every case here needs
# both, so none can run yet.

test "16.82 a transfer that needs the machine locked" {
  tag @egm-test-scripts @chapter-16 @needs-a-person
  requires decoder.lp72, machine.aft
  requires decoder.lp74

  note "On an empty machine, a full cashable transfer in with flag 6 (lock required) set fails while unlocked: $69 follows, and the status read shows not locked ($94) and nothing moved."
  note "Ask for the lock for transfers in and out with a long timeout: the reply shows the lock pending, then an interrogation shows it held, and $6F repeats about every five seconds while it is. The same transfer with a new number now succeeds ($00, 500) and the status reports that the host required a lock."
  note "Repeat with the lock taken for one kind of transfer only and a transfer of that kind - out to the host, to a ticket, into the machine - each succeeding with the lock flag shown."
  note "With the lock timeout set to twenty seconds, and then to three, the locked transfer still succeeds as long as it is sent inside the time."
}

test "16.83 a refused transfer takes no place in the history" {
  tag @egm-test-scripts @chapter-16 @needs-a-person
  requires decoder.lp72, machine.aft

  note "On an empty machine a plain full transfer of five dollars succeeds; note its position in the history."
  note "Empty the machine again; a transfer with the lock flag set and a new number fails. Both the peek at the current record and the status read show not locked ($94), position 0 and nothing moved, and $69 repeats about every fifteen seconds until read."
  note "Reading the history at the next position after the success answers either that nothing is there ($FF) or an earlier success with a non-zero amount; it is never the refusal."
  note "A full transfer of nothing at all is answered complete or pending; the peek and the status read then show $00, position 0 and nothing moved, and the same read of the history gives the same answer as before."
}

test "16.84 the current record before and after a memory clear" {
  tag @egm-test-scripts @chapter-16 @needs-a-person @destructive
  requires decoder.lp72, machine.aft
  requires machine.nvram_clear

  note "On an empty machine, a transfer needing a lock fails unlocked: the status read shows $94, position 0, nothing moved, and reading it again gives the same."
  note "Clear the machine's memory so its history is empty and set it up for transfers again. The status read now shows position 0, transfer status $FF and receipt status $FF."
}

test "16.85 the history fills and wraps" {
  tag @egm-test-scripts @chapter-16
  requires decoder.lp72, machine.aft
  requires decoder.lp74, host.compare_replies

  note "Interrogate the lock and status and remember the highest history index it reports."
  note "Alternate a transfer of one accounting unit in with one out until the position reaches that index: each succeeds and the position grows by one. One more lands at position 1."
  note "Reading the history at position 1 shows that last transfer; reading it at the highest index shows the right position, a success ($00 or $01) and receipt status $FF."
}

test "16.86 a machine with no transfers yet" {
  tag @egm-test-scripts @chapter-16 @needs-a-person @destructive
  requires decoder.lp72, machine.aft
  requires machine.nvram_clear

  note "Clear the machine's memory so no transfer has ever completed, and read the status: position 0, transfer status $FF and receipt status $FF."
}
