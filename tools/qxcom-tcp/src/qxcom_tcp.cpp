//	SasWorkbench - qxcom-tcp: the libqxcom surface.
//
//	Implements the contract declared by QSP/simulator/include/qxcom/, which has shipped
//	as headers with no implementation since SIMULATE_QXCOM was added. Every serial
//	operation is served by a TCP symbol link instead of \\.\QXCOM.

#include "tcp_link.h"

#include <cstdio>
#include <cstring>
#include <map>
#include <memory>
#include <mutex>
#include <string>

#include "libqxcom.h"
#include "libqxcom_callbacks.h"

namespace
{

using saswb::TcpLink;

std::mutex &RegistryMutex()
{
	static std::mutex mutex;
	return mutex;
}

std::map<long long, std::shared_ptr<TcpLink> > &Registry()
{
	static std::map<long long, std::shared_ptr<TcpLink> > registry;
	return registry;
}

//	Handles start at 1 so that 0 keeps its documented meaning of "invalid".
long long NextHandle()
{
	static long long next = 1;
	return next++;
}

std::shared_ptr<TcpLink> LinkFor(long long handle)
{
	std::lock_guard<std::mutex> lock(RegistryMutex());
	const std::map<long long, std::shared_ptr<TcpLink> >::iterator found = Registry().find(handle);
	return found == Registry().end() ? std::shared_ptr<TcpLink>() : found->second;
}

unsigned int OpenLink(const char *portName, long long *handle)
{
	if (!handle)
		return Q_QXCOM_OPEN_INVALID_PARAM;

	const std::string name = portName ? portName : "";
	std::shared_ptr<TcpLink> link(new TcpLink(saswb::ConfigFromEnvironment(name), name));

	std::lock_guard<std::mutex> lock(RegistryMutex());
	const long long assigned = NextHandle();
	Registry()[assigned] = link;
	*handle = assigned;
	return Q_SUCCESS;
}

unsigned int CloseLink(long long handle)
{
	std::lock_guard<std::mutex> lock(RegistryMutex());
	const std::map<long long, std::shared_ptr<TcpLink> >::iterator found = Registry().find(handle);
	if (found == Registry().end())
		return Q_QXCOM_CLOSE_W_ALREADY_CLOSED;
	Registry().erase(found);
	return Q_SUCCESS;
}

//
//	QUIXANT_SERIAL_CALLBACKS entry points
//

unsigned int CbOpen(char *portName, long long *handle)
{
	return OpenLink(portName, handle);
}

unsigned int CbClose(long long handle)
{
	return CloseLink(handle);
}

unsigned int CbCleanup()
{
	std::lock_guard<std::mutex> lock(RegistryMutex());
	Registry().clear();
	return Q_SUCCESS;
}

unsigned int CbSetState(long long handle, unsigned int baudRate, unsigned char dataBits,
	unsigned short parity, unsigned char stopBits, unsigned char enableTimestamps)
{
	const std::shared_ptr<TcpLink> link = LinkFor(handle);
	if (!link)
		return Q_QXCOM_SETPARAM_INVALID_HANDLE;

	TcpLink::State state;
	state.baudRate = baudRate;
	state.dataBits = dataBits;
	state.parity = parity;
	state.stopBits = stopBits;
	state.enableTimestamps = enableTimestamps;
	return link->Configure(state) ? Q_SUCCESS : Q_QXCOM_SETPARAM_INVALID_PARAM;
}

unsigned int CbGetState(long long handle, unsigned int *baudRate, unsigned char *dataBits,
	unsigned short *parity, unsigned char *stopBits, unsigned char *enableTimestamps)
{
	const std::shared_ptr<TcpLink> link = LinkFor(handle);
	if (!link)
		return Q_QXCOM_GETPARAM_INVALID_HANDLE;

	const TcpLink::State state = link->GetState();
	if (baudRate) *baudRate = state.baudRate;
	if (dataBits) *dataBits = state.dataBits;
	if (parity) *parity = state.parity;
	if (stopBits) *stopBits = state.stopBits;
	if (enableTimestamps) *enableTimestamps = state.enableTimestamps;
	return Q_SUCCESS;
}

unsigned int CbSetTimeouts(long long handle, long long intervalTimeout, long long readTimeout,
	long long writeTimeout)
{
	const std::shared_ptr<TcpLink> link = LinkFor(handle);
	if (!link)
		return Q_QXCOM_SETTIMEOUTS_INVALID_HANDLE;

	TcpLink::Timeouts timeouts;
	timeouts.intervalTimeout = intervalTimeout;
	timeouts.readTimeout = readTimeout;
	timeouts.writeTimeout = writeTimeout;
	link->SetTimeouts(timeouts);
	return Q_SUCCESS;
}

unsigned int CbGetTimeouts(long long handle, long long *intervalTimeout, long long *readTimeout,
	long long *writeTimeout)
{
	const std::shared_ptr<TcpLink> link = LinkFor(handle);
	if (!link)
		return Q_QXCOM_GETTIMEOUTS_INVALID_HANDLE;

	const TcpLink::Timeouts timeouts = link->GetTimeouts();
	if (intervalTimeout) *intervalTimeout = timeouts.intervalTimeout;
	if (readTimeout) *readTimeout = timeouts.readTimeout;
	if (writeTimeout) *writeTimeout = timeouts.writeTimeout;
	return Q_SUCCESS;
}

//	The two consumers of this struct declare this slot with different signatures - the
//	qxcom header takes (typeId, version), libsas takes (handle, typeId, version). The
//	header this file is compiled against wins; libsas stores the pointer but never calls
//	it, so the disagreement is inert.
unsigned int CbGetVersion(unsigned char *typeId, unsigned int *version)
{
	if (typeId) *typeId = LIBQXCOM_TYPEID;
	if (version) *version = LIBQXCOM_VERSION;
	return Q_SUCCESS;
}

unsigned int CbGetTimestamp(long long handle, unsigned long long *timestamp)
{
	const std::shared_ptr<TcpLink> link = LinkFor(handle);
	if (!link)
		return Q_QXCOM_TIMESTAMP_INVALID_HANDLE;
	if (timestamp)
		*timestamp = link->TimestampNow();
	return Q_SUCCESS;
}

//	Refused, not emulated: an 8-bit transfer cannot carry the wakeup bit, and a SAS link
//	that silently drops it reports healthy while being unusable. Only the 9-bit extended
//	pair below is implemented, which is the pair libsas uses.
unsigned int CbRead(long long, unsigned char *, unsigned int, unsigned long long *, unsigned int *dataRead)
{
	if (dataRead)
		*dataRead = 0;
	return Q_QXCOM_READ_INVALID_SEMANTICS_FILTER;
}

unsigned int CbWrite(long long, const unsigned char *, unsigned int, unsigned int *dataWritten)
{
	if (dataWritten)
		*dataWritten = 0;
	return Q_QXCOM_WRITE_INVALID_PARAM;
}

unsigned int CbReadExtended(long long handle, unsigned short *buffer, unsigned int length,
	unsigned long long *timestamps, unsigned int *dataRead)
{
	if (dataRead)
		*dataRead = 0;
	if (!buffer)
		return Q_QXCOM_READ_INVALID_PARAM;

	const std::shared_ptr<TcpLink> link = LinkFor(handle);
	if (!link)
		return Q_QXCOM_READ_INVALID_HANDLE;

	const std::size_t count = link->Read(buffer, length, timestamps);
	if (dataRead)
		*dataRead = static_cast<unsigned int>(count);
	return Q_SUCCESS;
}

unsigned int CbWriteExtended(long long handle, const unsigned short *buffer, unsigned int length,
	unsigned int *dataWritten)
{
	if (dataWritten)
		*dataWritten = 0;
	if (!buffer)
		return Q_QXCOM_WRITE_INVALID_PARAM;

	const std::shared_ptr<TcpLink> link = LinkFor(handle);
	if (!link)
		return Q_QXCOM_WRITE_INVALID_HANDLE;

	std::size_t written = 0;
	if (!link->Write(buffer, length, &written))
		return Q_QXCOM_WRITE_FAILED;
	if (dataWritten)
		*dataWritten = static_cast<unsigned int>(written);
	return Q_SUCCESS;
}

unsigned int CbFlushTx(long long handle)
{
	const std::shared_ptr<TcpLink> link = LinkFor(handle);
	if (!link)
		return Q_QXCOM_FLUSH_INVALID_HANDLE;
	link->FlushTransmit();
	return Q_SUCCESS;
}

unsigned int CbFlushRx(long long handle)
{
	const std::shared_ptr<TcpLink> link = LinkFor(handle);
	if (!link)
		return Q_QXCOM_FLUSH_INVALID_HANDLE;
	link->FlushReceive();
	return Q_SUCCESS;
}

unsigned int CbCancelRx(long long handle)
{
	const std::shared_ptr<TcpLink> link = LinkFor(handle);
	if (!link)
		return Q_QXCOM_FLUSH_INVALID_HANDLE;
	link->CancelReceive();
	return Q_SUCCESS;
}

//	The qxCom* API addresses links by opaque pointer; the callback API addresses the same
//	links by integer. One registry serves both.
long long HandleFromPointer(QXCOM_HANDLE handle)
{
	return static_cast<long long>(reinterpret_cast<std::intptr_t>(handle));
}

}	// namespace

