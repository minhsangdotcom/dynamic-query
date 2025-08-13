using System.Linq.Expressions;

namespace DynamicQuery.Results;

public record ConvertExpressionTypeResult(Expression Member, ConstantExpression Value);
