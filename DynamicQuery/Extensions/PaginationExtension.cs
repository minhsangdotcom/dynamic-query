using System.Linq.Expressions;
using System.Reflection;
using DotNetCoreExtension.Extensions;
using DotNetCoreExtension.Extensions.Expressions;
using DotNetCoreExtension.Extensions.Reflections;
using DynamicQuery.Constants;
using DynamicQuery.Models;
using DynamicQuery.Results;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace DynamicQuery.Extensions;

public static class PaginationExtension
{
    /// <summary>
    /// offset pagination for IQueryable
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="entities"></param>
    /// <param name="current"></param>
    /// <param name="size"></param>
    /// <returns></returns>
    public static async Task<PaginatedResult<T>> ToPagedListAsync<T>(
        this IQueryable<T> query,
        int current,
        int size,
        CancellationToken cancellationToken = default
    )
    {
        int totalPage = query.Count();

        return new PaginatedResult<T>(
            await query.Skip((current - 1) * size).Take(size).ToListAsync(cancellationToken),
            totalPage,
            current,
            size
        );
    }

    /// <summary>
    /// offset pagination for IEnumerable
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="entities"></param>
    /// <param name="current"></param>
    /// <param name="size"></param>
    /// <returns></returns>
    public static PaginatedResult<T> ToPagedList<T>(
        this IEnumerable<T> query,
        int current,
        int size
    ) => new(query.Skip((current - 1) * size).Take(size), query.Count(), current, size);

    /// <summary>
    /// Cursor pagination
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="query"></param>
    /// <param name="request"></param>
    /// <returns></returns>
    public static async Task<PaginatedResult<T>> ToCursorPagedListAsync<T>(
        this IQueryable<T> query,
        CursorPaginationRequest request
    )
    {
        string sort = RemoveAscOrder($"{request.Sort},{request.UniqueSort}");
        int totalPage = query.Count();
        if (totalPage == 0)
        {
            return new PaginatedResult<T>(await query.ToListAsync(), totalPage, request.Size);
        }

        bool IsPreviousMove = !string.IsNullOrWhiteSpace(request.Before);
        string originalSort = sort;

        IQueryable<T> sortedQuery = query.Sort(originalSort);
        T? first = sortedQuery.FirstOrDefault();
        T? last = sortedQuery.LastOrDefault();

        if (IsPreviousMove)
        {
            sort = ReverseSortOrder(originalSort);
            sortedQuery = sortedQuery.Sort(sort);
        }

        PaginationResult<T> result = await PaginateWithCursorAsync(
            new PaginationPayload<T>(
                sortedQuery,
                request.After ?? request.Before,
                IsPreviousMove,
                first!,
                last!,
                originalSort,
                sort,
                request.Size,
                totalPage,
                request.UniqueSort.Trim().Split(OrderTerm.DELIMITER)[0]
            )
        );

        return new PaginatedResult<T>(
            result.Data,
            totalPage,
            result.PageSize,
            result.Pre,
            result.Next
        );
    }

    /// <summary>
    /// do cursor paging
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="payload"></param>
    /// <returns></returns>
    private static async Task<PaginationResult<T>> PaginateWithCursorAsync<T>(
        PaginationPayload<T> payload
    )
    {
        // this is the first page
        if (string.IsNullOrWhiteSpace(payload.Cursor))
        {
            IQueryable<T> list = payload.Query.Take(payload.Size);
            T? theLast = list.LastOrDefault();

            List<T> pagedList = await list.ToListAsync();
            string? nextCursor =
                payload.ActualSize > payload.Size ? EncodeCursor(theLast, payload.Sort) : null;
            return new PaginationResult<T>(pagedList, payload.Size, nextCursor);
        }

        CursorPayload? cursorPayload =
            DecodeCursor(payload.Cursor) ?? throw new Exception("Cursor decode failed");
        string cursorSort = payload.IsPrevious
            ? ReverseSortOrder(cursorPayload.Sort)
            : cursorPayload.Sort;
        if (!string.Equals(payload.Sort, cursorSort, StringComparison.OrdinalIgnoreCase))
        {
            throw new Exception("Cursor sort mismatch");
        }

        IQueryable<T> data = MoveForwardOrBackwardAsync(
                payload.Query,
                cursorPayload.Properties,
                payload.Sort
            )
            .Take(payload.Size);
        IQueryable<T> sortedList = payload.IsPrevious ? data.Sort(payload.OriginalSort) : data;

        T? last = sortedList.LastOrDefault();
        T? first = sortedList.FirstOrDefault();
        int count = sortedList.Count();

        // whether or not we're currently at first or last page
        string? next = EncodeCursor(last, payload.Sort);
        string? pre = EncodeCursor(first, payload.Sort);

        var cursor = payload.IsPrevious ? first : last;
        var flag = payload.IsPrevious ? payload.First : payload.Last;
        if (
            count < payload.Size
            || (count == payload.Size && IsEndOfPage(cursor, flag, payload.UniqueKey))
        )
        {
            if (!payload.IsPrevious)
            {
                next = null;
            }
            if (payload.IsPrevious)
            {
                pre = null;
            }
        }

        return new PaginationResult<T>(await sortedList.ToListAsync(), payload.Size, next, pre);
    }

