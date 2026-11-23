# Chapter 10, section 10.6 - system validation.
#
# The third style, and the only one where the machine asks permission per ticket: it holds the
# cashout, tells the host what it wants to print, and waits for a number to come back before any
# paper moves. A host that never answers leaves the machine holding the money, which is what makes
# the timeout behaviour in this section matter as much as the happy path.
#
# Every case declares the style, so the software machine is built performing it and any other
# machine is asked and skips when it performs another. The exchange is $57 read, then $58 answered,
# within the ten seconds the machine waits; every case that prints reads its record back with $4D.

test "10.6.1 the host configures system validation" {
  tag @egm-test-scripts @chapter-10
  machine validation system
  requires decoder.lpA0, decoder.lp4C, host.validation_sequence

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # The style is set at the machine; what the host can check is that the machine reports it.
  host send lp A0 data 0000
  host expect field validation_style of lp A0 == 1
  host expect field validation_extensions of lp A0 == 1

  # The identifier poll belongs to secure enhanced validation, so a system machine leaves it unanswered.
  host expect no reply to lp 4C data 010000010000

  note "The document's configuration step is the machine's own: no long poll puts a machine into system validation, so the case checks the style the machine reports rather than acknowledging a poll that does not exist."
}

test "10.6.2 a cashout raises the system validation request" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  machine validation system
  requires stimulus.bill, stimulus.button.collect, host.validation_sequence, printer.wallet, decoder.lp57, decoder.lp58

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s

  # Nothing is printed while the machine waits for its number.
  host wait 2s
  host send lp 57
  host expect field cashout_type of lp 57 == 0

  # Answered, so the case leaves no money held and no record unread.
  host send lp 58 with validation_system_id=1 validation_number=1001
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  note "That no paper moved before the answer is read from the wallet in the run record: the ticket printed is the one stamped with the host's number, and a script cannot yet assert that the wallet stayed empty."
}

test "10.6.3 the pending cashout information poll says what is waiting" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  machine validation system
  requires decoder.lp57, stimulus.bill, stimulus.button.collect, host.validation_sequence, decoder.lp58, printer.wallet

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  # Before any cash-out the machine is waiting for nothing.
  host send lp 57
  host expect field cashout_type of lp 57 == 128
  host expect field amount of lp 57 == 0

  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host expect field cashout_type of lp 57 == 0
  host expect field amount of lp 57 == 2000

  host send lp 57
  host send lp 58 with validation_system_id=1 validation_number=1002
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number
}

test "10.6.4 the host answers with a validation number" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  machine validation system
  requires decoder.lp58, decoder.lp57, stimulus.bill, stimulus.button.collect, printer.wallet

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  # With no cash-out waiting, a number is refused as not in a cash-out.
  host send lp 58 with validation_system_id=1 validation_number=1003
  host expect field status of lp 58 == 128

  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=1 validation_number=1003
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == 1003
  host expect field validation_number of lp 4D == ticket.validation_number
}

test "10.6.5 hand pays are not validated under this style" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  machine validation system
  requires stimulus.handpay, stimulus.handpay.reset, decoder.lp1B

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  egm handpay 2000
  host expect exception handpay_is_pending within 10s
  host expect no exception system_validation_request within 5s

  # Paid and reset, so the next case finds a playable machine.
  host send lp 1B
  host expect field amount of lp 1B != 0
  host wait 1s
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host expect no exception system_validation_request within 5s
}

test "10.6.6 the printed ticket raises its exception" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  machine validation system
  requires decoder.lp58, decoder.lp57, stimulus.bill, stimulus.button.collect, printer.wallet

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=1 validation_number=1006
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s
  host expect exception cashout_ticket_printed within 5s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number
}

test "10.6.7 the cashout ticket information poll carries the host's number" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  machine validation system
  requires decoder.lp3D, decoder.lp58, decoder.lp57, decoder.lp4D, stimulus.bill, stimulus.button.collect, printer.wallet

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=1 validation_number=1007
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s

  # The document lets the machine answer $3D or ignore it in this style; this machine ignores it, as
  # section 15.5 reads, and the host's number is read back where the style keeps it.
  host expect no reply to lp 3D
  host send lp 4D data 00
  host expect field validation_number of lp 4D == 1007
  host expect field validation_number of lp 4D == ticket.validation_number
}

