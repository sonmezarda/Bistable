namespace Bistable.Core.Design.Ast;

public enum UnaryOp
{
    Not,        // ~
    LogicNot,   // !
    Negate,     // unary -
    ReduceAnd,  // &
    ReduceOr,   // |
    ReduceXor   // ^
}
