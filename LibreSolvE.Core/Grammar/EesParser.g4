// Grammar/EesParser.g4
parser grammar EesParser;

// Import tokens from the Lexer
options { tokenVocab=EesLexer; }

// --- Entry Rule ---
// An EES file is a sequence of statements
eesFile : statement* EOF;

// --- Statements ---
// For now, only equations and assignments
statement
    : assignment            # AssignmentStatement
    | equation              # EquationStatement
  //| functionDefinition    # FuncDefStatement    // Add later
  //| procedureDefinition   # ProcDefStatement    // Add later
  //| moduleDefinition      # ModuleDefStatement  // Add later
  //| directive             # DirectiveStatement  // Add later
  //| callStatement         # CallStatement       // Add later
    ;

// Equation uses = operator for equality
equation : lhs=expression EQ rhs=expression SEMI? ;

// Assignment can use either := (preferred) or = (for compatibility)
assignment
    : variable=ID ASSIGN rhs=expression SEMI?   # ExplicitAssignment
    | variable=ID EQ rhs=expression SEMI?       # ImplicitAssignment
    ;

// --- Expressions (Start simple, build precedence) ---
// Basic arithmetic - add more levels for precedence later
expression
    : left=expression op=(MUL | DIV) right=expression    # MulDivExpr // Higher precedence
    | left=expression op=(PLUS | MINUS) right=expression # AddSubExpr // Lower precedence
    | atom                                              # AtomExpr   // Base case
    ;

// --- Basic Building Blocks ---
atom
    : NUMBER                # NumberAtom
    | ID                    # VariableAtom
  //| functionCall          # FuncCallAtom // Add later
    | LPAREN expression RPAREN # ParenExpr // Parentheses for grouping
    ;

// --- Units (Placeholder - handle properly later) ---
// Units often appear in comments or [] after a value/variable
// We will need to handle this association after basic parsing
