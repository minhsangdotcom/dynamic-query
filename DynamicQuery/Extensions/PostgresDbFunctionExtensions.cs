using Microsoft.EntityFrameworkCore;

namespace DynamicQuery.Extensions;

public static class PostgresDbFunctionExtensions
{
    [DbFunction("unaccent", IsBuiltIn = true)]
    public static string Unaccent(string input) => throw new NotSupportedException();
}
