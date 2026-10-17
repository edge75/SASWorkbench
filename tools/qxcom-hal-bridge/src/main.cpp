//	SasWorkbench - qxcom-hal-bridge
//
//	Puts SasWorkbench on the SAS link of an unmodified HAL build of the demo. A HAL build speaks SAS
//	on one of libqxcom's virtual serial ports, a cable in shared memory with two ends; this program
//	opens the other end and relays every unit, wakeup bit included, to SasWorkbench over the TCP
//	format qxcom-tcp already uses. The machine sees a serial cable and SasWorkbench sees a TCP peer,
//	so neither needs a special build.
//
//	usage: qxcom-hal-bridge [port] [host:port]
//	       port       the machine's "Network 1" serial port, default /dev/ttyS5
//	       host:port  where SasWorkbench listens, default 127.0.0.1:9760

#include <atomic>
#include <chrono>
#include <csignal>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <string>
#include <thread>
#include <vector>

#include "libqxcom.h"
#include "tcp_link.h"

namespace
{

std::atomic<bool> sStop{false};

void OnSignal(int)
{
	sStop.store(true);
}

bool SplitEndpoint(const std::string &text, std::string &host, std::uint16_t &port)
{
	const size_t colon = text.rfind(':');
	if (colon == std::string::npos || colon == 0 || colon + 1 >= text.size())
		return false;

	const long parsed = std::strtol(text.c_str() + colon + 1, nullptr, 10);
	if (parsed <= 0 || parsed > 65535)
		return false;

	host = text.substr(0, colon);
	port = static_cast<std::uint16_t>(parsed);
	return true;
}

//	The machine to SasWorkbench. A read waits a few milliseconds for the first unit, so the loop
//	notices a stop request without spinning.
void MachineToHost(QXCOM_HANDLE port, saswb::TcpLink &link, std::atomic<unsigned long long> &relayed)
{
	std::vector<QXCOM_PACKET> packets(256);
	std::vector<saswb::Symbol> symbols(256);
	while (!sStop.load())
	{
		unsigned int read = 0;
		const QRESULT result = qxComRead(port, packets.data(), static_cast<unsigned int>(packets.size()), &read);
		if (read == 0)
		{
			//	A read that timed out has already waited; one that failed outright would spin.
			if (!QXT_SUCCESS(result) && result != Q_QXCOM_READ_W_CANCELLED)
				std::this_thread::sleep_for(std::chrono::milliseconds(10));
			continue;
		}

		for (unsigned int i = 0; i < read; i++)
			symbols[i] = static_cast<saswb::Symbol>(packets[i].Data.Raw & (saswb::kWakeupMask | saswb::kPayloadMask));

		//	Nobody listening yet is a serial cable with nothing on the far end: the units are lost,
		//	which is what the machine would see on a real one.
		std::size_t written = 0;
		if (link.IsConnected())
			link.Write(symbols.data(), read, &written);
		relayed += written;
	}
}

//	SasWorkbench to the machine.
void HostToMachine(QXCOM_HANDLE port, saswb::TcpLink &link, std::atomic<unsigned long long> &relayed)
{
	std::vector<saswb::Symbol> symbols(256);
	std::vector<QXCOM_PACKET> packets(256);
	while (!sStop.load())
	{
		const std::size_t read = link.Read(symbols.data(), symbols.size(), nullptr);
		if (read == 0)
			continue;

		for (std::size_t i = 0; i < read; i++)
		{
			packets[i] = {};
			packets[i].Data.Raw = symbols[i] & (saswb::kWakeupMask | saswb::kPayloadMask);
		}

		unsigned int written = 0;
		qxComWrite(port, packets.data(), static_cast<unsigned int>(read), &written);
		if (written < read)
			std::fprintf(stderr, "qxcom-hal-bridge: the machine's port took %u of %zu units\n", written, read);
		relayed += written;
	}
}

}	//	namespace

int main(int argc, char **argv)
{
	std::string portName = argc > 1 ? argv[1] : "/dev/ttyS5";
	saswb::LinkConfig config;
	config.mode = saswb::LinkMode::Connect;
	if (argc > 2 && !SplitEndpoint(argv[2], config.host, config.port))
	{
		std::fprintf(stderr, "qxcom-hal-bridge: '%s' is not host:port\n", argv[2]);
		return 2;
	}

	QXCOM_HANDLE port = nullptr;
	if (!QXT_SUCCESS(qxComOpen(&port, portName.data())))
	{
		std::fprintf(stderr, "qxcom-hal-bridge: cannot open the virtual port %s\n", portName.c_str());
		return 2;
	}

	//	Short read timeouts keep both loops responsive; a SAS reply is due within 20 ms.
	QXCOM_TIMEOUTS timeouts = {2, 5, 200};
	qxComSetCommTimeouts(port, timeouts);

	saswb::TcpLink link(config, portName);
	std::signal(SIGINT, OnSignal);
	std::signal(SIGTERM, OnSignal);

	std::printf("qxcom-hal-bridge: machine port %s <-> SasWorkbench %s:%u (Ctrl+C to stop)\n",
		portName.c_str(), config.host.c_str(), static_cast<unsigned>(config.port));
	std::fflush(stdout);

	std::atomic<unsigned long long> toHost{0};
	std::atomic<unsigned long long> toMachine{0};
	std::thread up(MachineToHost, port, std::ref(link), std::ref(toHost));
	std::thread down(HostToMachine, port, std::ref(link), std::ref(toMachine));

	bool wasConnected = false;
	while (!sStop.load())
	{
		std::this_thread::sleep_for(std::chrono::milliseconds(500));
		const bool connected = link.IsConnected();
		if (connected != wasConnected)
		{
			std::printf("qxcom-hal-bridge: SasWorkbench %s\n", connected ? "connected" : "disconnected");
			std::fflush(stdout);
			wasConnected = connected;
		}
	}

	qxComFlush(port, QXCOM_FLAG_CANCEL_RX);
	link.CancelReceive();
	up.join();
	down.join();
	qxComClose(port);

	std::printf("qxcom-hal-bridge: relayed %llu units to SasWorkbench and %llu to the machine\n",
		toHost.load(), toMachine.load());
	return 0;
}
