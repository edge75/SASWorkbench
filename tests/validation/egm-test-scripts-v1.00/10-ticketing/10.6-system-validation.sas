# Chapter 10, section 10.6 - system validation.
#
# The third style, and the only one where the machine asks permission per ticket: it holds the
# cashout, tells the host what it wants to print, and waits for a number to come back before any
# paper moves. A host that never answers leaves the machine holding the money, which is what makes
# the timeout behaviour in this section matter as much as the happy path.
#
# Two things block the whole section here. The machine has to be performing this style, which is
# memory the host cannot write; and the validation-number response poll has no layout in this
# build, so the host cannot form the message that completes the exchange.

test "10.6.1 the host configures system validation" {
  tag @egm-test-scripts @chapter-10
  requires egm.validation == system
  requires decoder.lp4C, host.validation_sequence
  note "Send the configuration poll that puts the machine into system validation and check it is acknowledged."
}

test "10.6.2 a cashout raises the system validation request" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires egm.validation == system
  requires stimulus.bill, stimulus.button.collect, host.validation_sequence
  note "Money in, cashout pressed: the machine raises the system-validation-request exception and prints nothing yet. The absence of paper at this point is half the case."
}

test "10.6.3 the pending cashout information poll says what is waiting" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires egm.validation == system
  requires decoder.lp57, stimulus.button.collect, host.validation_sequence
  note "Read the pending cashout poll after the request: it carries the kind of ticket and the amount the machine is asking to print."
}

test "10.6.4 the host answers with a validation number" {
  tag @egm-test-scripts @chapter-10
  requires egm.validation == system
  requires decoder.lp58
  note "Send the validation-number response carrying the number and the ticket data, and the machine prints. This build has no layout for that poll, so the message cannot be formed at all."
}

test "10.6.5 hand pays are not validated under this style" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires egm.validation == system
  requires stimulus.handpay
  note "Force a hand pay and confirm no validation request is raised for it: system validation covers tickets and not hand pays."
}

test "10.6.6 the printed ticket raises its exception" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires egm.validation == system
  requires decoder.lp58, stimulus.button.collect, printer.wallet
  note "After the host answers, the ticket prints and the machine raises the ticket-printed exception as it does under every other style."
}

test "10.6.7 the cashout ticket information poll carries the host's number" {
  tag @egm-test-scripts @chapter-10
  requires egm.validation == system
  requires decoder.lp3D, decoder.lp58
  note "Read the ticket back: the number on it is the one the host sent, not one the machine chose."
}

test "10.6.8 the enhanced validation information poll reads the record back" {
  tag @egm-test-scripts @chapter-10
  requires egm.validation == system
  requires decoder.lp4D, host.validation_sequence
  note "The buffer is kept under this style too, and every field of a record matches the ticket."
}

test "10.6.9 the validation type field names the style" {
  tag @egm-test-scripts @chapter-10
  requires egm.validation == system
  requires decoder.lp4D, host.validation_sequence
  note "A record made under system validation says so, and is distinguishable from one made under secure enhanced."
}

test "10.6.10 the buffer is circular under this style as well" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires egm.validation == system
  requires machine.buffer_depth, stimulus.button.collect
  note "Print past the end of the buffer and read it back: newest kept, oldest gone."
}
