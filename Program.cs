using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

var dbPath = builder.Configuration["DatabasePath"]
    ?? Path.Combine(app.Environment.ContentRootPath, "webapp.db");

// ======================================================
// BASE DE DATOS
// ======================================================

using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
{
    connection.Open();

    using var command = connection.CreateCommand();

    command.CommandText = """
        PRAGMA foreign_keys = ON;

        CREATE TABLE IF NOT EXISTS categories (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS tasks (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            title TEXT NOT NULL,
            completed INTEGER NOT NULL DEFAULT 0,
            category_id INTEGER NOT NULL,
            FOREIGN KEY (category_id)
                REFERENCES categories(id)
                ON DELETE CASCADE
        );
        """;

    command.ExecuteNonQuery();
}




SqliteConnection OpenDatabase()
{
    var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");

    connection.Open();

    using var command = connection.CreateCommand();

    command.CommandText = "PRAGMA foreign_keys = ON;";
    command.ExecuteNonQuery();

    return connection;
}

app.MapGet("/api/health", () => Results.Ok(new
{
    statusCode = 200,
    data = new
    {
        status = "healthy revisando"
    }
}));



app.MapGet("/api/tasks", () =>
{
    var tasks = new List<object>();

    using var connection = OpenDatabase();
    using var command = connection.CreateCommand();

    command.CommandText = """
        SELECT id, title, completed, category_id
        FROM tasks
        ORDER BY id;
        """;

    using var reader = command.ExecuteReader();

    while (reader.Read())
    {
        tasks.Add(new
        {
            id = reader.GetInt64(0),
            title = reader.GetString(1),
            completed = reader.GetInt64(2) == 1,
            categoryId = reader.GetInt64(3)
        });
    }

    return Results.Ok(new
    {
        statusCode = 200,
        data = tasks
    });
});


// ======================================================
// 2. GET - UNA TAREA
// ======================================================

app.MapGet("/api/tasks/{id}", (int id) =>
{
    using var connection = OpenDatabase();
    using var command = connection.CreateCommand();

    command.CommandText = """
        SELECT id, title, completed, category_id
        FROM tasks
        WHERE id = $id;
        """;

    command.Parameters.AddWithValue("$id", id);

    using var reader = command.ExecuteReader();

    if (!reader.Read())
    {
        return Results.NotFound(new
        {
            statusCode = 404,
            data = "Tarea no encontrada"
        });
    }

    var task = new
    {
        id = reader.GetInt64(0),
        title = reader.GetString(1),
        completed = reader.GetInt64(2) == 1,
        categoryId = reader.GetInt64(3)
    };

    return Results.Ok(new
    {
        statusCode = 200,
        data = task
    });
});


// ======================================================
// 3. POST - CREAR TAREA
// ======================================================

app.MapPost("/api/tasks", (CreateTask request) =>
{
    using var connection = OpenDatabase();

    using var categoryCommand = connection.CreateCommand();

    categoryCommand.CommandText = """
        SELECT COUNT(*)
        FROM categories
        WHERE id = $id;
        """;

    categoryCommand.Parameters.AddWithValue(
        "$id",
        request.CategoryId
    );

    var categoryExists =
        Convert.ToInt32(categoryCommand.ExecuteScalar()) > 0;

    if (!categoryExists)
    {
        return Results.BadRequest(new
        {
            statusCode = 400,
            data = "La categoría no existe"
        });
    }

    using var command = connection.CreateCommand();

    command.CommandText = """
        INSERT INTO tasks
        (
            title,
            completed,
            category_id
        )
        VALUES
        (
            $title,
            $completed,
            $categoryId
        );

        SELECT last_insert_rowid();
        """;

    command.Parameters.AddWithValue(
        "$title",
        request.Title
    );

    command.Parameters.AddWithValue(
        "$completed",
        request.Completed ? 1 : 0
    );

    command.Parameters.AddWithValue(
        "$categoryId",
        request.CategoryId
    );

    var id = Convert.ToInt64(
        command.ExecuteScalar()
    );

    return Results.Ok(new
    {
        statusCode = 200,
        data = new
        {
            id,
            message = "Tarea creada correctamente"
        }
    });
});


// ======================================================
// PUT - ACTUALIZAR TAREA
// ======================================================

app.MapPut("/api/tasks/{id}", (int id, UpdateTask request) =>
{
    using var connection = OpenDatabase();

    using var categoryCommand = connection.CreateCommand();

    categoryCommand.CommandText = """
        SELECT COUNT(*)
        FROM categories
        WHERE id = $id;
        """;

    categoryCommand.Parameters.AddWithValue("$id", request.CategoryId);

    var categoryExists = Convert.ToInt32(categoryCommand.ExecuteScalar()) > 0;

    if (!categoryExists)
    {
        return Results.BadRequest(new
        {
            statusCode = 400,
            data = "La categoría no existe"
        });
    }

    using var command = connection.CreateCommand();

    command.CommandText = """
        UPDATE tasks
        SET title = $title,
            completed = $completed,
            category_id = $categoryId
        WHERE id = $id;
        """;

    command.Parameters.AddWithValue("$id", id);
    command.Parameters.AddWithValue("$title", request.Title);
    command.Parameters.AddWithValue("$completed", request.Completed ? 1 : 0);
    command.Parameters.AddWithValue("$categoryId", request.CategoryId);

    if (command.ExecuteNonQuery() == 0)
    {
        return Results.NotFound(new
        {
            statusCode = 404,
            data = "Tarea no encontrada"
        });
    }

    return Results.Ok(new
    {
        statusCode = 200,
        data = "Tarea actualizada correctamente"
    });
});


