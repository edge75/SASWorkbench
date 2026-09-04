//	SasWorkbench - qxcom-tcp round-trip check.
//
//	Drives the transport exactly the way libsas does - assignCallbacks, then open,
//	setState, setTimeouts, writeExtended, readExtended - against a peer that echoes
//	symbols back. Proves the wire format and the 9-bit path without needing the whole
//	QSP build.
//
//	Usage: link_roundtrip <host:port>     (the peer must already be listening)

#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <string>
#include <thread>
#include <vector>

#include "libqxcom_callbacks.h"

namespace
{

int failures = 0;

void Check(bool condition, const char *what)
{
	std::printf("%-58s %s\n", what, condition ? "ok" : "FAILED");
	if (!condition)
		++failures;
}

}	// namespace

int main(int argc, char **argv)
{
	if (argc > 1)
	{
#if defined(_WIN32)
		_putenv_s("SASWB_QXCOM_ENDPOINT", argv[1]);
#else
		setenv("SASWB_QXCOM_ENDPOINT", argv[1], 1);
#endif
	}

	QUIXANT_SERIAL_CALLBACKS callbacks;
	std::memset(&callbacks, 0, sizeof(callbacks));
	callbacks.callbacksVersion = CALLBACKS_VERSION;
	assignCallbacks(&callbacks);

	Check(callbacks.open != NULL, "assignCallbacks fills open");
	Check(callbacks.readExtended != NULL, "assignCallbacks fills readExtended");
	Check(callbacks.writeExtended != NULL, "assignCallbacks fills writeExtended");
	if (failures)
		return 1;

	long long handle = 0;
	Check(callbacks.open((char *)"COM1", &handle) == 0, "open succeeds");
	Check(handle != 0, "open yields a non-zero handle");

	//	The parameters libsas asks for: 19200 baud, 9 data bits, no parity, one stop bit.
	Check(callbacks.setState(handle, 19200, 9, 0, 1, 0) == 0, "setState 19200/9N1 accepted");
	Check(callbacks.setState(handle, 19200, 8, 0, 1, 0) != 0, "setState with 8 data bits refused");
	Check(callbacks.setTimeouts(handle, 9, 5000, 1500) == 0, "setTimeouts accepted");

	//	Give the connector thread a moment to reach the peer.
	std::this_thread::sleep_for(std::chrono::milliseconds(400));

	//	A general poll for address 1 followed by long poll 1F: the wakeup bit is set on
	//	the address byte and on nothing else.
	const unsigned short outgoing[3] = {0x181, 0x101, 0x01F};
	unsigned int written = 0;
	Check(callbacks.writeExtended(handle, outgoing, 3, &written) == 0, "writeExtended succeeds");
	Check(written == 3, "writeExtended reports every symbol sent");

	std::vector<unsigned short> received;
	const std::chrono::steady_clock::time_point deadline =
		std::chrono::steady_clock::now() + std::chrono::seconds(3);
	while (received.size() < 3 && std::chrono::steady_clock::now() < deadline)
	{
		unsigned short chunk[8] = {0};
		unsigned int count = 0;
		if (callbacks.readExtended(handle, chunk, 8, NULL, &count) != 0)
			break;
		for (unsigned int i = 0; i < count; ++i)
			received.push_back(chunk[i]);
	}

	Check(received.size() == 3, "readExtended returns three symbols");
	if (received.size() == 3)
	{
		Check(received[0] == 0x181, "symbol 0 round-trips with the wakeup bit set");
		Check(received[1] == 0x101, "symbol 1 round-trips with the wakeup bit set");
		Check(received[2] == 0x01F, "symbol 2 round-trips with the wakeup bit clear");
	}

	Check(callbacks.close(handle) == 0, "close succeeds");
	callbacks.cleanup();

	std::printf("\n%s\n", failures ? "FAILED" : "all checks passed");
	return failures ? 1 : 0;
}
