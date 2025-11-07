# Dynamic Query Extension

### Support

- Search
- Sort
- Filter with LHS Bracket syntax
- Pagination with offset pagination and Cursor Pagination

# How to use

The First parameter is the key word
The Second one is The Specific fields that You wanna search
The Third is how deep this could search

```csharp

dbContext.User
    .Search("rose",["name"], 1)
    .ToListAsync();

```

# Filter

To do filter in this template, we use LHS Brackets.

LHS is the way to encode operators is the use of square brackets [] on the key name.

For example

```
GET api/v1/users?filter[dayOfBirth][$gt]="1990-10-01"
```

This example indicates filtering out users whose birthdays are after 1990/10/01

All support operations:

| Operator      | Description                         |
| ------------- | ----------------------------------- |
| $eq           | Equal                               |
| $eqi          | Equal (case-insensitive)            |
| $ne           | Not equal                           |
| $nei          | Not equal (case-insensitive)        |
| $in           | Included in an array                |
| $notin        | Not included in an array            |
| $lt           | Less than                           |
| $lte          | Less than or equal to               |
| $gt           | Greater than                        |
| $gte          | Greater than or equal to            |
| $between      | Is between                          |
| $notcontains  | Does not contain                    |
| $notcontainsi | Does not contain (case-insensitive) |
| $contains     | Contains                            |
| $containsi    | Contains (case-insensitive)         |
| $startswith   | Starts with                         |
| $endswith     | Ends with                           |

Some Examples:

```
GET /api/v1/users?filter[gender][$in][0]=1&filter[gender][$in][1]=2
```

```
GET /api/v1/users?filter[gender][$between][0]=1&filter[gender][$between][1]=2
```

```
GET /api/v1/users?filter[firstName][$contains]=abc
```

$and and $or operator:

```
GET /api/v1/users/filter[$and][0][firstName][$containsi]="sa"&filter[$and][1][lastName][$eq]="Tran"
```

```JSON
{
  "filter": {
    "$and": {
      "firstName": "sa",
      "lastName": "Tran"
    }
  }
}
```

```
GET /api/v1/users/filter[$or][0][$and][0][claims][claimValue][$eq]=admin&filter[$or][1][lastName][$eq]=Tran
```

```JSON
{
    "filter": {
        "$or": {
            "$and":{
                "claims": {
                    "claimValue": "admin"
                }
            },
            "lastName": "Tran"
        }
    }
}
```

For more examples and get better understand, you can visit

[https://docs.strapi.io/dev-docs/api/rest/filters-locale-publication#filtering](https://docs.strapi.io/dev-docs/api/rest/filters-locale-publication#filtering)\
[https://docs.strapi.io/dev-docs/api/rest/filters-locale-publication#complex-filtering](https://docs.strapi.io/dev-docs/api/rest/filters-locale-publication#complex-filtering)\
[https://docs.strapi.io/dev-docs/api/rest/filters-locale-publication#deep-filtering](https://docs.strapi.io/dev-docs/api/rest/filters-locale-publication#deep-filtering)

'Cause I designed filter input based on [Strapi filter](https://docs.strapi.io/dev-docs/api/rest/filters-locale-publication)



```csharp
public string[] GetFilterQueries(string query, string filterKey)
{
    string[] queryParams = query[1..].Split("&", StringSplitOptions.TrimEntries);
    return
    [
        .. queryParams.Where(param =>
            param.StartsWith(
                filterKey,
                StringComparison.OrdinalIgnoreCase
            )
        ),
    ];
}

var query = httpContext?.Request.QueryString.Value;
string[] stringQuery = GetFilterQueries(query, "Filter");

List<QueryResult> queries =
        [
            .. StringExtension.TransformStringQuery(stringQuery),
        ];
object filter = StringExtension.Parse(queries);
await dbContext.User.Filter(filter).ToListAsync();

```

### Sort

This example sorts users by name descending, then by age ascending when names are equal.

```csharp
await dbContext.User.sort("name:desc, age").ToListAsync();
```

### Pagination

Offset

```csharp
   await dbContext.User.ToPagedListAsync(1, 10);
```

Cursor

- Before : The previous Cursor
- After : The next cursor
- Size : Size of page
- Sort : User sort request
- UniqueSort : the property that is unique to make the mechanism work property, the default is Id:asc

```csharp
await dbContext.User.ToCursorPagedListAsync(request);
```
