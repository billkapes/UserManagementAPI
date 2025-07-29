using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using System.Text.Json;


var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// In-memory user store
var users = new ConcurrentDictionary<int, User>();
var nextId = 1;

// User model

// Create user
app.MapPost("/users", async (HttpContext context) => {
    var user = await JsonSerializer.DeserializeAsync<User>(context.Request.Body);
    if (user is null || string.IsNullOrWhiteSpace(user.Name) || string.IsNullOrWhiteSpace(user.Email))
        return Results.BadRequest("Invalid user data.");
    var id = nextId++;
    var newUser = user with { Id = id };
    users[id] = newUser;
    return Results.Created($"/users/{id}", newUser);
});

// Get all users
app.MapGet("/users", () => users.Values);

// Get user by id
app.MapGet("/users/{id:int}", (int id) =>
    users.TryGetValue(id, out var user) ? Results.Ok(user) : Results.NotFound()
);

// Update user
app.MapPut("/users/{id:int}", async (int id, HttpContext context) => {
    if (!users.ContainsKey(id)) return Results.NotFound();
    var updated = await JsonSerializer.DeserializeAsync<User>(context.Request.Body);
    if (updated is null || string.IsNullOrWhiteSpace(updated.Name) || string.IsNullOrWhiteSpace(updated.Email))
        return Results.BadRequest("Invalid user data.");
    var newUser = updated with { Id = id };
    users[id] = newUser;
    return Results.Ok(newUser);
});

// Delete user
app.MapDelete("/users/{id:int}", (int id) =>
    users.TryRemove(id, out _) ? Results.NoContent() : Results.NotFound()
);

app.Run();

record User(int Id, string Name, string Email);
