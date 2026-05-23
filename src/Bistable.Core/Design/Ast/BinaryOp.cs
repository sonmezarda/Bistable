namespace Bistable.Core.Design.Ast;

public enum BinaryOp
{
    Add, Sub, Mul, Div, Mod,
    And, Or, Xor,
    LogicAnd, LogicOr,
    Equal, NotEqual,
    LessThan, GreaterThan, LessOrEqual, GreaterOrEqual,
    ShiftLeft, ShiftRight, ShiftRightArithmetic
}
