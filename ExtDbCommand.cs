using System.Data.Common;
using System.Reflection;
using System.Text.RegularExpressions;
using TypeDetect;

namespace EntityScribe;

/// <summary>
/// <para>Entity mapper for database reader language extension, memory allocation is optimized for performance.</para>
/// <para>Contains a collection of methods, async and with yield returns, to map database reads to objects and types.</para>
/// <para>Example:</para>
/// <para>".ReaderQueryAsync&lt; int &gt;()" returns a list of int, used with "SELECT n_size FROM items" where n_size is an integer.</para>
/// <para>".ReaderQueryAsync&lt; (string, int) &gt;()" returns a tuple of string and int, used with "SELECT c_name, n_size FROM items" where n_size is an integer and c_name is a string.</para>
/// <para>".ReaderQueryAsync&lt; Item &gt;()" returns a list of "Item" objects, the istances are mapped automatically.</para>
/// <para>All methods have a non-async version eg ".ReaderQuery&lt; int &gt;()" which return an IEnumerable that can be collected into a list ".ToList()" or iterated over as it "yields" the results and is more comfortable for large number of rows.</para>
/// </summary>
public static class ExtDbCommand
{
    #region DBCommand
    /// <summary>
    /// Run the query in the command parameter.
    /// <para>In the returning tuple, "errMessage" (string) is not empty only if isSuccess is false.</para>
    /// <para>WARNING: The command parameter is not disposed of, dispose of it in the caller method with the "using" keyword.</para>
    /// </summary>
    public static async Task<(bool isSuccess, int rowsAffected, string errMessage)> RunQuery(this DbCommand cmd, CancellationToken? cancellationToken = null)
    {
        cancellationToken ??= new CancellationToken();
        var rowsAffected = 0;
        try
        {
            rowsAffected = await cmd.ExecuteNonQueryAsync(cancellationToken.Value);
            return (isSuccess: true, rowsAffected, errMessage: string.Empty);
        }
        catch (Exception e) when (e is not TaskCanceledException)
        {
            return (isSuccess: false, rowsAffected, errMessage: $"It was not possible to perform the query \"{cmd.CommandText}\" with the error: \"{e.Message}\".\n {rowsAffected} rows were affected.");
        }
    }

    /// <summary>
    /// Async return a list of mapped objects indicated in the generic definition with the values of the rows from the query in
    /// the command parameter.
    /// It works with base types, classes, enums, and tuples.
    /// <para>NOTE: for classes, make sure the class has mutable fields beyond initialization e.g.
    /// "{ get; set; }" and not "{ get; init; }". </para>
    /// <para>NOTE: do NOT use structs, as value types a dynamic assign from the database reader row would result in
    /// new copies created every time. Structs will not be assigned to and not work with this method.
    /// </para>
    /// <para>Example:</para>
    /// <para>cmd.ReaderQueryAsync&lt; Users &gt;();</para>
    /// <para>will return a list of the class "Users", which fields will be mapped to the columns with the same name returned by the query.</para>
    /// <para>NOTE: consider using nullable data types e.g. "int?" instead of "int" since the most common use case is for
    /// null values to be present in a database. Using non nullable data types will result in default assignment for non matched fields.</para>
    /// <para>WARNING: The command parameter is not disposed of, dispose of it in the caller method with the "using" keyword.</para>
    /// <para>WARNING: This method returns a list with all the results, if you expect a large number of rows then consider
    /// using the "ReaderQuery()" method which yields an IEnumerable that can be iterated in a memory efficient way.</para>
    /// </summary>
    public static async Task<List<T>> ReaderQueryAsync<T>(this DbCommand cmd, CancellationToken? cancellationToken = null) where T : notnull
    {
        cancellationToken ??= new CancellationToken();

        var rowType = typeof(T);
        var rowsResult = new List<T>();

        using var rdr = cmd.ExecuteReader();

        var rowValues = new object[rdr.FieldCount];
        while (await rdr.ReadAsync(cancellationToken.Value) && rdr.GetValues(rowValues) != 0 && rowValues.All(x => x.GetType() == typeof(DBNull)))
            continue;

        if (!rdr.HasRows)
            return rowsResult;

        bool isBaseType;
        if (isBaseType = rdr.FieldCount == 1 && rowType.IsBaseType() && !rowType.IsEnum)
            rowsResult.Add((T)rdr.GetValue(0));
        else
            rowsResult.Add(rdr.RowToItem<T>());

        switch (isBaseType)
        {
            case true:
                while (await rdr.ReadAsync(cancellationToken.Value))
                    if (rdr.GetValue(0).GetType() != typeof(DBNull))
                        rowsResult.Add((T)rdr.GetValue(0));
                break;
            case false:
                while (await rdr.ReadAsync(cancellationToken.Value))
                    rowsResult.Add(rdr.RowToItem<T>());
                break;
        }

        return rowsResult;
    }

