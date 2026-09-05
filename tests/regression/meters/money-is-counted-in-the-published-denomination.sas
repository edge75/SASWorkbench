# The unit a machine publishes in its identification reply is the unit its credit meters are
# counted in. A twenty-dollar note is two thousand cents, which at five cents to the unit is
# four hundred - the value the real machine was measured reporting.
#
# The same twenty moves the bill-value meter by twenty, because that meter is in whole currency
# units and does not follow the accounting denomination. Asserting both in one test is the point:
# one unit is fixed and one is not, and a build that confuses them passes half of this.
#
# Against a real machine the two preconditions of note-moves-bill-meters.sas apply here too.

test "money is counted in the denomination the machine publishes" {
  tag @needs-stimulus
  requires decoder.lp1F
  requires stimulus.bill

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  host send lp 1F
  host expect field denomination == 2

  egm settle
  host forget exceptions

  egm bill insert 2000
  host expect exception bill_accepted_20 within 5s
  host expect meter credits += 400 within 3s
  host expect meter total_bill_in += 20 within 3s
}