test "10.6.8 the enhanced validation information poll reads the record back" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  machine validation system
  requires decoder.lp4D, decoder.lp57, decoder.lp58, host.validation_sequence, stimulus.bill, stimulus.button.collect, printer.wallet

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=3 validation_number=1008
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s

  host send lp 4D data FF
  host expect field validation_type of lp 4D == 0
  host expect field validation_number of lp 4D == ticket.validation_number
  host expect field amount of lp 4D == ticket.amount_cents
  host expect field ticket_number of lp 4D == ticket.ticket_number
  host expect field validation_system_id of lp 4D == 3
  host expect field pool_id of lp 4D == 0
  host wait 1s

  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number
  host wait 1s

  # Read and acknowledged, so nothing is left.
  host send lp 4D data 00
  host expect field validation_number of lp 4D == 0
}

test "10.6.9 the validation type field names the style" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  machine validation system
  requires decoder.lp4D, decoder.lp57, decoder.lp58, host.validation_sequence, stimulus.bill, stimulus.button.collect, printer.wallet

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=7 validation_number=1009
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s

  # A cash-out ticket is type 0 in every style; the system ID is what a secure enhanced record has as 0.
  host send lp 4D data 00
  host expect field validation_type of lp 4D == 0
  host expect field validation_system_id of lp 4D == 7
}

test "10.6.10 the buffer is circular under this style as well" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  machine validation system
  requires machine.buffer_depth, stimulus.bill, stimulus.button.collect, decoder.lp4D, decoder.lp57, decoder.lp58, printer.wallet

  # Thirty-two tickets through a buffer of thirty-one, each read as it prints so the printer stays the
  # cash-out device; the thirty-second takes the slot the first was in.
  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  # Ticket 1.
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=1 validation_number=2001
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 2.
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=1 validation_number=2002
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 3.
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=1 validation_number=2003
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 4.
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=1 validation_number=2004
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 5.
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=1 validation_number=2005
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 6.
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=1 validation_number=2006
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 7.
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=1 validation_number=2007
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 8.
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=1 validation_number=2008
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 9.
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=1 validation_number=2009
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 10.
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=1 validation_number=2010
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 11.
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=1 validation_number=2011
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 12.
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=1 validation_number=2012
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 13.
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=1 validation_number=2013
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 14.
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=1 validation_number=2014
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 15.
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=1 validation_number=2015
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 16.
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=1 validation_number=2016
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 17.
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=1 validation_number=2017
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 18.
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=1 validation_number=2018
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 19.
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=1 validation_number=2019
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 20.
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=1 validation_number=2020
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 21.
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=1 validation_number=2021
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 22.
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=1 validation_number=2022
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 23.
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=1 validation_number=2023
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 24.
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=1 validation_number=2024
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 25.
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=1 validation_number=2025
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 26.
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=1 validation_number=2026
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 27.
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=1 validation_number=2027
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 28.
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=1 validation_number=2028
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 29.
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=1 validation_number=2029
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 30.
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=1 validation_number=2030
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 31.
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=1 validation_number=2031
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 32.
  egm bill insert 2000
  egm press collect
  host expect exception system_validation_request within 5s
  host send lp 57
  host send lp 58 with validation_system_id=1 validation_number=2032
  host expect field status of lp 58 == 0
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # The newest in slot 1, where the oldest was; the second still in slot 2; the thirty-first in the last.
  host send lp 4D data 01
  host expect field validation_number of lp 4D == 2032
  host send lp 4D data 02
  host expect field validation_number of lp 4D == 2002
  host send lp 4D data 1F
  host expect field validation_number of lp 4D == 2031

  note "Assumes a buffer of thirty-one records starting at index 1, the machine library's; a machine with another depth wraps at another ticket."
}