    /// <summary>
    /// Returns an IEnumerable of mapped objects indicated in the generic definition with the values of the rows from the query in
    /// the command parameter.
    /// It works with base types, classes, enums, and tuples.
    /// <para>NOTE: for classes, make sure the class has mutable fields beyond initialization e.g.
    /// "{ get; set; }" and not "{ get; init; }". </para>
    /// <para>NOTE: do NOT use structs, as value types a dynamic assign from the database reader row would result in
    /// new copies created every time. Structs will not be assigned to and not work with this method.
    /// </para>
    /// <para>Example:</para>
    /// <para>cmd.ReaderQuery&lt; Users &gt;();</para>
    /// <para>will return an IEnumerable of the class "Users", which fields will be mapped to the columns with the same name returned by the query.</para>
    /// <para>NOTE: consider using nullable data types e.g. "int?" instead of "int" since the most common use case is for
    /// null values to be present in a database. Using non nullable data types will result in default assignment for non matched fields.</para>
    /// <para>WARNING: The command parameter is not disposed of, dispose of it in the caller method with the "using" keyword.</para>
    /// </summary>
    public static IEnumerable<T> ReaderQuery<T>(this DbCommand cmd) where T : notnull
    {
        var rowType = typeof(T);

        using var rdr = cmd.ExecuteReader();

        var rowValues = new object[rdr.FieldCount];
        while (rdr.Read() && rdr.GetValues(rowValues) != 0 && rowValues.All(x => x.GetType() == typeof(DBNull)))
            continue;

        if (rdr.HasRows)
        {
            bool isBaseType;
            if (isBaseType = rdr.FieldCount == 1 && rowType.IsBaseType() && !rowType.IsEnum)
                yield return (T)rdr.GetValue(0);
            else
                yield return rdr.RowToItem<T>();

            switch (isBaseType)
            {
                case true:
                    while (rdr.Read())
                        if (rdr.GetValue(0).GetType() != typeof(DBNull))
                            yield return (T)rdr.GetValue(0);
                    break;
                case false:
                    while (rdr.Read())
                        yield return rdr.RowToItem<T>();
                    break;
            }
        }
    }
    #endregion DBCommand

    #region DbDataReader
    /// <summary>
    /// Returns a mapped object indicated in the generic definition with the values of the row in the reader parameter,
    /// it works with classes, tuples and enums.
    /// <para>NOTE: for classes, make sure the class has mutable fields beyond initialization e.g.
    /// "{ get; set; }" and not "{ get; init; }". </para>
    /// <para>NOTE: tuples can be used if the name of the columns is not relevant e.g. using (string, int) and calling its
    /// ".Item1", ".Item2" properties. Tuples support up to 8 values.</para>
    /// <para>NOTE: do NOT use structs, as value types a dynamic assign from the database reader row would result in
    /// new copies created every time. Structs will not be assigned to and not work with this method.</para>
    /// </summary>
    /// <exception cref="ArgumentException">The generic type passed does not match the row data retrieved, for classes
    /// make sure the field names and types match the data that you're retrieving, they don't need to be ordered,
    /// for tuples make sure to match the correct column type order.</exception>
    public static T RowToItem<T>(this DbDataReader reader) where T : notnull
    {
#pragma warning disable CS8600, CS8603, CS8602 // pragma "possible null reference for T": this is addressed in the method constraint
        dynamic row = Activator.CreateInstance<T>();

        /* tuple */
        if (row.GetType().FullName.StartsWith("System.ValueTuple"))
        {
            if (reader.FieldCount > 7)
                throw new ArgumentException($"The number of parameters retrieved or passed into the tuple is not valid, notice that tuples only support up to 8 elements while {row.GetType().GetRuntimeProperties().Last().ReflectedType?.GenericTypeArguments.Length} were passed, while {reader.FieldCount} fields were listed in the query.");

            for (byte i = 0; i < reader.FieldCount; i++)
            {
                dynamic fieldValue = reader.GetFieldType(i).Name switch
                {
                    _ when reader[i] == DBNull.Value => null,
                    "Byte" => reader.GetByte(i),
                    "Int16" => reader.GetInt16(i),
                    "Int32" => reader.GetInt32(i),
                    "Int64" => reader.GetInt64(i),
                    "Decimal" => reader.GetDecimal(i),
                    "Float" => reader.GetFloat(i),
                    "Double" => reader.GetDouble(i),
                    "Char" => reader.GetChar(i),
                    "String" => reader.GetString(i),
                    "Boolean" => reader.GetBoolean(i),
                    "DateTime" => reader.GetDateTime(i),
                    "Guid" => reader.GetGuid(i),
                    "Stream" => reader.GetStream(i),
                    _ => string.Empty
                };

                try
                {
                    TupleDynamicAssign(fieldValue, i, ref row);
                }
                catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException ex)
                {
                    throw new ArgumentException($"\"{ex.Message}\", if you wanted to accept null values (which is the most common use case) consider using a nullable type e.g. \"int?\" instead of \"int\".");
                }
            }

            return (T)row;
        }

