/**
 * Derives a session title from the first user prompt only.
 *
 * The title is assigned once per session and then latched by the store, so this runs a single time
 * per conversation. That is why it can afford to be picky: it strips greetings and polite framing,
 * keeps identifiers intact, and produces 3–5 words that describe the intent rather than the wording.
 */

/** Greetings and vocatives that carry no intent. Applied repeatedly, so "hey there, hi" clears. */
const LEADING_FILLERS: RegExp[] = [
  // Vocatives first: the bare greeting pattern below would otherwise leave "agent" behind.
  /^(?:hey|hi|hello)\s+(?:agent|there|team|bot|assistant|guys)\b[\s,!.:-]*/i,
  /^(?:hi|hello|hey|heya|yo|sup|greetings|howdy)\b[\s,!.:-]*/i,
  /^(?:good\s+(?:morning|afternoon|evening|day))\b[\s,!.:-]*/i,
  /^(?:thanks|thank you|cheers)\b[\s,!.:-]*/i,
  /^(?:ok|okay|alright|so|well|now|right)\b[\s,!.:-]*/i,
  // Interrogative framing addressed at the assistant ("how do I …" → "…"), which turns the rest of
  // the prompt into the actionable phrase a title wants. "what is this image" is left untouched.
  /^(?:how|what|why|where|when|which)\s+(?:do|does|can|could|should|would|will|did)\s+(?:i|we|you)\b[\s,]*/i,
  /^(?:is|are)\s+there\s+(?:a|any)\s+way\s+to\b[\s,]*/i,
];

/** Polite or modal framing in front of the real request. */
const POLITE_PREFIX: RegExp[] = [
  /^(?:(?:can|could|would|will|may)\s+you(?:\s+please)?)\b[\s,]*/i,
  /^(?:please|kindly|pls)\b[\s,]*/i,
  /^(?:i(?:'d| would)\s+(?:like|love|want)\s+(?:you\s+)?to)\b[\s,]*/i,
  /^(?:i\s+(?:want|need)\s+(?:you\s+)?to)\b[\s,]*/i,
  /^(?:help\s+me(?:\s+to)?|help\s+with|assist\s+me(?:\s+with)?)\b[\s,]*/i,
  /^(?:let'?s|go\s+ahead\s+and|start\s+by|try\s+to)\b[\s,]*/i,
  /^(?:tell\s+me|show\s+me|let\s+me\s+(?:see|know))\b[\s,]*/i,
  /^(?:i\s+(?:have|got)\s+a\s+question\s+about)\b[\s,]*/i,
];

/** Kept when they open the prompt, because they carry the shape of the request. */
const QUESTION_OPENERS = /^(?:what|why|how|when|where|who|which|whose|is|are|does|do|did|should)\b/i;

/** Dropped from the middle of a title: they add length without adding intent. */
const INNER_STOPWORDS = new Set([
  'a', 'an', 'the', 'of', 'to', 'for', 'in', 'on', 'at', 'by', 'with', 'from', 'into', 'onto',
  'and', 'or', 'but', 'so', 'then', 'than', 'as', 'if', 'while', 'about', 'like', 'is', 'are', 'was', 'were', 'be', 'been', 'am',
  'this', 'that', 'these', 'those', 'it', 'its',
  'my', 'me', 'we', 'us', 'our', 'i', 'you', 'your',
  'please', 'just', 'really', 'actually', 'also', 'some', 'any', 'all',
  'can', 'could', 'would', 'should', 'will', 'do', 'does', 'did',
]);

const MAX_TITLE_WORDS = 5;
const MAX_TITLE_CHARS = 48;

function stripLeadingNoise(prompt: string): string {
  // Prompts are often wrapped in markup ("<USER_REQUEST>…</USER_REQUEST>"). The tag is not a name,
  // and before this the generator happily titled a session "<USER_REQUEST>".
  let text = prompt.replace(/<[^>]{1,60}>/g, ' ').replace(/\s+/g, ' ').trim();

  // Leading slash command: keep what follows it, since that is the actual instruction.
  text = text.replace(/^\/[a-zA-Z0-9_-]+\s+/, '');

  for (let pass = 0; pass < 5; pass += 1) {
    const before = text;
    for (const pattern of LEADING_FILLERS) text = text.replace(pattern, '');
    for (const pattern of POLITE_PREFIX) text = text.replace(pattern, '');
    text = text.trim();
    if (text === before) break;
  }

  return text;
}

function firstClause(text: string): string {
  // Code fences, paths and URLs at the start say nothing about intent.
  const withoutFence = text.replace(/```[\s\S]*?```/g, ' ');
  const withoutUrls = withoutFence.replace(/https?:\/\/\S+/g, ' ');
  const withoutMentions = withoutUrls.replace(/@[\w./\\-]+/g, ' ');

  const line = withoutMentions.split(/[\r\n]/)[0] || '';
  // Split on sentence punctuation only when it ends a sentence, so `store.ts` and `v1.2` survive.
  const clause = line.split(/[?!;]|\.\s+|,\s|\s+-\s/)[0] || line;
  return clause.replace(/^#+\s*/, '').trim();
}

/** Keeps identifiers readable: file names, camelCase, snake_case, versions and acronyms. */
function isIdentifier(word: string): boolean {
  return /[._/\\\d]/.test(word) || /[a-z][A-Z]/.test(word) || (word.length > 1 && word === word.toUpperCase());
}

function capitalise(word: string): string {
  if (!word) return word;
  if (isIdentifier(word)) return word;
  return word.charAt(0).toUpperCase() + word.slice(1);
}

function truncateChars(title: string): string {
  if (title.length <= MAX_TITLE_CHARS) return title;
  const clipped = title.slice(0, MAX_TITLE_CHARS);
  const lastSpace = clipped.lastIndexOf(' ');
  return (lastSpace > MAX_TITLE_CHARS * 0.55 ? clipped.slice(0, lastSpace) : clipped).trim();
}

export function generateSessionTitle(prompt: string): string {
  if (!prompt || typeof prompt !== 'string') return 'New Session';

  const cleaned = stripLeadingNoise(prompt);
  const clause = firstClause(cleaned);
  if (!clause) return 'New Session';

  const words = clause.split(/\s+/).filter(Boolean);
  if (words.length === 0) return 'New Session';

  // A question keeps its first few function words — "What Is This Image" reads better than
  // "What Image" — but drops them later so the title still lands on content.
  const keepFunctionWords = QUESTION_OPENERS.test(clause);
  const verbatimLimit = keepFunctionWords ? 3 : 1;
  const selected: string[] = [];

  for (const word of words) {
    const normalised = word.replace(/^[^\w]+|[^\w]+$/g, '');
    if (!normalised) continue;

    const isBeyondVerbatim = selected.length >= verbatimLimit;
    if (isBeyondVerbatim && INNER_STOPWORDS.has(normalised.toLowerCase())) continue;

    selected.push(normalised);
    if (selected.length >= MAX_TITLE_WORDS) break;
  }

  if (selected.length === 0) {
    return truncateChars(capitalise(words[0])) || 'New Session';
  }

  const title = truncateChars(selected.map(capitalise).join(' '));
  return title || 'New Session';
}
