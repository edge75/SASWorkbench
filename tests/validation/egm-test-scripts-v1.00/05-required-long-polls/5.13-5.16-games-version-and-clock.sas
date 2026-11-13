# Chapter 5, sections 5.13 to 5.16 - how many games, which protocol revision, and the clock.
#
# Four short replies about the machine itself. Each reply is checked against the bounds its
# fields allow; whether the value is the right one for this machine - the number of games it was
# built with, the version it was certified to, the time on its own display - is a comparison with
# the machine and is left to a person in a note.

test "5.13 the number of games implemented" {
  tag @egm-test-scripts @chapter-5 @wire-only
  requires decoder.lp51

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 51

  note "The right number depends on how the machine is built: 0 for a single paytable with no multi-game polls, 1 for a single paytable that supports them, and the number of paytables an attendant can choose between otherwise. Ask the manufacturer if in doubt."
  note "Power cycle the machine and read it again: the same number. This build cannot power cycle the software machine, so that half is a person's."
}

test "5.14 the protocol version and serial number" {
  tag @egm-test-scripts @chapter-5 @wire-only
  requires decoder.lp54

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # Three version characters and up to forty serial characters, counted by the length byte.
  host send lp 54
  host expect field length of lp 54 >= 3
  host expect field length of lp 54 <= $2B

  note "The version is three characters with the decimal point dropped, and must be a published revision - 602 for 6.02. With a length of exactly 3 there is no serial number and the check sequence follows the version."
}

test "5.15 the current date and time" {
  tag @egm-test-scripts @chapter-5 @wire-only
  requires decoder.lp7E

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 7E
  host expect field month of lp 7E >= 1
  host expect field month of lp 7E <= 12
  host expect field day of lp 7E >= 1
  host expect field day of lp 7E <= 31
  host expect field hour of lp 7E <= 23
  host expect field minute of lp 7E <= 59
  host expect field second of lp 7E <= 59

  note "The date and time match what the machine shows on its own meter and configuration screens."
}

# Setting the clock leaves the machine's clock changed, hence destructive.
test "5.16 the host sets the date and time" {
  tag @egm-test-scripts @chapter-5 @wire-only @destructive
  requires decoder.lp7F, decoder.lp7E

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # Addressed to the machine, the clock change is acknowledged; read back, it holds what was sent.
  # The date is June 15 2031 and the time 10:30:00.
  host send lp 7F data 06152031103000
  host send lp 7E
  host expect field month of lp 7E == 6
  host expect field day of lp 7E == 15
  host expect field year of lp 7E == 2031
  host expect field hour of lp 7E == 10
  host expect field minute of lp 7E <= 31

  note "The document first sends the same command as a broadcast to address 00, which the machine must not answer and must still obey; this build cannot broadcast that command. The machine's own configuration screen then shows the time sent plus the time since."
  note "A copy of the command with a broken check sequence is ignored or refused; this build cannot send one. Set the clock back to the right time when the case is done."
}
