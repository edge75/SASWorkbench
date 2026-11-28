/*
 *	The scripted-outcome queue, as the demo's draws read it (D64).
 *
 *	No file, no clock and nothing of the demo here, so the rules can be checked on their own
 *	(test/outcome_queue_check.cpp). The RNG substitution (saswb_scripted_rng.cpp) does the I/O.
 *
 *	One outcome per line of the queue file, written by the test host:
 *
 *		<id> <alternative>=<max>:<value>[,<max>:<value>...] [<alternative>=...]
 *
 *	e.g.	7 slot=15:0,16:2,15:0,14:0,13:0 wheel=19:0
 *
 *	An outcome is the values of the next game's draws. It names one alternative per kind of game,
 *	because the host does not always know which game the player is on; the first draw of the game
 *	picks the alternative whose first range it matches, and every later draw must match its own range
 *	too. A value outside its range, a draw whose range no alternative expects, or a draw that differs
 *	from the one the alternative expected is refused and logged - never clamped, never reinterpreted -
 *	and that draw, like any draw with an empty queue, is the demo's own generator's.
 */

#ifndef SASWB_OUTCOME_QUEUE_H
#define SASWB_OUTCOME_QUEUE_H

#include <cstdint>
#include <functional>
#include <optional>
#include <sstream>
#include <string>
#include <utility>
#include <vector>

namespace saswb
{

struct ForcedDraw
{
	uint32_t	max_inclusive;
	uint32_t	value;
};

struct ScriptedAlternative
{
	std::string				name;
	std::vector<ForcedDraw>	draws;
};

struct ScriptedOutcome
{
	std::string							id;
	std::vector<ScriptedAlternative>	alternatives;
};

//	What one draw came to: the value to return when forced, and the lines the log gets either way.
struct DrawDecision
{
	std::optional<uint32_t>		forced;
	std::vector<std::string>	log;
};

inline bool ParseUint32(const std::string &text, uint32_t &out)
{
	if (text.empty() || text.size() > 10)
		return false;
	uint64_t value = 0;
	for (char c : text)
	{
		if (c < '0' || c > '9')
			return false;
		value = value * 10 + static_cast<uint64_t>(c - '0');
	}
	if (value > 0xFFFFFFFFull)
		return false;
	out = static_cast<uint32_t>(value);
	return true;
}

inline bool IsName(const std::string &text)
{
	if (text.empty())
		return false;
	for (char c : text)
	{
		const bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
		if (!ok)
			return false;
	}
	return true;
}

//	Reads one queue line; on refusal says why, and the outcome is left unusable.
inline bool ParseOutcome(const std::string &line, ScriptedOutcome &out, std::string &why)
{
	out = ScriptedOutcome{};
	std::istringstream words(line);
	if (!(words >> out.id) || !IsName(out.id))
	{
		why = "the line has no outcome id";
		return false;
	}

	std::string word;
	while (words >> word)
	{
		const size_t equals = word.find('=');
		ScriptedAlternative alternative;
		alternative.name = word.substr(0, equals == std::string::npos ? 0 : equals);
		if (equals == std::string::npos || !IsName(alternative.name))
		{
			why = "'" + word + "' is not <alternative>=<max>:<value>,...";
			return false;
		}

		std::istringstream pairs(word.substr(equals + 1));
		std::string pair;
		while (std::getline(pairs, pair, ','))
		{
			const size_t colon = pair.find(':');
			ForcedDraw draw{};
			if (colon == std::string::npos
				|| !ParseUint32(pair.substr(0, colon), draw.max_inclusive)
				|| !ParseUint32(pair.substr(colon + 1), draw.value))
			{
				why = "'" + pair + "' is not <max>:<value>";
				return false;
			}
			if (draw.value > draw.max_inclusive)
			{
				why = "value " + std::to_string(draw.value) + " is outside 0.." + std::to_string(draw.max_inclusive);
				return false;
			}
			alternative.draws.push_back(draw);
		}

		if (alternative.draws.empty())
		{
			why = "alternative '" + alternative.name + "' has no draws";
			return false;
		}
		out.alternatives.push_back(alternative);
	}

	if (out.alternatives.empty())
	{
		why = "outcome " + out.id + " names no alternative";
		return false;
	}
	return true;
}

//	Serves one outcome's draws in order; the caller supplies the next queue line when one is wanted.
class OutcomeServer
{
public:
	using NextLine = std::function<std::optional<std::string>()>;

	DrawDecision Draw(const uint32_t min_inclusive, const uint32_t max_inclusive, const NextLine &next_line)
	{
		DrawDecision decision;
		const std::string range = std::to_string(min_inclusive) + ".." + std::to_string(max_inclusive);

		if (!Serving())
		{
			const std::optional<std::string> line = next_line();
			if (!line)
			{
				decision.log.push_back("drawn " + range + " random");
				return decision;
			}

			ScriptedOutcome outcome;
			std::string why;
			if (!ParseOutcome(*line, outcome, why))
			{
				decision.log.push_back("refused " + (outcome.id.empty() ? std::string("-") : outcome.id) + ": " + why);
				decision.log.push_back("drawn " + range + " random");
				return decision;
			}

			const ScriptedAlternative *chosen = nullptr;
			for (const auto &alternative : outcome.alternatives)
			{
				if (min_inclusive == 0 && alternative.draws.front().max_inclusive == max_inclusive)
				{
					chosen = &alternative;
					break;
				}
			}
			if (!chosen)
			{
				decision.log.push_back("refused " + outcome.id + ": no alternative starts with a draw of " + range);
				decision.log.push_back("drawn " + range + " random");
				return decision;
			}

			fId = outcome.id;
			fAlternative = chosen->name;
			fDraws = chosen->draws;
			fNext = 0;
		}

		const ForcedDraw &expected = fDraws[fNext];
		if (min_inclusive != 0 || expected.max_inclusive != max_inclusive)
		{
			decision.log.push_back("refused " + fId + ": draw " + std::to_string(fNext + 1) + " of " + fAlternative
				+ " expected 0.." + std::to_string(expected.max_inclusive) + ", the game drew " + range);
			decision.log.push_back("drawn " + range + " random");
			Abandon();
			return decision;
		}

		decision.forced = expected.value;
		decision.log.push_back("forced " + fId + " " + fAlternative + " " + std::to_string(fNext + 1) + "/"
			+ std::to_string(fDraws.size()) + " " + range + " -> " + std::to_string(expected.value));
		if (++fNext == fDraws.size())
		{
			decision.log.push_back("done " + fId + " " + fAlternative);
			Abandon();
		}
		return decision;
	}

	bool Serving() const { return !fDraws.empty(); }

	void Abandon()
	{
		fId.clear();
		fAlternative.clear();
		fDraws.clear();
		fNext = 0;
	}

private:
	std::string				fId;
	std::string				fAlternative;
	std::vector<ForcedDraw>	fDraws;
	size_t					fNext = 0;
};

}	//	namespace saswb

#endif	//	SASWB_OUTCOME_QUEUE_H
