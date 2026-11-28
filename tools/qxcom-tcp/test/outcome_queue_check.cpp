/*
 *	Checks the scripted-outcome queue rules (saswb_outcome_queue.h) without the demo: a queued outcome
 *	is served draw by draw, an empty queue leaves the draw random, and anything that does not fit is
 *	refused and logged rather than clamped or reinterpreted. Exits non-zero on the first failure.
 */

#include <cstdio>
#include <deque>
#include <string>

#include "../src/saswb_outcome_queue.h"

namespace
{

int failures = 0;

void Check(bool condition, const char *what)
{
	if (!condition)
	{
		std::printf("FAIL %s\n", what);
		++failures;
	}
}

bool Logged(const saswb::DrawDecision &decision, const std::string &start)
{
	for (const auto &line : decision.log)
		if (line.rfind(start, 0) == 0)
			return true;
	return false;
}

struct Queue
{
	std::deque<std::string> lines;
	saswb::OutcomeServer::NextLine Next()
	{
		return [this]() -> std::optional<std::string>
		{
			if (lines.empty())
				return std::nullopt;
			std::string line = lines.front();
			lines.pop_front();
			return line;
		};
	}
};

}	//	namespace

int main()
{
	{
		saswb::OutcomeServer server;
		Queue queue;
		const auto decision = server.Draw(0, 15, queue.Next());
		Check(!decision.forced, "an empty queue leaves the draw to the generator");
		Check(Logged(decision, "drawn 0..15 random"), "an unforced draw is logged too");
	}

	{
		saswb::OutcomeServer server;
		Queue queue{{"7 slot=15:0,16:2,15:0,14:0,13:0 wheel=19:0"}};
		const uint32_t ranges[] = {15, 16, 15, 14, 13};
		const uint32_t wanted[] = {0, 2, 0, 0, 0};
		for (int reel = 0; reel < 5; reel++)
		{
			const auto decision = server.Draw(0, ranges[reel], queue.Next());
			Check(decision.forced && *decision.forced == wanted[reel], "the slot alternative is served reel by reel");
			Check(Logged(decision, "forced 7 slot"), "each forced draw is logged with its outcome");
			Check((reel == 4) == Logged(decision, "done 7 slot"), "the outcome is done after its last draw only");
		}
		Check(!server.Draw(0, 15, queue.Next()).forced, "the next game is random again");
	}

	{
		saswb::OutcomeServer server;
		Queue queue{{"8 slot=15:12,16:13,15:13,14:13,13:12 wheel=19:3"}};
		const auto decision = server.Draw(0, 19, queue.Next());
		Check(decision.forced && *decision.forced == 3, "the wheel's first draw picks the wheel alternative");
		Check(Logged(decision, "done 8 wheel"), "a one-draw alternative is done at once");
	}

	{
		saswb::OutcomeServer server;
		Queue queue{{"9 slot=15:12,16:13,15:13,14:13,13:12"}};
		const auto decision = server.Draw(0, 19, queue.Next());
		Check(!decision.forced, "a game no alternative fits draws its own value");
		Check(Logged(decision, "refused 9: no alternative"), "and the outcome is refused, by id");
		Check(queue.lines.empty(), "the refused outcome is taken off the queue");
	}

	{
		saswb::OutcomeServer server;
		Queue queue{{"10 wheel=19:20"}};
		const auto decision = server.Draw(0, 19, queue.Next());
		Check(!decision.forced, "a value outside its range is never clamped");
		Check(Logged(decision, "refused 10: value 20 is outside 0..19"), "and the refusal says which value");
	}

	{
		saswb::OutcomeServer server;
		Queue queue{{"11 slot=15:0,16:2,15:0,14:0,13:0"}};
		Check(server.Draw(0, 15, queue.Next()).forced.has_value(), "the first reel is forced");
		const auto decision = server.Draw(0, 19, queue.Next());
		Check(!decision.forced, "a draw that differs from the expected one is not forced");
		Check(Logged(decision, "refused 11: draw 2 of slot expected 0..16"), "and the mismatch is named");
		Check(!server.Serving(), "the rest of the outcome is abandoned");
	}

	{
		saswb::OutcomeServer server;
		Queue queue{{"12 slot=15:x"}};
		Check(Logged(server.Draw(0, 15, queue.Next()), "refused 12: '15:x' is not <max>:<value>"), "a malformed pair is refused");
		Queue nameless{{"   "}};
		Check(Logged(server.Draw(0, 15, nameless.Next()), "refused -: the line has no outcome id"), "a blank line is refused");
		Queue nonzero{{"13 wheel=19:3"}};
		Check(!server.Draw(1, 19, nonzero.Next()).forced, "a draw that does not start at zero is never forced");
	}

	if (failures == 0)
		std::printf("outcome queue: all checks passed\n");
	return failures == 0 ? 0 : 1;
}