    /// <summary>
    /// move forward or backward <- | ->
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="query"></param>
    /// <param name="cursors">consist of property names and its value in cursor</param>
    /// <param name="sort"></param>
    /// <returns></returns>
    private static IQueryable<T> MoveForwardOrBackwardAsync<T>(
        IQueryable<T> query,
        Dictionary<string, object?>? cursors,
        string sort
    )
    {
        List<KeyValuePair<string, string>> sorts = TransformSort(sort);

        ParameterExpression parameter = Expression.Parameter(typeof(T), "x");
        Expression? body = null;

        List<KeyValuePair<MemberExpression, object?>> ComparisonValues = [];
        for (int i = 0; i < sorts.Count; i++)
        {
            KeyValuePair<string, string> orderField = sorts[i];
            string order = orderField.Value;
            string field = orderField.Key;

            //* x."property"
            Expression expressionMember = parameter.MemberExpression(typeof(T), field);
            object? value = cursors?[field];
            ComparisonValues.Add(new((MemberExpression)expressionMember, value));

            BinaryExpression andClause = BuildAndClause<T>(i, ComparisonValues, order);
            body = body == null ? andClause : Expression.OrElse(body, andClause);
        }

        //* x => x.Age < AgeValue ||
        //*     (x.Age == AgeValue && x.Name > NameValue) ||
        //*     (x.Age == AgeValue && x.Name == NameValue && x.Id > IdValue)
        var expression = Expression.Lambda<Func<T, bool>>(body!, parameter);
        return query.Where(expression);
    }

    /// <summary>
    /// build and clause (x.Age == AgeValue && x.Name > NameValue)
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="index"></param>
    /// <param name="ComparisonValues"></param>
    /// <param name="propertyName"></param>
    /// <param name="order"></param>
    /// <returns></returns>
    private static BinaryExpression BuildAndClause<T>(
        int index,
        List<KeyValuePair<MemberExpression, object?>> ComparisonValues,
        string order
    )
    {
        BinaryExpression? innerExpression = BuildEqualOperationClause(index, ComparisonValues);

        MemberExpression expressionMember = ComparisonValues[index].Key;
        object? value = ComparisonValues[index].Value;

        BinaryExpression binaryExpression = BuildCompareOperation(
            expressionMember.GetMemberExpressionType(),
            expressionMember,
            value,
            order
        );

        return innerExpression == null
            ? binaryExpression
            : Expression.AndAlso(innerExpression, binaryExpression);
    }

    private static BinaryExpression BuildCompareOperation(
        Type type,
        MemberExpression member,
        object? value,
        string order
    )
    {
        if (type == typeof(Ulid))
        {
            MethodCallExpression compareExpression = CompareUlidByExpression(member, value);
            ConstantExpression comparisonValue = Expression.Constant(0);

            return order == OrderTerm.DESC
                ? Expression.LessThan(compareExpression, comparisonValue)
                : Expression.GreaterThan(compareExpression, comparisonValue);
        }

        if (type == typeof(string))
        {
            ConstantExpression comparisonValue = Expression.Constant(0);
            MethodCallExpression comparisonExpression = CompareStringByExpression(
                member,
                Expression.Constant(value)
            );

            return order == OrderTerm.DESC
                ? Expression.LessThan(comparisonExpression, comparisonValue)
                : Expression.GreaterThan(comparisonExpression, comparisonValue);
        }

        ConvertExpressionTypeResult typeResult = ConvertType(member, value);
        return order == OrderTerm.DESC
            ? Expression.LessThan(typeResult.Member, typeResult.Value)
            : Expression.GreaterThan(typeResult.Member, typeResult.Value);
    }