// ======================================================
// 4. DELETE - ELIMINAR TAREA
// ======================================================

app.MapDelete("/api/tasks/{id}", (int id) =>
{
    using var connection = OpenDatabase();
    using var command = connection.CreateCommand();

    command.CommandText = """
        DELETE FROM tasks
        WHERE id = $id;
        """;

    command.Parameters.AddWithValue("$id", id);

    var rows = command.ExecuteNonQuery();

    if (rows == 0)
    {
        return Results.NotFound(new
        {
            statusCode = 404,
            data = "Tarea no encontrada"
        });
    }

    return Results.Ok(new
    {
        statusCode = 200,
        data = "Tarea eliminada correctamente"
    });
});


// ======================================================
// 5. GET - TODAS LAS CATEGORAS
// ======================================================

app.MapGet("/api/categories", () =>
{
    var categories = new List<object>();

    using var connection = OpenDatabase();
    using var command = connection.CreateCommand();

    command.CommandText = """
        SELECT id, name
        FROM categories
        ORDER BY id;
        """;

    using var reader = command.ExecuteReader();

    while (reader.Read())
    {
        categories.Add(new
        {
            id = reader.GetInt64(0),
            name = reader.GetString(1)
        });
    }

    return Results.Ok(new
    {
        statusCode = 200,
        data = categories
    });
});


// ======================================================
// 6. POST - CREAR CATEGORÍA
// ======================================================

app.MapPost("/api/categories", (CreateCategory request) =>
{
    using var connection = OpenDatabase();
    using var command = connection.CreateCommand();

    command.CommandText = """
        INSERT INTO categories (name)
        VALUES ($name);

        SELECT last_insert_rowid();
        """;

    command.Parameters.AddWithValue(
        "$name",
        request.Name
    );

    var id = Convert.ToInt64(
        command.ExecuteScalar()
    );

    return Results.Ok(new
    {
        statusCode = 200,
        data = new
        {
            id,
            message = "Categoría creada correctamente"
        }
    });
});


// ======================================================
// 7. DELETE - ELIMINAR CATEGORÍA
// ======================================================

app.MapDelete("/api/categories/{id}", (int id) =>
{
    using var connection = OpenDatabase();
    using var command = connection.CreateCommand();

    command.CommandText = """
        DELETE FROM categories
        WHERE id = $id;
        """;

    command.Parameters.AddWithValue("$id", id);

    var rows = command.ExecuteNonQuery();

    if (rows == 0)
    {
        return Results.NotFound(new
        {
            statusCode = 404,
            data = "Categoría no encontrada"
        });
    }

    return Results.Ok(new
    {
        statusCode = 200,
        data = "Categoría eliminada correctamente"
    });
});


// ======================================================
// 8. GET - TAREAS DE UNA CATEGORÍA
// ======================================================

app.MapGet("/api/categories/{id}/tasks", (int id) =>
{
    var tasks = new List<object>();

    using var connection = OpenDatabase();

    using var categoryCommand = connection.CreateCommand();

    categoryCommand.CommandText = """
        SELECT COUNT(*)
        FROM categories
        WHERE id = $id;
        """;

    categoryCommand.Parameters.AddWithValue(
        "$id",
        id
    );

    var categoryExists =
        Convert.ToInt32(categoryCommand.ExecuteScalar()) > 0;

    if (!categoryExists)
    {
        return Results.NotFound(new
        {
            statusCode = 404,
            data = "Categoría no encontrada"
        });
    }

    using var command = connection.CreateCommand();

    command.CommandText = """
        SELECT id, title, completed
        FROM tasks
        WHERE category_id = $categoryId
        ORDER BY id;
        """;

    command.Parameters.AddWithValue(
        "$categoryId",
        id
    );

    using var reader = command.ExecuteReader();

    while (reader.Read())
    {
        tasks.Add(new
        {
            id = reader.GetInt64(0),
            title = reader.GetString(1),
            completed = reader.GetInt64(2) == 1
        });
    }

    return Results.Ok(new
    {
        statusCode = 200,
        data = tasks
    });
});


// ======================================================
// 9. GET - BACKUP
// ======================================================

app.MapGet("/api/database/backup", () =>
{
    if (!File.Exists(dbPath))
    {
        return Results.NotFound(new
        {
            statusCode = 404,
            data = "La base de datos no existe"
        });
    }

    var bytes = File.ReadAllBytes(dbPath);

    return Results.File(
        bytes,
        "application/octet-stream",
        "webapp-backup.db"
    );
});


// ======================================================
// 10. DELETE - VACIAR BASE DE DATOS
// ======================================================

app.MapDelete("/api/database", () =>
{
    using var connection = OpenDatabase();
    using var command = connection.CreateCommand();

    command.CommandText = """
        DELETE FROM tasks;
        DELETE FROM categories;
        """;

    command.ExecuteNonQuery();

    return Results.Ok(new
    {
        statusCode = 200,
        data = "Base de datos vaciada correctamente"
    });
});


// ======================================================
// INICIAR APLICACIN
// ======================================================

app.Run();


// ======================================================
// MODELOS
// ======================================================

record CreateTask(
    string Title,
    bool Completed,
    int CategoryId
);

record CreateCategory(
    string Name
);

record UpdateTask(
    string Title,
    bool Completed,
    int CategoryId
);