QUIXANT_EXPORT void assignCallbacks(void *callbacksStruct)
{
	if (!callbacksStruct)
		return;

	PQUIXANT_SERIAL_CALLBACKS callbacks = static_cast<PQUIXANT_SERIAL_CALLBACKS>(callbacksStruct);
	callbacks->callbacksVersion = CALLBACKS_VERSION;
	callbacks->open = &CbOpen;
	callbacks->close = &CbClose;
	callbacks->cleanup = &CbCleanup;
	callbacks->setState = &CbSetState;
	callbacks->getState = &CbGetState;
	callbacks->setTimeouts = &CbSetTimeouts;
	callbacks->getTimeouts = &CbGetTimeouts;
	callbacks->getVersion = &CbGetVersion;
	callbacks->getTimestamp = &CbGetTimestamp;
	callbacks->read = &CbRead;
	callbacks->readExtended = &CbReadExtended;
	callbacks->write = &CbWrite;
	callbacks->writeExtended = &CbWriteExtended;
	callbacks->flushRx = &CbFlushRx;
	callbacks->flushTx = &CbFlushTx;
	callbacks->cancelRx = &CbCancelRx;
}

//
//	The direct qxCom* API. libsas reaches the transport through the callback struct
//	above; these exist because the header declares them and other consumers link them.
//

