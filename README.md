# **EntityScribe**
**Lightweight C# language extension to map objects and type from database reader executions, memory allocation is optimized for performance. Contains a collection of methods, async and with yield returns, to map database reads to objects and types.**<br><br>

add "**using EntityScribe;**" to enable the language extension, then on any sql command class that derives from "DbDataSource".

Available on **NuGet** ([here](https://www.nuget.org/packages/TrishkaV.EntityScribe/)) and installed using the command:<br>
*dotnet add package TrishkaV.EntityScribe*<br><br>


**Examples:**<br>
".ReaderQueryAsync<int>()" returns a list of int, used with "SELECT n_size FROM items" where n_size is an integer.<br>
".ReaderQueryAsync<(string, int)>()" returns a tuple of string and int, used with "SELECT c_name, n_size FROM items" where n_size is an integer and c_name is a string.<br>
".ReaderQueryAsync<Item>()" returns a list of "Item" objects, the istances are mapped automatically.
<br><br>
All methods have a non-async version eg ".ReaderQuery<int>()" which return an IEnumerable that can be collected into a list ".ToList()" or iterated over as it "yields" the results and is more comfortable for large number of rows.

<br><br>
----------------------------------------
Map query results to a class<br><br>
<img src="https://user-images.githubusercontent.com/96583994/231211779-7b9966f2-a581-4297-b5bb-ca045a18cefa.png" width="600"><br><br><img src="https://user-images.githubusercontent.com/96583994/231212129-9e9e92be-3281-456f-a551-517c779e9be2.png" width="400"><br><br>
---------------------------------------
<br><br>

NOTE
Legally this comes with no warranty and you are using it at your own risk.

This library have been tested agaist real database extractions and objects of all the mentioned types its results hold correct.

If you find an issue with the results or implementation or an optimization could be made please feel free to contact me or issue a pull request.
You will be credited for any contribution.
