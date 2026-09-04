# Placed here so the conformance tree grows in the shape the corpus has, and so this
# milestone's honesty is visible: the requirement below is not met by any environment this
# build can assemble, so the test reports as skipped with that reason rather than passing.
#
# What it needs is the validation configuration commands, which arrive with the ticket work.

test "10.1.1 standard validation configuration" {
  tag @needs-stimulus

  requires egm.validation == standard
  requires decoder.lp4C

  host connect tcp 127.0.0.1:7200 address 01
  host poll every 200ms
  host expect link up within 10s

  host send lp 4C
  host expect field validation_type of lp 4C == 0
}
