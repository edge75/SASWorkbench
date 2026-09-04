//	SasWorkbench - qxcom-tcp: the TCP symbol link.

#include "tcp_link.h"

#include <cctype>
#include <cerrno>
#include <cstdarg>
#include <cstdio>
#include <cstdlib>
#include <cstring>

#if defined(_WIN32)
	#include <winsock2.h>
	#include <ws2tcpip.h>
	using socket_t = SOCKET;
	static const socket_t kInvalidSocket = INVALID_SOCKET;
#else
	#include <arpa/inet.h>
	#include <fcntl.h>
	#include <netdb.h>
	#include <netinet/in.h>
	#include <netinet/tcp.h>
	#include <sys/select.h>
	#include <sys/socket.h>
	#include <unistd.h>
	using socket_t = int;
	static const socket_t kInvalidSocket = -1;
#endif

namespace saswb
{
namespace
{

#if defined(_WIN32)
//	Winsock needs a process-wide startup, and nothing in this process guarantees one.
struct WinsockGuard
{
	WinsockGuard()
	{
		WSADATA data;
		std::memset(&data, 0, sizeof(data));
		WSAStartup(MAKEWORD(2, 2), &data);
	}
	~WinsockGuard() { WSACleanup(); }
};
WinsockGuard &Winsock()
{
	static WinsockGuard guard;
	return guard;
}
void CloseNative(socket_t s) { ::closesocket(s); }
void SetNonBlocking(socket_t s)
{
	u_long mode = 1;
	::ioctlsocket(s, FIONBIO, &mode);
}
bool WouldBlock() { return WSAGetLastError() == WSAEWOULDBLOCK; }
#else
void CloseNative(socket_t s) { ::close(s); }
void SetNonBlocking(socket_t s)
{
	const int flags = ::fcntl(s, F_GETFL, 0);
	::fcntl(s, F_SETFL, flags | O_NONBLOCK);
}
bool WouldBlock() { return errno == EAGAIN || errno == EWOULDBLOCK || errno == EINTR; }
#endif

std::string EnvOrEmpty(const std::string &name)
{
	const char *value = std::getenv(name.c_str());
	return value ? std::string(value) : std::string();
}

//	COM3 stays COM3, /dev/ttyS1 becomes _DEV_TTYS1: a port name usable as a variable suffix.
std::string EnvSuffixFor(const std::string &portName)
{
	std::string suffix;
	suffix.reserve(portName.size());
	for (std::size_t i = 0; i < portName.size(); ++i)
	{
		const unsigned char c = static_cast<unsigned char>(portName[i]);
		suffix.push_back(std::isalnum(c) ? static_cast<char>(std::toupper(c)) : '_');
	}
	return suffix;
}

bool SplitEndpoint(const std::string &endpoint, std::string *host, std::uint16_t *port)
{
	const std::size_t colon = endpoint.rfind(':');
	if (colon == std::string::npos || colon + 1 >= endpoint.size())
		return false;
	const long parsed = std::strtol(endpoint.c_str() + colon + 1, NULL, 10);
	if (parsed <= 0 || parsed > 65535)
		return false;
	*host = endpoint.substr(0, colon);
	*port = static_cast<std::uint16_t>(parsed);
	return true;
}

}	// namespace

LinkConfig ConfigFromEnvironment(const std::string &portName)
{
	LinkConfig config;

	std::string endpoint = EnvOrEmpty("SASWB_QXCOM_ENDPOINT_" + EnvSuffixFor(portName));
	if (endpoint.empty())
		endpoint = EnvOrEmpty("SASWB_QXCOM_ENDPOINT");
	if (!endpoint.empty())
		SplitEndpoint(endpoint, &config.host, &config.port);

	if (EnvOrEmpty("SASWB_QXCOM_MODE") == "listen")
		config.mode = LinkMode::Listen;

	const std::string pollMs = EnvOrEmpty("SASWB_QXCOM_POLL_MS");
	if (!pollMs.empty())
	{
		const long parsed = std::strtol(pollMs.c_str(), NULL, 10);
		if (parsed >= 0 && parsed <= 1000)
			config.pollTimeout = std::chrono::milliseconds(parsed);
	}

	config.logPath = EnvOrEmpty("SASWB_QXCOM_LOG");
	return config;
}

TcpLink::TcpLink(const LinkConfig &config, std::string portName)
	: fConfig(config)
	, fPortName(portName)
	, fEpoch(std::chrono::steady_clock::now())
{
#if defined(_WIN32)
	Winsock();
#endif
	if (!fConfig.logPath.empty())
		fLogFile = (fConfig.logPath == "-") ? stderr : std::fopen(fConfig.logPath.c_str(), "a");

	Trace("open port=\"%s\" endpoint=%s:%u mode=%s", fPortName.c_str(), fConfig.host.c_str(),
		static_cast<unsigned>(fConfig.port), fConfig.mode == LinkMode::Listen ? "listen" : "connect");

	fConnector = std::thread(&TcpLink::ConnectorLoop, this);
	fReceiver = std::thread(&TcpLink::ReceiverLoop, this);
}

TcpLink::~TcpLink()
{
	fRunning.store(false, std::memory_order_release);
	CloseSocket();
	fRxSignal.notify_all();
	if (fConnector.joinable())
		fConnector.join();
	if (fReceiver.joinable())
		fReceiver.join();
	Trace("close port=\"%s\"", fPortName.c_str());
	if (fLogFile && fLogFile != stderr)
		std::fclose(static_cast<std::FILE *>(fLogFile));
}

void TcpLink::CloseSocket()
{
	std::lock_guard<std::mutex> lock(fSocketMutex);
	if (fSocket != -1)
	{
		CloseNative(static_cast<socket_t>(fSocket));
		fSocket = -1;
	}
	fConnected.store(false, std::memory_order_release);
}

void TcpLink::ConnectorLoop()
{
	socket_t listener = kInvalidSocket;

	while (fRunning.load(std::memory_order_acquire))
	{
		if (fConnected.load(std::memory_order_acquire))
		{
			std::this_thread::sleep_for(std::chrono::milliseconds(50));
			continue;
		}

		struct sockaddr_in address;
		std::memset(&address, 0, sizeof(address));
		address.sin_family = AF_INET;
		address.sin_port = htons(fConfig.port);
		::inet_pton(AF_INET, fConfig.host.c_str(), &address.sin_addr);

		socket_t peer = kInvalidSocket;

		if (fConfig.mode == LinkMode::Listen)
		{
			if (listener == kInvalidSocket)
			{
				listener = ::socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
				if (listener != kInvalidSocket)
				{
					int reuse = 1;
					::setsockopt(listener, SOL_SOCKET, SO_REUSEADDR,
						reinterpret_cast<const char *>(&reuse), sizeof(reuse));
					if (::bind(listener, reinterpret_cast<struct sockaddr *>(&address), sizeof(address)) != 0
						|| ::listen(listener, 1) != 0)
					{
						CloseNative(listener);
						listener = kInvalidSocket;
					}
				}
			}
			if (listener != kInvalidSocket)
			{
				fd_set readable;
				FD_ZERO(&readable);
				FD_SET(listener, &readable);
				struct timeval wait;
				wait.tv_sec = 0;
				wait.tv_usec = 200000;
				if (::select(static_cast<int>(listener) + 1, &readable, NULL, NULL, &wait) > 0)
					peer = ::accept(listener, NULL, NULL);
			}
		}
		else
		{
			peer = ::socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
			if (peer != kInvalidSocket
				&& ::connect(peer, reinterpret_cast<struct sockaddr *>(&address), sizeof(address)) != 0)
			{
				CloseNative(peer);
				peer = kInvalidSocket;
			}
		}

		if (peer == kInvalidSocket)
		{
			std::this_thread::sleep_for(std::chrono::milliseconds(200));
			continue;
		}

		int noDelay = 1;
		::setsockopt(peer, IPPROTO_TCP, TCP_NODELAY,
			reinterpret_cast<const char *>(&noDelay), sizeof(noDelay));
		SetNonBlocking(peer);

		{
			std::lock_guard<std::mutex> lock(fSocketMutex);
			fSocket = static_cast<std::intptr_t>(peer);
		}
		fConnected.store(true, std::memory_order_release);
		Trace("connected port=\"%s\"", fPortName.c_str());
	}

	if (listener != kInvalidSocket)
		CloseNative(listener);
}

void TcpLink::ReceiverLoop()
{
	//	Symbols arrive as little-endian uint16 pairs and a TCP read can split one across
	//	two segments, so a half symbol is carried over rather than discarded.
	unsigned char pending = 0;
	bool havePending = false;

	while (fRunning.load(std::memory_order_acquire))
	{
		socket_t peer = kInvalidSocket;
		{
			std::lock_guard<std::mutex> lock(fSocketMutex);
			peer = static_cast<socket_t>(fSocket);
		}
		if (peer == kInvalidSocket || !fConnected.load(std::memory_order_acquire))
		{
			std::this_thread::sleep_for(std::chrono::milliseconds(5));
			havePending = false;
			continue;
		}

		fd_set readable;
		FD_ZERO(&readable);
		FD_SET(peer, &readable);
		struct timeval wait;
		wait.tv_sec = 0;
		wait.tv_usec = 2000;
		if (::select(static_cast<int>(peer) + 1, &readable, NULL, NULL, &wait) <= 0)
			continue;

		unsigned char chunk[512];
		const int received = ::recv(peer, reinterpret_cast<char *>(chunk), sizeof(chunk), 0);
		if (received == 0)
		{
			Trace("peer closed port=\"%s\"", fPortName.c_str());
			CloseSocket();
			continue;
		}
		if (received < 0)
		{
			if (WouldBlock())
				continue;
			Trace("receive error port=\"%s\"", fPortName.c_str());
			CloseSocket();
			continue;
		}

		const std::uint64_t timestamp = TimestampNow();
		std::lock_guard<std::mutex> lock(fRxMutex);
		for (int i = 0; i < received; ++i)
		{
			if (!havePending)
			{
				pending = chunk[i];
				havePending = true;
				continue;
			}
			const Symbol symbol = static_cast<Symbol>(
				pending | (static_cast<Symbol>(chunk[i]) << 8));
			havePending = false;
			fRxBuffer.push_back(static_cast<Symbol>(symbol & (kWakeupMask | kPayloadMask)));
			fRxTimestamps.push_back(timestamp);
			Trace("rx %s%02X", (symbol & kWakeupMask) ? "1." : "0.", symbol & kPayloadMask);
		}
		fRxSignal.notify_all();
	}
}

std::size_t TcpLink::Read(Symbol *out, std::size_t maxSymbols, std::uint64_t *timestampsOut)
{
	if (!out || maxSymbols == 0)
		return 0;

	std::unique_lock<std::mutex> lock(fRxMutex);
	if (fRxBuffer.empty())
	{
		//	One short wait for the first symbol. libsas scans for the address byte in a
		//	tight loop, so returning instantly would spin a core, while waiting the full
		//	read timeout would blow its 9 ms inter-byte budget. A couple of milliseconds
		//	sits between the two.
		fRxSignal.wait_for(lock, fConfig.pollTimeout, [this] {
			return !fRxBuffer.empty() || !fRunning.load(std::memory_order_acquire)
				|| fCancelReceive.load(std::memory_order_acquire);
		});
	}
	if (fCancelReceive.exchange(false, std::memory_order_acq_rel))
		return 0;

	const std::size_t count = fRxBuffer.size() < maxSymbols ? fRxBuffer.size() : maxSymbols;
	for (std::size_t i = 0; i < count; ++i)
	{
		out[i] = fRxBuffer[i];
		if (timestampsOut)
			timestampsOut[i] = fRxTimestamps[i];
	}
	fRxBuffer.erase(fRxBuffer.begin(), fRxBuffer.begin() + static_cast<std::ptrdiff_t>(count));
	fRxTimestamps.erase(fRxTimestamps.begin(),
		fRxTimestamps.begin() + static_cast<std::ptrdiff_t>(count));
	return count;
}

bool TcpLink::Write(const Symbol *symbols, std::size_t count, std::size_t *written)
{
	if (written)
		*written = 0;
	if (!symbols || count == 0)
		return true;

	std::vector<unsigned char> bytes;
	bytes.reserve(count * 2);
	for (std::size_t i = 0; i < count; ++i)
	{
		const Symbol symbol = static_cast<Symbol>(symbols[i] & (kWakeupMask | kPayloadMask));
		bytes.push_back(static_cast<unsigned char>(symbol & 0xFF));
		bytes.push_back(static_cast<unsigned char>((symbol >> 8) & 0xFF));
		Trace("tx %s%02X", (symbol & kWakeupMask) ? "1." : "0.", symbol & kPayloadMask);
	}

	std::lock_guard<std::mutex> lock(fSocketMutex);
	if (fSocket == -1 || !fConnected.load(std::memory_order_acquire))
		return false;

	const socket_t peer = static_cast<socket_t>(fSocket);
	std::size_t sent = 0;
	while (sent < bytes.size())
	{
		const int chunk = ::send(peer, reinterpret_cast<const char *>(bytes.data() + sent),
			static_cast<int>(bytes.size() - sent), 0);
		if (chunk > 0)
		{
			sent += static_cast<std::size_t>(chunk);
			continue;
		}
		if (chunk < 0 && WouldBlock())
		{
			std::this_thread::sleep_for(std::chrono::microseconds(200));
			continue;
		}
		Trace("transmit error port=\"%s\"", fPortName.c_str());
		return false;
	}

	if (written)
		*written = count;
	return true;
}

void TcpLink::FlushReceive()
{
	std::lock_guard<std::mutex> lock(fRxMutex);
	fRxBuffer.clear();
	fRxTimestamps.clear();
}

void TcpLink::FlushTransmit()
{
	//	Every write reaches the socket before it returns, so nothing is queued this side.
}

void TcpLink::CancelReceive()
{
	fCancelReceive.store(true, std::memory_order_release);
	fRxSignal.notify_all();
}

std::uint64_t TcpLink::TimestampNow() const
{
	return static_cast<std::uint64_t>(std::chrono::duration_cast<std::chrono::microseconds>(
		std::chrono::steady_clock::now() - fEpoch).count());
}

bool TcpLink::Configure(const State &state)
{
	//	Refuse rather than degrade: an 8-bit link cannot carry the wakeup bit, and a SAS
	//	link that quietly drops it looks healthy while being unusable.
	if (state.dataBits != 9)
	{
		Trace("refused setState dataBits=%u: this transport is 9-bit only",
			static_cast<unsigned>(state.dataBits));
		return false;
	}
	std::lock_guard<std::mutex> lock(fStateMutex);
	fState = state;
	return true;
}

TcpLink::State TcpLink::GetState() const
{
	std::lock_guard<std::mutex> lock(fStateMutex);
	return fState;
}

void TcpLink::SetTimeouts(const Timeouts &timeouts)
{
	std::lock_guard<std::mutex> lock(fStateMutex);
	fTimeouts = timeouts;
}

TcpLink::Timeouts TcpLink::GetTimeouts() const
{
	std::lock_guard<std::mutex> lock(fStateMutex);
	return fTimeouts;
}

void TcpLink::Trace(const char *format, ...)
{
	if (!fLogFile)
		return;
	std::lock_guard<std::mutex> lock(fLogMutex);
	std::FILE *out = static_cast<std::FILE *>(fLogFile);
	std::fprintf(out, "[qxcom-tcp %10llu] ", static_cast<unsigned long long>(TimestampNow()));
	va_list args;
	va_start(args, format);
	std::vfprintf(out, format, args);
	va_end(args);
	std::fputc('\n', out);
	std::fflush(out);
}

}	// namespace saswb