        var rowType = typeof(T);
        var rowFields = rowType.GetFields();
        var rowFieldsRT = rowType.GetRuntimeFields();
        /* class and enum */
        if (rowFields.Length > 0 || rowFieldsRT.Any())
        {
            if (rowType.IsEnum && Enum.TryParse(rowType, reader.GetString(0), ignoreCase: true, out var enumValue))
                return (T)enumValue;

            var dbNullType = typeof(DBNull);
            var columns = reader.GetColumnSchema().Select(x => x.ColumnName).ToArray();
            foreach (var f in rowFields.Length > 0 ? rowFields : rowFieldsRT)
            {
                var fName = rowFields.Length > 0 ? f.Name : Regex.Match(f.Name, "(?<=<).*(?=>)").Value;

                if (!columns.Contains(fName))
                    continue;

                var fType = Nullable.GetUnderlyingType(f.FieldType) ?? f.FieldType;
                if (fType.IsEnum && Enum.TryParse(fType, reader[fName].ToString(), ignoreCase: true, out enumValue))
                    f.SetValue(row, enumValue);
                else
                {
#if DEBUG
                    var safeValue = reader[fName].GetType() == dbNullType ? null : Convert.ChangeType(reader[fName], fType);
                    f.SetValue(row, safeValue);
#else
                    f.SetValue(row, reader[fName].GetType() == dbNullType ? null : Convert.ChangeType(reader[fName], fType));
#endif
                }
            }

            return (T)row;
        }

        throw new ArgumentException($"The type passed \"{typeof(T)}\" is not a class containing properties and is not a tuple. Please control that the type passed matches the elements returned from the query.");
#pragma warning restore CS8600, CS8603, CS8602
    }

    private static void TupleDynamicAssign(dynamic value, byte position, ref dynamic tuple)
    {
        if (value == null)
            return;

        Type? fType = position switch
        {
            0 => tuple.Item1 == null ? null : Nullable.GetUnderlyingType(tuple.Item1.GetType()) ?? tuple.Item1.GetType(),
            1 => tuple.Item2 == null ? null : Nullable.GetUnderlyingType(tuple.Item2.GetType()) ?? tuple.Item2.GetType(),
            2 => tuple.Item3 == null ? null : Nullable.GetUnderlyingType(tuple.Item3.GetType()) ?? tuple.Item3.GetType(),
            3 => tuple.Item4 == null ? null : Nullable.GetUnderlyingType(tuple.Item4.GetType()) ?? tuple.Item4.GetType(),
            4 => tuple.Item5 == null ? null : Nullable.GetUnderlyingType(tuple.Item5.GetType()) ?? tuple.Item5.GetType(),
            5 => tuple.Item6 == null ? null : Nullable.GetUnderlyingType(tuple.Item6.GetType()) ?? tuple.Item6.GetType(),
            6 => tuple.Item7 == null ? null : Nullable.GetUnderlyingType(tuple.Item7.GetType()) ?? tuple.Item7.GetType(),
            7 => tuple.Item8 == null ? null : Nullable.GetUnderlyingType(tuple.Item8.GetType()) ?? tuple.Item8.GetType(),
            _ => null
        };
        if (fType?.IsEnum == true)
            Enum.TryParse(fType, value.ToString(), true, out value);

        switch (position)
        {
            case 0: tuple.Item1 = value; break;
            case 1: tuple.Item2 = value; break;
            case 2: tuple.Item3 = value; break;
            case 3: tuple.Item4 = value; break;
            case 4: tuple.Item5 = value; break;
            case 5: tuple.Item6 = value; break;
            case 6: tuple.Item7 = value; break;
            case 7: tuple.Item8 = value; break;
            default: return;
        }
    }
    #endregion DbDataReader
}
