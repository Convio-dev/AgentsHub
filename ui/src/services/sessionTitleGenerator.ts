/**
 * Smart Session Title Generator
 * Generates concise, professional 3-5 word summaries of user intent.
 */

export function generateSessionTitle(prompt: string): string {
  if (!prompt || typeof prompt !== 'string') {
    return 'New Session';
  }

  // Clean prompt and remove common chatter prefixes
  let cleaned = prompt
    .trim()
    .replace(/^(\/?[a-zA-Z0-9_\-]+:\s*)/, '') // remove command prefixes like "act:" or "/chat:"
    .replace(/^(can you|could you|please|help me to|how do i|i want to|i need to|write a|create a|implement a|build a|fix)\s+/i, '')
    .trim();

  // If first line has markdown headers, strip them
  cleaned = cleaned.replace(/^#+\s*/, '');

  // Take only the first sentence or first line
  const firstLine = cleaned.split(/[\r\n.?!]/)[0].trim();
  if (!firstLine) {
    return 'New Session';
  }

  // Tokenize words
  const words = firstLine.split(/\s+/).filter(w => w.length > 0);
  if (words.length <= 4) {
    return capitalizeWords(words.join(' '));
  }

  // Take the first 3 to 5 words
  const selected = words.slice(0, 4);
  return capitalizeWords(selected.join(' '));
}

function capitalizeWords(str: string): string {
  return str
    .split(' ')
    .map(word => {
      // Don't modify code tokens like .ts, camelCase, or UPPERCASE
      if (word.includes('.') || word.toUpperCase() === word || /[a-z][A-Z]/.test(word)) {
        return word;
      }
      return word.charAt(0).toUpperCase() + word.slice(1);
    })
    .join(' ');
}
