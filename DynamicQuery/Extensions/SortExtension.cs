using System.Linq.Expressions;
using DotNetCoreExtension.Extensions.Expressions;
using DotNetCoreExtension.Extensions.Reflections;
using DynamicQuery.Constants;
using DynamicQuery.Models;

namespace DynamicQuery.Extensions;

public static class SortExtension
{
    /// <summary>
    /// Dynamic sort but do not do nested sort for array properties
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="entities"></param>
    /// <param name="sortBy"></param>
    /// <param name="thenby"></param>
    /// <returns></returns>
    /// <exception cref="NotFoundException"></exception>
    public static IQueryable<T> Sort<T>(
        this IQueryable<T> entities,
        string sortBy,
        bool isNullCheck = false
    )
    {
        if (string.IsNullOrWhiteSpace(sortBy))
        {
            return entities;
        }

        ParameterExpression parameter = Expression.Parameter(typeof(T), "x");
        string[] sorts = sortBy.Trim().Split(",", StringSplitOptions.TrimEntries);

        Expression expression = entities.Expression;
        bool hasThenBy = false;
        foreach (string sort in sorts)
        {
            string[] orderField = sort.Split(OrderTerm.DELIMITER);
            string field = orderField[0];

            if (!typeof(T).IsNestedPropertyValid(field))
            {
                throw new ArgumentException(
                    $"Property '{field}' was not found on type '{typeof(T).Name}'.",
                    nameof(sortBy)
                );
            }

            string order = orderField.Length == 1 ? OrderTerm.ASC : orderField[1];

            string command =
                order == OrderTerm.DESC
                    ? hasThenBy
                        ? OrderType.ThenByDescending
                        : OrderType.Descending
                    : hasThenBy
                        ? SortType.ThenBy
                        : SortType.OrderBy;

            var member = parameter.MemberExpression<T>(field, isNullCheck);
            UnaryExpression converted = Expression.Convert(member, typeof(object));
            Expression<Func<T, object>> lamda = Expression.Lambda<Func<T, object>>(
                converted,
                parameter
            );

            expression = Expression.Call(
                typeof(Queryable),
                command,
                [typeof(T), lamda.ReturnType],
                expression,
                Expression.Quote(lamda)
            );

            hasThenBy = true;
        }

        return entities.Provider.CreateQuery<T>(expression);
    }

    public static IEnumerable<T> Sort<T>(this IEnumerable<T> source, string sortBy)
    {
        if (string.IsNullOrWhiteSpace(sortBy))
        {
            return source;
        }

        string[] sorts = sortBy.Split(",", StringSplitOptions.TrimEntries);

        IOrderedEnumerable<T>? ordered = null;
        const bool isNullCheck = true;
        foreach (string sort in sorts)
        {
            string[] orderField = sort.Split(OrderTerm.DELIMITER);
            string field = orderField[0];

            if (!typeof(T).IsNestedPropertyValid(field))
            {
                throw new ArgumentException(
                    $"Property '{field}' was not found on type '{typeof(T).Name}'.",
                    nameof(sortBy)
                );
            }

            string order = orderField.Length == 1 ? OrderTerm.ASC : orderField[1];

            string cacheKey = $"SORT:{typeof(T).FullName}:{sort}:{isNullCheck}";
            Func<T, object?> keySelector = DelegateDictionaryCache.GetOrAdd(
                cacheKey,
                () =>
                {
                    ParameterExpression param = Expression.Parameter(typeof(T), "x");
                    Expression body = param.MemberExpression<T>(field, isNullCheck);
                    UnaryExpression converted = Expression.Convert(body, typeof(object));

                    return Expression.Lambda<Func<T, object?>>(converted, param).Compile();
                }
            );

            if (ordered == null)
            {
                ordered =
                    order == OrderTerm.DESC
                        ? source.OrderByDescending(keySelector)
                        : source.OrderBy(keySelector);
            }
            else
            {
                ordered =
                    order == OrderTerm.DESC
                        ? ordered.ThenByDescending(keySelector)
                        : ordered.ThenBy(keySelector);
            }
        }

        return ordered ?? source;
    }
}
