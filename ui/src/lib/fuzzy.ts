/**
 * Fuzzy subsequence scoring for the slash command palette.
 *
 * A simple `includes` filter fails the way people actually type: `/pr` should find `/switch-profile`,
 * and `/cm` should find `/compact`. Scores reward prefix and word-boundary hits so the obvious
 * command sorts first.
 */

export interface FuzzyResult {
  score: number;
  /** Indices of matched characters in the target, for highlighting. */
  matched: number[];
}

const BOUNDARY = /[\s\-_./:]/;

/**
 * Returns null when `query` is not a subsequence of `target`. Scores are relative, not absolute:
 * only their ordering matters.
 */
export function fuzzyMatch(query: string, target: string): FuzzyResult | null {
  if (!query) return { score: 0, matched: [] };

  const haystack = target.toLowerCase();
  const needle = query.toLowerCase().replace(/\s+/g, '');
  if (!needle) return { score: 0, matched: [] };

  const matched: number[] = [];
  let score = 0;
  let cursor = 0;

  for (let i = 0; i < needle.length; i += 1) {
    const char = needle[i];
    const found = haystack.indexOf(char, cursor);
    if (found === -1) return null;

    // Contiguity and word-boundary hits are what make a match feel intentional.
    if (found === cursor) score += 6;
    if (found === 0 || BOUNDARY.test(haystack[found - 1])) score += 8;
    score -= Math.min(found - cursor, 8);

    matched.push(found);
    cursor = found + 1;
  }

  // Prefer shorter targets when the match quality is otherwise equal.
  score += Math.max(0, 12 - target.length / 2);
  return { score, matched };
}

/** Ranks commands by name first, then by description, dropping non-matches. */
export function rankByQuery<T>(
  items: T[],
  query: string,
  nameOf: (item: T) => string,
  descriptionOf: (item: T) => string
): Array<{ item: T; score: number }> {
  if (!query.trim()) return items.map(item => ({ item, score: 0 }));

  const scored: Array<{ item: T; score: number }> = [];

  for (const item of items) {
    const nameMatch = fuzzyMatch(query, nameOf(item));
    if (nameMatch) {
      // A name match always outranks a description-only match.
      scored.push({ item, score: nameMatch.score + 100 });
      continue;
    }
    const descriptionMatch = fuzzyMatch(query, descriptionOf(item));
    if (descriptionMatch) scored.push({ item, score: descriptionMatch.score });
  }

  return scored.sort((a, b) => b.score - a.score);
}
