/*
 *	SasWorkbench's substitution for the demo's RNG, built only by the scripted-outcomes preset (D64).
 *
 *	The demo's own Rng.cpp is included whole, with its class renamed out of the way, and keeps doing
 *	everything it did: seeding, the background cycling thread, the state saved to vendor NVRAM after
 *	every draw. The real Rng class below hands each game draw to it and, when the test host has queued
 *	an outcome, returns the queued value instead (saswb_outcome_queue.h has the rules). Every game
 *	draw is logged, forced or not, and a marker tied to this process says the build is scripted.
 *
 *	The demo is substituted, never edited. The swap is made by a gitignored preset in the demo
 *	(cmake/saswb-scripted-outcomes.cmake), so no customer build reaches this file.
 */

//	Everything their file includes, first, so the rename below reaches only their class.
#include <cassert>
#include <mutex>
#include <random>
#include <thread>

#include "libqsp.h"

//	Their class and its definitions, as SaswbStockRng. Their Rng.cpp includes "Rng.h" from its own
//	directory, which the guard turns into nothing after the first inclusion here.
#define Rng SaswbStockRng
#include "Rng.h"
#include SASWB_RNG_SOURCE
#undef Rng
#undef RNG_H

//	The real class, with the declaration every other file of the demo was compiled against.
#include "Rng.h"

#include <chrono>
#include <cstdio>
#include <filesystem>
#include <fstream>
#include <iterator>
#include <optional>
#include <string>
#include <system_error>
#include <vector>

#if defined(_WIN32)
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#else
#include <unistd.h>
#endif

#include "saswb_outcome_queue.h"

#ifndef SASWB_OUTCOMES_DIR
#error "SASWB_OUTCOMES_DIR names where the test host queues outcomes; the CMake hook supplies it"
#endif

namespace
{

namespace fs = std::filesystem;

const char *const kQueueFile = "queue.txt";
const char *const kClaimedFile = "queue.taking";
const char *const kRemainderFile = "queue.rest";
const char *const kLogFile = "draws.log";
const char *const kMarkerFile = "scripted-outcomes.marker";

SaswbStockRng			*gStock = nullptr;
saswb::OutcomeServer	gServer;
std::mutex				gDrawMutex;

fs::path Directory() { return fs::path(SASWB_OUTCOMES_DIR); }

uint64_t NowMilliseconds()
{
	return static_cast<uint64_t>(std::chrono::duration_cast<std::chrono::milliseconds>(
		std::chrono::system_clock::now().time_since_epoch()).count());
}

void Log(const std::vector<std::string> &lines)
{
	std::ofstream log(Directory() / kLogFile, std::ios::app);
	for (const auto &line : lines)
		log << NowMilliseconds() << ' ' << line << '\n';
	log.flush();
}

//	Takes the queue's first line, writing the rest back; the rename makes the take atomic against the host.
std::optional<std::string> TakeNextLine()
{
	std::error_code error;
	const fs::path queue = Directory() / kQueueFile;
	const fs::path claimed = Directory() / kClaimedFile;
	if (!fs::exists(queue, error))
		return std::nullopt;

	fs::rename(queue, claimed, error);
	if (error)
	{
		Log({"refused -: the queue could not be taken: " + error.message()});
		return std::nullopt;
	}

	std::vector<std::string> lines;
	{
		std::ifstream in(claimed);
		std::string line;
		while (std::getline(in, line))
		{
			if (!line.empty() && line.back() == '\r')
				line.pop_back();
			if (!line.empty())
				lines.push_back(line);
		}
	}

	if (lines.size() > 1)
	{
		const fs::path rest = Directory() / kRemainderFile;
		{
			std::ofstream out(rest, std::ios::trunc);
			for (size_t i = 1; i < lines.size(); i++)
				out << lines[i] << '\n';
		}
		fs::rename(rest, queue, error);
	}
	fs::remove(claimed, error);

	if (lines.empty())
		return std::nullopt;
	return lines.front();
}

#if defined(_WIN32)
uint64_t ProcessCreated()
{
	FILETIME created{}, exited{}, kernel{}, user{};
	if (!GetProcessTimes(GetCurrentProcess(), &created, &exited, &kernel, &user))
		return 0;
	return (static_cast<uint64_t>(created.dwHighDateTime) << 32) | created.dwLowDateTime;
}

unsigned long ProcessId() { return GetCurrentProcessId(); }

std::string ExecutablePath()
{
	wchar_t path[MAX_PATH * 4] = {};
	const DWORD length = GetModuleFileNameW(nullptr, path, static_cast<DWORD>(std::size(path)));
	return fs::path(std::wstring(path, length)).string();
}
#else
uint64_t ProcessCreated() { return 0; }
unsigned long ProcessId() { return static_cast<unsigned long>(getpid()); }
std::string ExecutablePath()
{
	std::error_code error;
	return fs::read_symlink("/proc/self/exe", error).string();
}
#endif

//	Tied to this process, so a marker left by one that has gone cannot vouch for whatever runs next.
void WriteMarker()
{
	std::error_code error;
	fs::create_directories(Directory(), error);
	const fs::path marker = Directory() / kMarkerFile;
	const fs::path written = Directory() / (std::string(kMarkerFile) + ".new");
	{
		std::ofstream out(written, std::ios::trunc);
		out << "format=1\n"
			<< "pid=" << ProcessId() << '\n'
			<< "created=" << ProcessCreated() << '\n'
			<< "exe=" << ExecutablePath() << '\n';
	}
	fs::rename(written, marker, error);
	Log({"started pid=" + std::to_string(ProcessId())});
}

void RemoveMarker()
{
	std::error_code error;
	fs::remove(Directory() / kMarkerFile, error);
	Log({"stopped pid=" + std::to_string(ProcessId())});
}

}	//	namespace

Rng * Rng :: sRngInstance = nullptr;

Rng :: Rng()
	: fRngThread(nullptr)
{
	assert(sRngInstance == nullptr);
	sRngInstance = this;
	gStock = new SaswbStockRng();
	WriteMarker();
}

Rng :: ~Rng()
{
	assert(sRngInstance);
	RemoveMarker();
	delete gStock;
	gStock = nullptr;
	sRngInstance = nullptr;
}

void Rng :: NvramReset()
{
	gStock->NvramReset();
}

//	Their generator always draws, so its state and its NVRAM copy move exactly as in the stock build.
uint32_t Rng :: GenerateUint32(const uint32_t min_inclusive, const uint32_t max_inclusive)
{
	const uint32_t drawn = gStock->GenerateUint32(min_inclusive, max_inclusive);

	std::lock_guard<std::mutex> lock(gDrawMutex);
	saswb::DrawDecision decision = gServer.Draw(min_inclusive, max_inclusive, TakeNextLine);
	if (!decision.forced && !decision.log.empty() && decision.log.back().rfind("drawn ", 0) == 0)
		decision.log.back() += " -> " + std::to_string(drawn);
	Log(decision.log);
	return decision.forced ? *decision.forced : drawn;
}
