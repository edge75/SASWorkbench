# Reads back what earlier runs left for a host on the HAL build of the demo: the handpay
# information behind exception 51, the ticket status behind 68, and every record in the
# validation buffer behind 3D. While a validation record stays unread the machine pays every
# cash-out by handpay, as SAS 6.02 section 15 requires, so run this when a cash-out that should
# print does not. Each read is followed by another poll, which is what acknowledges it.

test "read back what earlier runs left for a host" {
  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # Each read hands over one handpay record, and several can be queued.
  host send lp 1B
  host send lp 1A
  host send lp 1B
  host send lp 1A
  host send lp 1B
  host send lp 1A
  host send lp 1B
  host send lp 1A
  host send lp 1B
  host send lp 1A
  host send lp 1B
  host send lp 1A
  host send lp 1B
  host send lp 1A
  host send lp 1B
  host send lp 1A
  host send lp 71 with transfer_code=ttc_request_for_current_ticket_status
  host send lp 1A

  # The buffer holds up to 31 records.
  host send lp 4D with function=0
  host send lp 1A
  host send lp 4D with function=0
  host send lp 1A
  host send lp 4D with function=0
  host send lp 1A
  host send lp 4D with function=0
  host send lp 1A
  host send lp 4D with function=0
  host send lp 1A
  host send lp 4D with function=0
  host send lp 1A
  host send lp 4D with function=0
  host send lp 1A
  host send lp 4D with function=0
  host send lp 1A
  host send lp 4D with function=0
  host send lp 1A
  host send lp 4D with function=0
  host send lp 1A
  host send lp 4D with function=0
  host send lp 1A
  host send lp 4D with function=0
  host send lp 1A
  host send lp 4D with function=0
  host send lp 1A
  host send lp 4D with function=0
  host send lp 1A
  host send lp 4D with function=0
  host send lp 1A
  host send lp 4D with function=0
  host send lp 1A
  host send lp 4D with function=0
  host send lp 1A
  host send lp 4D with function=0
  host send lp 1A
  host send lp 4D with function=0
  host send lp 1A
  host send lp 4D with function=0
  host send lp 1A
  host send lp 4D with function=0
  host send lp 1A
  host send lp 4D with function=0
  host send lp 1A
  host send lp 4D with function=0
  host send lp 1A
  host send lp 4D with function=0
  host send lp 1A
  host send lp 4D with function=0
  host send lp 1A
  host send lp 4D with function=0
  host send lp 1A
  host send lp 4D with function=0
  host send lp 1A
  host send lp 4D with function=0
  host send lp 1A
  host send lp 4D with function=0
  host send lp 1A
  host send lp 4D with function=0
  host send lp 1A
  host send lp 4D with function=0
  host send lp 1A

  host forget exceptions
  host expect no exception $51 within 5s
}
