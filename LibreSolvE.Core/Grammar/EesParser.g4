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
    | DIRECTIVE             # DirectiveStatement
    | PLOT_CMD              # PlotStatement
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
    : MINUS expression                                  # UnaryMinusExpr
    | left=expression op=POW right=expression           # PowExpr
    | left=expression op=(MUL | DIV) right=expression   # MulDivExpr
    | left=expression op=(PLUS | MINUS) right=expression # AddSubExpr
    | atom                                              # AtomExpr
    ;

// --- Basic Building Blocks ---
atom
    : NUMBER                # NumberAtom
    | STRING_LITERAL        # StringAtom
    | ID                    # VariableAtom
    | functionCall          # FuncCallAtom  // Added function call
    | LPAREN expression RPAREN # ParenExpr  // Parentheses for grouping
    ;

// Function call syntax
functionCall
    : fname=ID LPAREN exprList? RPAREN
    ;

// Expression list (for function arguments)
exprList
    : expression (COMMA expression)*
    ;

// --- Units (Placeholder - handle properly later) ---
// Units often appear in comments or [] after a value/variable
// We will need to handle this association after basic parsing
