| Code | Description | LEN | TRN | ACK | TYP | STS | CRC | #Box | Input boxes (`prompt` @bytes) |
|---|---|---|---|---|---|---|---|---|---|
| `$1D` | Cumulative meters (credits) | n | n | n | n | n | n | 0 | - |
| `$22` | Load non-cashable money (dollars) | n | y | y | n | n | y | 1 | `Enter how many dollars` @4 |
| `$23` | Load promotional money (dollars) | n | y | y | n | n | y | 1 | `Enter how many dollars` @4 |
| `$24` | Force cash out to host (dollars) | n | y | y | n | n | y | 0 | - |
| `$25` | Force cash out from gaming machine (dollars) | n | y | y | n | n | y | 0 | - |
| `$26` | Request for cash out dollar amount | n | n | y | n | n | y | 0 | - |
| `$27` | Request for current promotional credits | n | n | n | n | n | n | 0 | - |
| `$28` | Request EFT transaction log | n | n | n | n | n | n | 0 | - |
| `$29` | Load cashable money (dollars) | n | y | y | n | n | y | 1 | `Enter how many dollars` @4 |
| `$62` | Load non-cashable credits | n | y | y | n | n | y | 1 | `Enter how many credits` @4 |
| `$63` | Load promotional credits | n | y | y | n | n | y | 1 | `Enter how many credits` @4 |
| `$64` | Force cash out to host (credits) | n | y | y | n | n | y | 0 | - |
| `$65` | Force cash out from gaming machine (credits) | n | y | y | n | n | y | 0 | - |
| `$66` | Request for cash out credit amount | n | n | y | n | n | y | 0 | - |
| `$67` | Cash out win to host | n | y | y | n | n | y | 0 | - |
| `$69` | Load cashable credits | n | y | y | n | n | y | 1 | `Enter how many credits` @4 |
| `$6A` | Request available EFT transfers | n | n | n | n | n | n | 0 | - |
| `$6B` | Force promotional cash out to host | n | y | y | n | n | y | 0 | - |
