//	SasWorkbench - qxcom-tcp
//	A 9-bit SAS symbol link carried over a TCP socket, standing in for the QXCOM
//	hardware port. See ../README.md for the wire format and the environment
//	variables that configure it.

#pragma once

#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdint>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

namespace saswb
{

//	One SAS symbol as it travels on the socket: little-endian uint16, bit 8 = wakeup.
using Symbol = std::uint16_t;

constexpr Symbol kWakeupMask = 0x0100;
constexpr Symbol kPayloadMask = 0x00FF;

enum class LinkMode
{
	Connect,	//	dial the peer (the usual case: the host listens, the EGM dials)
	Listen		//	accept a single peer
};

struct LinkConfig
{
	std::string host = "127.0.0.1";
	std::uint16_t port = 9760;
	LinkMode mode = LinkMode::Connect;
	std::chrono::milliseconds pollTimeout{2};
	std::string logPath;			//	empty = no trace, "-" = stderr
};

//	Reads the configuration for one port name out of the environment.
LinkConfig ConfigFromEnvironment(const std::string &portName);

//	A single point-to-point symbol link.
//
//	Opening never fails on an absent peer: an unconnected link behaves like an open
//	serial port with nothing plugged in, and a background thread keeps trying to
//	establish the connection. That lets the EGM and the host start in either order.
class TcpLink
{
public:
	explicit TcpLink(const LinkConfig &config, std::string portName);
	~TcpLink();

	TcpLink(const TcpLink &) = delete;
	TcpLink &operator=(const TcpLink &) = delete;

	//	Blocks up to the configured poll timeout for the first symbol, then returns
	//	whatever is buffered, up to maxSymbols. Never blocks once data is available.
	std::size_t Read(Symbol *out, std::size_t maxSymbols, std::uint64_t *timestampsOut);

	//	Sends every symbol or reports failure. Returns false on a transport error.
	bool Write(const Symbol *symbols, std::size_t count, std::size_t *written);

	void FlushReceive();
	void FlushTransmit();
	void CancelReceive();

	bool IsConnected() const { return fConnected.load(std::memory_order_acquire); }
	std::uint64_t TimestampNow() const;

	const std::string &PortName() const { return fPortName; }

	//	Serial parameters the caller asked for. Stored and reported back verbatim; the
	//	transport itself is always 9-bit, and Configure() refuses anything narrower.
	struct State
	{
		std::uint32_t baudRate = 19200;
		std::uint8_t dataBits = 9;
		std::uint16_t parity = 0;
		std::uint8_t stopBits = 1;
		std::uint8_t enableTimestamps = 0;
	};

	struct Timeouts
	{
		long long intervalTimeout = 5;
		long long readTimeout = 1500;
		long long writeTimeout = 1500;
	};

	bool Configure(const State &state);
	State GetState() const;
	void SetTimeouts(const Timeouts &timeouts);
	Timeouts GetTimeouts() const;

	void Trace(const char *format, ...);

private:
	void ConnectorLoop();
	void ReceiverLoop();
	void CloseSocket();

	LinkConfig fConfig;
	std::string fPortName;

	mutable std::mutex fStateMutex;
	State fState;
	Timeouts fTimeouts;

	std::atomic<bool> fRunning{true};
	std::atomic<bool> fConnected{false};
	std::atomic<bool> fCancelReceive{false};

	//	Guards fSocket for the connector/receiver/writer threads.
	std::mutex fSocketMutex;
	std::intptr_t fSocket = -1;

	std::mutex fRxMutex;
	std::condition_variable fRxSignal;
	std::vector<Symbol> fRxBuffer;
	std::vector<std::uint64_t> fRxTimestamps;

	std::thread fConnector;
	std::thread fReceiver;

	std::mutex fLogMutex;
	void *fLogFile = nullptr;
	std::chrono::steady_clock::time_point fEpoch;
};

}	// namespace saswb