QXCOMAPI QRESULT qxComOpen(QXCOM_HANDLE *handle, char *portName)
{
	if (!handle)
		return Q_QXCOM_OPEN_INVALID_PARAM;

	long long assigned = 0;
	const unsigned int result = OpenLink(portName, &assigned);
	if (result != Q_SUCCESS)
		return result;
	*handle = reinterpret_cast<QXCOM_HANDLE>(static_cast<std::intptr_t>(assigned));
	return Q_SUCCESS;
}

QXCOMAPI QRESULT qxComSetCommParameters(QXCOM_HANDLE handle, QXCOM_DCB dcb)
{
	return CbSetState(HandleFromPointer(handle), dcb.BaudRate,
		static_cast<unsigned char>(dcb.Databits), static_cast<unsigned short>(dcb.Parity),
		static_cast<unsigned char>(dcb.StopBits), dcb.EnableTimestamps);
}

QXCOMAPI QRESULT qxComGetCommParameters(QXCOM_HANDLE handle, PQXCOM_DCB dcb)
{
	if (!dcb)
		return Q_QXCOM_GETPARAM_INVALID_PARAM;

	unsigned char dataBits = 0, stopBits = 0, timestamps = 0;
	unsigned short parity = 0;
	const QRESULT result = CbGetState(HandleFromPointer(handle), &dcb->BaudRate, &dataBits,
		&parity, &stopBits, &timestamps);
	dcb->Databits = dataBits;
	dcb->Parity = parity;
	dcb->StopBits = stopBits;
	dcb->EnableTimestamps = timestamps;
	return result;
}

QXCOMAPI QRESULT qxComSetCommTimeouts(QXCOM_HANDLE handle, QXCOM_TIMEOUTS timeouts)
{
	return CbSetTimeouts(HandleFromPointer(handle), timeouts.IntervalTimeout,
		timeouts.ReadTimeout, timeouts.WriteTimeout);
}

QXCOMAPI QRESULT qxComGetCommTimeouts(QXCOM_HANDLE handle, PQXCOM_TIMEOUTS timeouts)
{
	if (!timeouts)
		return Q_QXCOM_GETTIMEOUTS_INVALID_PARAM;
	return CbGetTimeouts(HandleFromPointer(handle), &timeouts->IntervalTimeout,
		&timeouts->ReadTimeout, &timeouts->WriteTimeout);
}

QXCOMAPI QRESULT qxComSetSemantics(QXCOM_HANDLE, const PQXCOM_SEMANTICS)
{
	return Q_QXCOM_SETSEMANTICS_NOT_IMPLEMENTED;
}

QXCOMAPI QRESULT qxComGetLengthOfSemanticsItems(QXCOM_HANDLE, unsigned int *length)
{
	if (length)
		*length = 0;
	return Q_SUCCESS;
}

QXCOMAPI QRESULT qxComGetSemantics(QXCOM_HANDLE, PQXCOM_SEMANTICS)
{
	return Q_QXCOM_GETSEMANTICS_NOT_IMPLEMENTED;
}

