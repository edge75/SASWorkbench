/*
 *	SasWorkbench's substitution for QPM2's simulated peripheral manager.
 *
 *	QPM2's simulated printer prints a ticket to stdout and nothing else, so no ticket the machine
 *	produces can be looked at, redeemed or asserted on. This file gives that one function a body
 *	that also files the ticket in the wallet, which is the directory the simulator's printer panel
 *	shows and the test host reads.
 *
 *	QPM2 is substituted, never edited. Their source is *included* rather than copied, with only the
 *	printer entry point renamed out of the way, so QPM2 remains the only place that file is
 *	maintained: a change they make to any other peripheral arrives here on the next configure, and
 *	the printer is the only behaviour that is ours. The swap is made by a gitignored preset in the
 *	demo, so no customer build reaches this file.
 */

//	Their whole file, unchanged. SASWB_QPM_HAL_SOURCE is the path to it, supplied by the CMake hook
//	so this source names no checkout.
#define qpmPrinterPrint qpmPrinterPrintAsQpm2WroteIt
#define qpmPrinterRegisterCallback qpmPrinterRegisterCallbackAsQpm2WroteIt
#include SASWB_QPM_HAL_SOURCE
#undef qpmPrinterPrint
#undef qpmPrinterRegisterCallback

#include <chrono>
#include <cstdio>
#include <ctime>
#include <string>
#include <thread>

#include "Ticket.h"
#include "Wallet.h"

namespace
{

//	The printer is told a template, not a validation type: the type is assigned when the ticket is
//	validated, which happens before this call and out of the printer's sight. Four of the five
//	templates name exactly one type, so those four are carried across; a void ticket names none and
//	keeps the default, with the template recorded in the note either way so the derivation is
//	visible rather than assumed.
TicketValidationType ValidationTypeFor(int printerTemplate)
{
	switch (printerTemplate)
	{
		case QPM_PRINTER_TKT_CASHOUTTKT:		return TicketValidationType::CashableFromCashout;
		case QPM_PRINTER_TKT_RESTRICTEDTKT:		return TicketValidationType::RestrictedPromotionalFromCashout;
		case QPM_PRINTER_TKT_CASHOUTRCP:		return TicketValidationType::CancelledCreditHandpayReceiptPrinted;
		case QPM_PRINTER_TKT_JACKPOTRCP:		return TicketValidationType::JackpotHandpayReceiptPrinted;
		default:								return TicketValidationType::CashableFromCashout;
	}
}

const char *TemplateName(int printerTemplate)
{
	switch (printerTemplate)
	{
		case QPM_PRINTER_TKT_VOID:				return "void";
		case QPM_PRINTER_TKT_CASHOUTTKT:		return "cash ticket out";
		case QPM_PRINTER_TKT_CASHOUTRCP:		return "cash out receipt";
		case QPM_PRINTER_TKT_JACKPOTRCP:		return "jackpot receipt";
		case QPM_PRINTER_TKT_RESTRICTEDTKT:		return "restricted ticket";
		default:								return "unknown";
	}
}

//	The validation code arrives as text because that is what gets barcoded. Anything that is not a
//	whole number is left as zero rather than guessed at, and survives verbatim in the note.
bool TryParseDigits(const char *text, uint64_t &value)
{
	if (text == nullptr || *text == '\0')
		return false;

	value = 0;
	for (const char *at = text; *at != '\0'; at++)
	{
		if (*at < '0' || *at > '9')
			return false;

		if (value > (UINT64_MAX - static_cast<uint64_t>(*at - '0')) / 10)
			return false;

		value = value * 10 + static_cast<uint64_t>(*at - '0');
	}

	return true;
}

//	QSP writes the expiration as a duration — "3 Days" — not as a date, so the date is that many
//	days after the moment of printing. A duration that is absent, zero or unreadable leaves the
//	field empty, which is a ticket that does not expire; the machine's own words are kept in the
//	note so a test can assert on what it actually asked for.
bool TryParseDays(const char *text, int &days)
{
	if (text == nullptr)
		return false;

	int parsed = 0;
	const char *at = text;
	while (*at == ' ')
		at++;

	if (*at < '0' || *at > '9')
		return false;

	for (; *at >= '0' && *at <= '9'; at++)
		parsed = parsed * 10 + (*at - '0');

	days = parsed;
	return parsed > 0;
}

std::string Format(const std::tm &when, const char *pattern)
{
	char text[64] = {};
	std::strftime(text, sizeof(text), pattern, &when);
	return text;
}

//	The moment of printing, observed here. The machine's own date and time strings are not parsed
//	into it: they are rendered for a person in whatever form the market asks for, so reading them
//	back as a date would be guessing at a format. They are kept verbatim in the note instead.
std::tm LocalNow()
{
	std::time_t now = std::time(nullptr);
	std::tm broken = {};
#ifdef _WIN32
	localtime_s(&broken, &now);
#else
	localtime_r(&now, &broken);
#endif
	return broken;
}

Ticket TicketFromDescriptor(const QPMO_PRINTER_TICKET_DESCRIPTOR &descriptor)
{
	Ticket ticket;

	uint64_t validationNumber = 0;
	if (TryParseDigits(descriptor.Barcode, validationNumber))
		ticket.validationNumber = validationNumber;

	ticket.validationType = ValidationTypeFor(descriptor.Template);
	ticket.amountCents = static_cast<int64_t>(descriptor.Value);
	ticket.ticketNumber = static_cast<uint32_t>(descriptor.VoucherNumber);

	std::tm printedAt = LocalNow();
	ticket.printedAt = Format(printedAt, "%Y-%m-%dT%H:%M:%S");

	int days = 0;
	if (TryParseDays(descriptor.Expiration, days))
	{
		std::tm expires = printedAt;
		expires.tm_mday += days;			//	mktime normalises the overflow into months and years
		expires.tm_isdst = -1;
		if (std::mktime(&expires) != static_cast<std::time_t>(-1))
			ticket.expiration = Format(expires, "%Y-%m-%d");
	}

	//	What the printer was handed and this model has nowhere else to put. The pool, the validation
	//	system, the asset number and the location are not in the descriptor at all: the printer is
	//	never told them, so they stay unset rather than being invented here. The host supplied them
	//	over the wire and can check them against its own record.
	std::string note = "printed by the machine: template=";
	note += TemplateName(descriptor.Template);
	note += "; validation_code=";
	note += descriptor.Barcode;
	note += "; date=";
	note += descriptor.Date;
	note += "; time=";
	note += descriptor.Time;
	note += "; expiration=";
	note += descriptor.Expiration;
	ticket.note = note;

	return ticket;
}

//	The machine's own copy of the printer callback. Kept here rather than read back out of QPM2's
//	wrapper, whose members are private: registering is intercepted the same way printing is, so
//	nothing in their file has to be reached into.
d2cCallback sPrinterCallback = nullptr;

//	A print that never completes leaves the machine believing its printer is still busy, and a busy
//	printer is a machine that refuses money - measured, not argued: after one cashout every
//	subsequent note was rejected until this event was delivered. A real printer reports the ticket
//	when the paper has moved, so this is reported from a thread a moment later rather than from
//	inside the print call, which is still on the machine's printer thread.
void ReportPrintComplete()
{
	d2cCallback callback = sPrinterCallback;
	if (callback == nullptr)
		return;

	std::thread([callback]
	{
		std::this_thread::sleep_for(std::chrono::milliseconds(200));

		D2C_MESSAGE message;
		QPM_INIT_OBJECT(&message, D2C_MESSAGE);
		message.MessageId = D2C_PRINTER_PRINT_COMPLETE;
		message.Argument = 0;
		callback(&message);
	}).detach();
}

}	//	namespace

