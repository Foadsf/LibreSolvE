// Grammar/EesLexer.g4
lexer grammar EesLexer;

// --- Channels ---
// We'll put comments and whitespace on a hidden channel so the parser ignores them by default
// HIDDEN is a predefined channel, no need to declare it

// --- Keywords (Reserved words, potentially more later) ---
// None defined yet

// --- Operators ---
PLUS    : '+';
MINUS   : '-';
MUL     : '*';
DIV     : '/';
POW     : '^' | '**'; // Allow both ^ and ** for power
EQ      : '=';        // Equation operator
ASSIGN  : ':=';       // Assignment operator
LPAREN  : '(';
RPAREN  : ')';
LBRACK  : '[';
RBRACK  : ']';
COMMA   : ',';
SEMI    : ';';  // Add semicolon token
// More operators later (e.g., <, >, <=, >=, <>)

// --- Literals ---
NUMBER  : INT | FLOAT; // Combine integer and float logic
STRING_LITERAL: '\'' ( '\'\'' | ~['] )* '\'' ; // String literals with single quotes

// --- Identifiers ---
// The trailing LBRACK option (for future array indices) was dropped: it
// was dead -- EesParser.g4 never references LBRACK/RBRACK anywhere, so an
// ID that had swallowed a literal '[' into its own text could never
// actually be consumed by any parser rule. Real array support (PLAN.md
// Phase 5) needs to design this properly rather than inherit a stray
// artifact from before UNIT_ANNOTATION existed below.
ID      : [a-zA-Z] [a-zA-Z0-9_]* '$'? ; // Basic ID, '$' suffix for string variables

// Directives
DIRECTIVE : '$' [a-zA-Z]+ (~[\r\n])* ;

// --- Comments ---
// EES curly brace comments { ... } - can be nested, but ANTLR simple version doesn't handle nesting easily without modes
// Let's start with non-nested for simplicity
COMMENT_BRACE : '{' ~[{}]+ '}' -> channel(HIDDEN); // Matches { followed by any chars except {} until }

// EES double quote comments " ... "
COMMENT_QUOTE : '"' ('""'|~'"')*? '"' -> channel(HIDDEN); // Matches ", allows escaped "" inside, non-greedy until "

// '//' is NOT valid EES (COMPATIBILITY.md Rule 2: only { } and " " are
// comment forms -- manual, general rules, item 3). Deliberately no
// COMMENT_SLASH rule: a bare '//' now lexes as two DIV tokens, which the
// parser grammar rejects, so it surfaces as a real syntax error instead of
// silently being swallowed as a comment.

// Real EES inline units: `T=50 [C]`, unquoted, immediately after a value
// (COMPATIBILITY.md SS4, confirmed on two independent modern EES sources --
// the 2000 manual predates this and only documents the GUI route).
// Hidden from the parser exactly like a comment: EES treats the unit as a
// display/dimensional-checking annotation, never as part of the arithmetic
// (COMPATIBILITY.md's own commitment: "do not carry units into the numeric
// solve"), and this token being HIDDEN is what makes that true here too --
// UnitParser.ExtractUnitsFromSource already regex-scans raw source text for
// `[...]` generically (it never distinguished quoted from bare brackets),
// so no other code needed to change for bare units to work.
//
// KNOWN TENSION, not silently punted: this will also swallow `X[5]`-style
// array indices once Phase 5 implements them, because an ANTLR lexer rule
// cannot look back to ask "did a NUMBER or an ID just precede this
// bracket?". Phase 5 needs to actually solve that disambiguation (e.g. a
// lexer mode entered only after NUMBER), not inherit this token unchanged.
UNIT_ANNOTATION : '[' ~[\r\n\]]+ ']' -> channel(HIDDEN);

// --- Whitespace ---
WS      : [ \t\r\n]+ -> channel(HIDDEN); // Match one or more whitespace characters

// PLOT_CMD deliberately removed (COMPATIBILITY.md Rule 5: PLOT is not a
// real EES statement). 'PLOT' at top level now lexes as a plain ID, which
// the parser's statement rule rejects unless it happens to start a valid
// assignment/equation -- a real syntax error, matching real EES. The
// LibreSolvE-only {$PLOT ...} form is extracted from comment text by
// PlotDirectiveParser, entirely outside this grammar.

// --- Fragments (Helper rules, not tokens themselves) ---
fragment INT   : [0-9]+ ;
fragment FLOAT : INT '.' INT? EXP? | '.' INT EXP? | INT EXP ; // Handle various float formats
fragment EXP   : [eE] [+\-]? INT ; // Scientific notation exponent
