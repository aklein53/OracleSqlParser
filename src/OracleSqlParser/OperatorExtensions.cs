namespace OracleSqlParser;

/// <summary>SQL text of the operator enums. This is the single mapping from an operator to its canonical spelling; the script generator uses it.</summary>
public static class OperatorExtensions
{
    /// <summary>The canonical SQL spelling of a binary operator, such as <c>+</c>, <c>&lt;&gt;</c> or <c>MULTISET UNION ALL</c>.</summary>
    public static string ToSql(this BinaryOperator op) => op switch
    {
        BinaryOperator.Add => "+",
        BinaryOperator.Subtract => "-",
        BinaryOperator.Multiply => "*",
        BinaryOperator.Divide => "/",
        BinaryOperator.Power => "**",
        BinaryOperator.Concatenate => "||",
        BinaryOperator.Equals => "=",
        BinaryOperator.NotEquals => "<>",
        BinaryOperator.LessThan => "<",
        BinaryOperator.GreaterThan => ">",
        BinaryOperator.LessThanOrEqual => "<=",
        BinaryOperator.GreaterThanOrEqual => ">=",
        BinaryOperator.And => "AND",
        BinaryOperator.Or => "OR",
        BinaryOperator.MemberOf => "MEMBER OF",
        BinaryOperator.NotMemberOf => "NOT MEMBER OF",
        BinaryOperator.SubmultisetOf => "SUBMULTISET OF",
        BinaryOperator.NotSubmultisetOf => "NOT SUBMULTISET OF",
        BinaryOperator.MultisetUnion => "MULTISET UNION",
        BinaryOperator.MultisetUnionAll => "MULTISET UNION ALL",
        BinaryOperator.MultisetUnionDistinct => "MULTISET UNION DISTINCT",
        BinaryOperator.MultisetIntersect => "MULTISET INTERSECT",
        BinaryOperator.MultisetIntersectAll => "MULTISET INTERSECT ALL",
        BinaryOperator.MultisetIntersectDistinct => "MULTISET INTERSECT DISTINCT",
        BinaryOperator.MultisetExcept => "MULTISET EXCEPT",
        BinaryOperator.MultisetExceptAll => "MULTISET EXCEPT ALL",
        BinaryOperator.MultisetExceptDistinct => "MULTISET EXCEPT DISTINCT",
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, null),
    };

    /// <summary>The canonical SQL spelling of a unary operator, such as <c>-</c>, <c>NOT</c> or <c>CONNECT_BY_ROOT</c>.</summary>
    public static string ToSql(this UnaryOperator op) => op switch
    {
        UnaryOperator.Plus => "+",
        UnaryOperator.Minus => "-",
        UnaryOperator.Not => "NOT",
        UnaryOperator.Prior => "PRIOR",
        UnaryOperator.ConnectByRoot => "CONNECT_BY_ROOT",
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, null),
    };
}