QPMAPI QRESULT qpmPrinterRegisterCallback(QPMHANDLE handle, const char *deviceId,
	d2cCallback callback)
{
	QRESULT result = qpmPrinterRegisterCallbackAsQpm2WroteIt(handle, deviceId, callback);
	if (result == Q_SUCCESS)
		sPrinterCallback = callback;

	return result;
}

QPMAPI QRESULT qpmPrinterPrint(QPMHANDLE handle, const char *deviceId,
	const PQPMO_PRINTER_TICKET_DESCRIPTOR ticket)
{
	QRESULT result = qpmPrinterPrintAsQpm2WroteIt(handle, deviceId, ticket);
	if (result != Q_SUCCESS || ticket == nullptr)
		return result;

	//	The print itself still succeeded, because it did: the machine behaved exactly as it would
	//	on a board. A ticket that could not be filed is said loudly instead, and the test that was
	//	waiting for it fails for the true reason rather than passing on a ticket nobody can find.
	try
	{
		Wallet wallet(DefaultWalletDirectory());
		std::string path = wallet.Add(TicketFromDescriptor(*ticket), TicketEncoding::Json);
		printf("[SasWorkbench] ticket filed in the wallet: %s\n", path.c_str());
	}
	catch (const std::exception &problem)
	{
		printf("[SasWorkbench] FAILED to file the printed ticket in %s: %s\n",
			DefaultWalletDirectory().c_str(), problem.what());
	}

	//	Reported whether or not the ticket could be filed: the machine printed it either way, and a
	//	machine left permanently busy because our wallet was unwritable would be a second failure on
	//	top of the one worth reporting.
	ReportPrintComplete();
	return result;
}