QXCOMAPI QRESULT qxComGetInventory(QXCOM_HANDLE handle, PQXCOM_INVENTORY inventory)
{
	if (!inventory)
		return Q_QXCOM_INVENTORY_INVALID_PARAM;

	const std::shared_ptr<TcpLink> link = LinkFor(HandleFromPointer(handle));
	if (!link)
		return Q_QXCOM_INVENTORY_INVALID_HANDLE;

	std::memset(inventory, 0, sizeof(*inventory));
#if defined(_WIN32)
	strncpy_s(inventory->PortName, sizeof(inventory->PortName), link->PortName().c_str(), _TRUNCATE);
#else
	std::strncpy(inventory->PortName, link->PortName().c_str(), sizeof(inventory->PortName) - 1);
#endif
	inventory->PortType = QXCOM;
	inventory->LibraryVersion = LIBQXCOM_VERSION;
	return Q_SUCCESS;
}

QXCOMAPI QRESULT qxComRead(QXCOM_HANDLE handle, PQXCOM_PACKET buffer, unsigned int nUnits,
	unsigned int *unitsRead)
{
	if (unitsRead)
		*unitsRead = 0;
	if (!buffer)
		return Q_QXCOM_READ_INVALID_PARAM;

	const std::shared_ptr<TcpLink> link = LinkFor(HandleFromPointer(handle));
	if (!link)
		return Q_QXCOM_READ_INVALID_HANDLE;

	std::vector<saswb::Symbol> symbols(nUnits);
	std::vector<unsigned long long> timestamps(nUnits);
	const std::size_t count = link->Read(symbols.data(), nUnits, timestamps.data());
	for (std::size_t i = 0; i < count; ++i)
	{
		buffer[i].Data.Raw = symbols[i];
		buffer[i].Timestamp = timestamps[i];
	}
	if (unitsRead)
		*unitsRead = static_cast<unsigned int>(count);
	return Q_SUCCESS;
}

QXCOMAPI QRESULT qxComWrite(QXCOM_HANDLE handle, const PQXCOM_PACKET buffer, unsigned int nUnits,
	unsigned int *unitsWritten)
{
	if (unitsWritten)
		*unitsWritten = 0;
	if (!buffer)
		return Q_QXCOM_WRITE_INVALID_PARAM;

	const std::shared_ptr<TcpLink> link = LinkFor(HandleFromPointer(handle));
	if (!link)
		return Q_QXCOM_WRITE_INVALID_HANDLE;

	std::vector<saswb::Symbol> symbols(nUnits);
	for (unsigned int i = 0; i < nUnits; ++i)
		symbols[i] = buffer[i].Data.Raw;

	std::size_t written = 0;
	if (!link->Write(symbols.data(), nUnits, &written))
		return Q_QXCOM_WRITE_FAILED;
	if (unitsWritten)
		*unitsWritten = static_cast<unsigned int>(written);
	return Q_SUCCESS;
}

QXCOMAPI QRESULT qxComGetCurrentTimestamp(QXCOM_HANDLE handle, unsigned long long *timestamp)
{
	return CbGetTimestamp(HandleFromPointer(handle), timestamp);
}

QXCOMAPI QRESULT qxComFlush(QXCOM_HANDLE handle, unsigned int flags)
{
	const long long id = HandleFromPointer(handle);
	QRESULT result = Q_SUCCESS;
	if (flags & QXCOM_FLAG_FLUSH_TX)
		result = CbFlushTx(id);
	if (result == Q_SUCCESS && (flags & QXCOM_FLAG_FLUSH_RX))
		result = CbFlushRx(id);
	if (result == Q_SUCCESS && (flags & QXCOM_FLAG_CANCEL_RX))
		result = CbCancelRx(id);
	return result;
}

QXCOMAPI QRESULT qxComClose(QXCOM_HANDLE handle)
{
	return CloseLink(HandleFromPointer(handle));
}

QXCOMAPI QRESULT qxComSetEventMask(QXCOM_HANDLE, unsigned short, QXCOM_EVENT_CALLBACK)
{
	return Q_QXCOM_EVENTMASK_NOT_IMPLEMENTED;
}

QXCOMAPI QRESULT qxComReleaseResources()
{
	return CbCleanup();
}

QXCOMAPI QRESULT qxComSetGlobalFlags(unsigned int)
{
	return Q_SUCCESS;
}

QXCOMAPI QRESULT qxComSetLoggingCallback(QXCOM_HANDLE, QXCOM_LOGGING_CALLBACK)
{
	return Q_QXCOM_SETLOG_NOT_IMPLEMENTED;
}