    /// <summary>
    /// Build equal query like (x.Age == AgeValue && .....)
    /// </summary>
    /// <param name="index"></param>
    /// <param name="ComparisonValues"></param>
    /// <returns></returns>
    private static BinaryExpression? BuildEqualOperationClause(
        int index,
        List<KeyValuePair<MemberExpression, object?>> ComparisonValues
    )
    {
        BinaryExpression? body = null;
        for (int i = 0; i < index; i++)
        {
            var comparisonValue = ComparisonValues[i];
            MemberExpression memberExpression = comparisonValue.Key;
            object? value = comparisonValue.Value;

            BinaryExpression operation;
            if (comparisonValue.Key.GetMemberExpressionType() == typeof(Ulid))
            {
                MethodCallExpression comparison = CompareUlidByExpression(memberExpression, value);
                operation = Expression.Equal(comparison, Expression.Constant(0));
            }
            else
            {
                ConvertExpressionTypeResult typeResult = ConvertType(memberExpression, value);
                operation = Expression.Equal(typeResult.Member, typeResult.Value!);
            }

            body = body == null ? operation : Expression.AndAlso(body, operation);
        }
        return body;
    }

    private static ConvertExpressionTypeResult ConvertType(MemberExpression left, object? right)
    {
        Expression member = left;
        Type leftType = left.GetMemberExpressionType();
        Type? rightType = right?.GetType();

        if (leftType != rightType)
        {
            Type targetType = leftType;

            if (targetType.IsNullable() && targetType.GenericTypeArguments.Length > 0)
            {
                targetType = targetType.GenericTypeArguments[0];
            }

            object? changedTypeValue = right.ConvertTo(targetType);
            return new(member, Expression.Constant(changedTypeValue, leftType));
        }

        return new(member, Expression.Constant(right));
    }

    private static MethodCallExpression CompareUlidByExpression(Expression left, object? value)
    {
        Ulid comparisonValue = value == null ? Ulid.Empty : Ulid.Parse(value!.ToString());
        MethodInfo? compareToMethod = typeof(Ulid).GetMethod(
            nameof(Ulid.CompareTo),
            [typeof(Ulid)]
        );

        return Expression.Call(left, compareToMethod!, Expression.Constant(comparisonValue));
    }

    private static MethodCallExpression CompareStringByExpression(
        Expression left,
        Expression right
    ) => Expression.Call(typeof(string), nameof(string.Compare), null, [left, right]);

    /// <summary>
    /// reverse sort to move backward
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    private static string ReverseSortOrder(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        string[] fields = input.Split(',', StringSplitOptions.TrimEntries);

        for (int i = 0; i < fields.Length; i++)
        {
            string[] parts = fields[i].Split(OrderTerm.DELIMITER, StringSplitOptions.TrimEntries);
            string fieldName = parts[0];
            string sortOrder = parts.Length > 1 ? parts[1].ToLowerInvariant() : OrderTerm.ASC;

            // Reverse the sort order
            if (sortOrder == OrderTerm.ASC)
            {
                sortOrder = OrderTerm.DESC;
            }
            else
            {
                sortOrder = OrderTerm.ASC;
            }

            // Rebuild string — remove :asc, keep :desc
            fields[i] =
                sortOrder == OrderTerm.ASC
                    ? fieldName
                    : fieldName + OrderTerm.DELIMITER + sortOrder;
        }

        return string.Join(",", fields);
    }

    private static string RemoveAscOrder(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        string[] parts = input.Split(',', StringSplitOptions.TrimEntries);
        List<string> result = [];

        foreach (string part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                continue;
            }

            string[] tokens = part.Split(OrderTerm.DELIMITER, StringSplitOptions.TrimEntries);
            string property = tokens[0];

            if (tokens.Length == 1)
            {
                result.Add(property);
                continue;
            }

            string order = tokens[1].ToLowerInvariant();
            if (order == OrderTerm.ASC)
            {
                result.Add(property);
            }
            else
            {
                result.Add(property + OrderTerm.DELIMITER + order);
            }
        }

