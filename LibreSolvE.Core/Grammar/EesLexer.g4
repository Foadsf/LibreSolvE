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
ID      : [a-zA-Z] [a-zA-Z0-9_]* ('$' | LBRACK)? ; // Basic ID, allows $ or [ at end (for strings/arrays later)

// Directives
DIRECTIVE : '$' [a-zA-Z]+ (~[\r\n])* ;

// --- Comments ---
// EES curly brace comments { ... } - can be nested, but ANTLR simple version doesn't handle nesting easily without modes
// Let's start with non-nested for simplicity
COMMENT_BRACE : '{' ~[{}]+ '}' -> channel(HIDDEN); // Matches { followed by any chars except {} until }

// EES double quote comments " ... "
COMMENT_QUOTE : '"' ('""'|~'"')*? '"' -> channel(HIDDEN); // Matches ", allows escaped "" inside, non-greedy until "

// EES single line comment // ...
COMMENT_SLASH : '//' ~[\r\n]* -> channel(HIDDEN);

// --- Whitespace ---
WS      : [ \t\r\n]+ -> channel(HIDDEN); // Match one or more whitespace characters

// --- Fragments (Helper rules, not tokens themselves) ---
fragment INT   : [0-9]+ ;
fragment FLOAT : INT '.' INT? EXP? | '.' INT EXP? | INT EXP ; // Handle various float formats
fragment EXP   : [eE] [+\-]? INT ; // Scientific notation exponent