        return string.Join(",", result);
    }

    /// <summary>
    /// make sure that whether we reach the end of page.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="cursor"></param>
    /// <param name="theLast">the last item</param>
    ///  <param name="uniqueField"></param>
    /// <returns>true if we reach</returns>
    private static bool IsEndOfPage<T>(T cursor, T theLastItem, string uniqueField)
    {
        PropertyInfo cursorPropertyInfo = typeof(T).GetNestedPropertyInfo(uniqueField);
        object? cursorValue = typeof(T).GetNestedPropertyValue(uniqueField, cursor!);

        PropertyInfo destinationPropertyInfo = typeof(T).GetNestedPropertyInfo(uniqueField);
        object? theLastItemValue = typeof(T).GetNestedPropertyValue(uniqueField, theLastItem!);

        if (cursorPropertyInfo.PropertyType != destinationPropertyInfo.PropertyType)
        {
            throw new Exception($"{cursor} and {theLastItem} key is difference");
        }

        if (cursorValue == null || theLastItemValue == null)
        {
            throw new Exception($"{cursor} or {theLastItem} key is null");
        }

        return cursorPropertyInfo.PropertyType == typeof(Ulid)
            ? ((Ulid)cursorValue).CompareTo((Ulid)theLastItemValue) == 0
            : cursorValue == theLastItemValue;
    }

    /// <summary>
    /// Get all of properties that we need to put them into cursor
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="entity"></param>
    /// <param name="sort"></param>
    /// <returns></returns>
    private static Dictionary<string, object?> GetEncryptionProperties<T>(T entity, string sort)
    {
        Dictionary<string, object?> properties = [];
        List<string> sortFields = SortFields(sort);
        foreach (string field in sortFields)
        {
            if (!typeof(T).IsNestedPropertyValid(field))
            {
                throw new ArgumentException(
                    $"Invalid property name '{nameof(field)}' for type {typeof(T).Name}."
                );
            }

            object? value = typeof(T).GetNestedPropertyValue(field, entity!);
            properties.Add(field, value);
        }

        return properties;
    }

    private static CursorPayload? DecodeCursor(string cursor)
    {
        string stringCursor = cursor.DecompressString();
        var cursorPayload = JsonConvert.DeserializeObject<CursorPayload>(stringCursor);
        return cursorPayload;
    }

    private static string? EncodeCursor<T>(T? entity, string sort)
    {
        if (entity == null)
        {
            return null;
        }

        Dictionary<string, object?> properties = GetEncryptionProperties(entity, sort);
        CursorPayload cursorPayload = new(sort, properties);
        string json = JsonConvert.SerializeObject(cursorPayload, Formatting.Indented);
        return json.CompressString();
    }

    /// <summary>
    /// turn string sort to easy form
    /// </summary>
    /// <param name="sort"></param>
    /// <returns></returns>
    private static List<KeyValuePair<string, string>> TransformSort(string sort)
    {
        string[] fields = sort.Trim().Split(",", StringSplitOptions.TrimEntries);

        return
        [
            .. fields.Select(field =>
            {
                string[] orderFields = field.Split(OrderTerm.DELIMITER);

                if (orderFields.Length == 1)
                {
                    return new KeyValuePair<string, string>(orderFields[0], OrderTerm.ASC);
                }
                return new KeyValuePair<string, string>(orderFields[0], orderFields[1]);
            }),
        ];
    }

    /// <summary>
    /// get all of fields of string sort
    /// </summary>
    /// <param name="sort">string sort</param>
    /// <returns></returns>
    private static List<string> SortFields(string sort)
    {
        return [.. TransformSort(sort).Select(field => field.Key)];
    }
}

internal record CursorPayload(string Sort, Dictionary<string, object?> Properties);

internal record PaginationPayload<T>(
    IQueryable<T> Query,
    string? Cursor,
    bool IsPrevious,
    T First,
    T Last,
    string OriginalSort,
    string Sort,
    int Size,
    int ActualSize,
    string UniqueKey
);

internal record ProcessResultPayload<T>(
    IEnumerable<T> List,
    int RequestSize,
    T FirstFlag,
    T LastFlag
);

internal record PaginationResult<T>(
    IEnumerable<T> Data,
    int PageSize,
    string? Next = null,
    string? Pre = null
);
